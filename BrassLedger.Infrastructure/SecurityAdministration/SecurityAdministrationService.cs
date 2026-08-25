using System.Security.Claims;
using BrassLedger.Application.Security;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.SecurityAdministration;

public sealed class SecurityAdministrationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    IPasswordHasher<AppUser> passwordHasher) : ISecurityAdministrationService
{
    public async Task<SecurityAdministrationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);

        await EnsureBuiltInRolesAsync(dbContext, companyId, cancellationToken);

        var roles = await dbContext.AccessRoles
            .AsNoTracking()
            .Where(role => role.CompanyId == companyId && role.IsActive)
            .OrderByDescending(role => role.IsSystemRole)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);
        var operators = await dbContext.CompanyMemberships
            .AsNoTracking()
            .Where(membership => membership.CompanyId == companyId)
            .Join(
                dbContext.Users,
                membership => membership.UserId,
                user => user.Id,
                (membership, user) => new { membership, user })
            .OrderBy(item => item.user.UserName)
            .ToListAsync(cancellationToken);
        var operatorCounts = operators
            .Where(item => item.membership.IsActive && item.user.IsActive)
            .GroupBy(item => item.membership.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var roleMfaRequirements = roles.ToDictionary(role => role.Name, role => role.RequiresMfa, StringComparer.OrdinalIgnoreCase);

        return new SecurityAdministrationSnapshot(
            Permissions: BrassLedgerPermissions.Definitions
                .Select(permission => new PermissionDefinitionSnapshot(permission.Code, permission.Name, permission.Description))
                .ToArray(),
            Roles: roles
                .Select(role => new AccessRoleSnapshot(
                    role.Name,
                    role.Description,
                    role.TemplateCode,
                    role.IsSystemRole,
                    role.RequiresMfa,
                    operatorCounts.GetValueOrDefault(role.Name),
                    ParsePermissions(role.Permissions)))
                .ToArray(),
            Operators: operators
                .Select(item => new OperatorAccountSnapshot(
                    item.user.UserName,
                    item.user.DisplayName,
                    item.user.Email,
                    item.membership.Role,
                    item.user.IsActive && item.membership.IsActive,
                    item.user.MfaEnabled,
                    roleMfaRequirements.GetValueOrDefault(item.membership.Role),
                    item.user.LastSuccessfulSignInUtc))
                .ToArray());
    }

    public async Task<SecurityOperationResult> CreateRoleAsync(CreateAccessRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (!CanManage(BrassLedgerPermissions.RoleManage)) return SecurityOperationResult.Failure("You are not authorized to manage roles.");
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return SecurityOperationResult.Failure("Enter a role name.");
        }

        var normalizedPermissions = BrassLedgerRoleTemplates.NormalizePermissions(request.Permissions);
        if (normalizedPermissions.Count == 0)
        {
            return SecurityOperationResult.Failure("Select at least one permission.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);

        await EnsureBuiltInRolesAsync(dbContext, companyId, cancellationToken);

        var trimmedName = request.Name.Trim();
        if (await dbContext.AccessRoles.AnyAsync(
                role => role.CompanyId == companyId && role.Name == trimmedName,
                cancellationToken))
        {
            return SecurityOperationResult.Failure("A role with that name already exists.");
        }

        dbContext.AccessRoles.Add(new AccessRole
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Name = trimmedName,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? "Custom role created from the administration workspace."
                : request.Description.Trim(),
            TemplateCode = "custom",
            Permissions = string.Join('|', normalizedPermissions),
            IsSystemRole = false,
            IsActive = true,
            RequiresMfa = request.RequiresMfa
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return SecurityOperationResult.Success();
    }

    public async Task<SecurityOperationResult> SetRoleMfaRequirementAsync(
        string roleName,
        bool requiresMfa,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage(BrassLedgerPermissions.RoleManage)) return SecurityOperationResult.Failure("You are not authorized to manage roles.");
        if (string.IsNullOrWhiteSpace(roleName)) return SecurityOperationResult.Failure("Select a valid role.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        await EnsureBuiltInRolesAsync(dbContext, companyId, cancellationToken);
        var role = await dbContext.AccessRoles.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == companyId && candidate.IsActive && candidate.Name == roleName.Trim(),
            cancellationToken);
        if (role is null) return SecurityOperationResult.Failure("Select a valid role.");
        if (role.RequiresMfa == requiresMfa) return SecurityOperationResult.Success();

        role.RequiresMfa = requiresMfa;
        var affectedUserIds = await dbContext.CompanyMemberships
            .Where(membership => membership.CompanyId == companyId && membership.IsActive && membership.Role == role.Name)
            .Select(membership => membership.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var affectedUsers = await dbContext.Users.Where(user => affectedUserIds.Contains(user.Id)).ToListAsync(cancellationToken);
        foreach (var user in affectedUsers) user.SecurityStamp = Guid.NewGuid().ToString("N");
        dbContext.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = CurrentUserId(),
            Action = "security.role.mfa-requirement-changed",
            EntityType = "AccessRole",
            EntityId = role.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { role.Name, RequiresMfa = requiresMfa, AffectedOperatorCount = affectedUsers.Count }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return SecurityOperationResult.Success();
    }

    public async Task<SecurityOperationResult> CreateOperatorAsync(CreateOperatorRequest request, CancellationToken cancellationToken = default)
    {
        if (!CanManage(BrassLedgerPermissions.UserManage)) return SecurityOperationResult.Failure("You are not authorized to manage operator accounts.");
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return SecurityOperationResult.Failure("Enter a username.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return SecurityOperationResult.Failure("Enter a display name.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return SecurityOperationResult.Failure("Enter an email address.");
        }

        if (string.IsNullOrWhiteSpace(request.RoleName))
        {
            return SecurityOperationResult.Failure("Select a role.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12)
        {
            return SecurityOperationResult.Failure("Choose a password with at least 12 characters.");
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return SecurityOperationResult.Failure("The password confirmation does not match.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);

        await EnsureBuiltInRolesAsync(dbContext, companyId, cancellationToken);

        var trimmedUserName = request.UserName.Trim();
        if (await dbContext.Users.AnyAsync(
                user => user.UserName.ToUpper() == trimmedUserName.ToUpper(),
                cancellationToken))
        {
            return SecurityOperationResult.Failure("That username is already in use (usernames are case-insensitive).");
        }

        var role = await dbContext.AccessRoles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.IsActive && candidate.Name == request.RoleName.Trim(), cancellationToken);
        if (role is null)
        {
            return SecurityOperationResult.Failure("Select a valid role.");
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserName = trimmedUserName,
            DisplayName = request.DisplayName.Trim(),
            Email = request.Email.Trim(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role.Name,
            IsActive = true,
            LastPasswordChangedUtc = DateTimeOffset.UtcNow
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);
        dbContext.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = companyId,
            Role = role.Name,
            IsOwner = false,
            IsActive = true,
            GrantedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return SecurityOperationResult.Success();
    }

    public static async Task EnsureBuiltInRolesAsync(BrassLedgerDbContext dbContext, Guid companyId, CancellationToken cancellationToken = default)
    {
        var existingRoles = await dbContext.AccessRoles
            .Where(role => role.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var hasChanges = false;
        foreach (var template in BrassLedgerRoleTemplates.BuiltIn)
        {
            var existingRole = existingRoles.FirstOrDefault(role => string.Equals(role.Name, template.Name, StringComparison.OrdinalIgnoreCase));
            var normalizedPermissions = BrassLedgerRoleTemplates.NormalizePermissions(template.Permissions);
            var serializedPermissions = string.Join('|', normalizedPermissions);

            if (existingRole is null)
            {
                dbContext.AccessRoles.Add(new AccessRole
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Name = template.Name,
                    Description = template.Description,
                    TemplateCode = template.TemplateCode,
                    Permissions = serializedPermissions,
                    IsSystemRole = true,
                    IsActive = true,
                    RequiresMfa = template.RequiresMfa
                });
                hasChanges = true;
                continue;
            }

            if (!existingRole.IsSystemRole
                || !string.Equals(existingRole.TemplateCode, template.TemplateCode, StringComparison.Ordinal)
                || !string.Equals(existingRole.Permissions, serializedPermissions, StringComparison.Ordinal)
                || !string.Equals(existingRole.Description, template.Description, StringComparison.Ordinal)
                || !existingRole.IsActive)
            {
                existingRole.IsSystemRole = true;
                existingRole.TemplateCode = template.TemplateCode;
                existingRole.Description = template.Description;
                existingRole.Permissions = serializedPermissions;
                existingRole.IsActive = true;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Guid> ResolveCompanyIdAsync(BrassLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var claimValue = httpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claimValue, out var companyId))
        {
            return companyId;
        }

        if (httpContext is not null) throw new UnauthorizedAccessException("An authenticated company context is required.");
        return await dbContext.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .Select(company => company.Id)
            .FirstAsync(cancellationToken);
    }

    private bool CanManage(string permission)
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user is null
            || (!user.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
                && (user.IsInRole("Administrator")
                    || user.IsInRole("Owner/CEO")
                    || user.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));
    }

    private Guid? CurrentUserId()
    {
        var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static IReadOnlyList<string> ParsePermissions(string permissions)
    {
        return permissions
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
