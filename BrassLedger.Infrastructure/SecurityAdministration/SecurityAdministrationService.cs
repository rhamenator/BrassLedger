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
        var operators = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.CompanyId == companyId)
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);
        var operatorCounts = operators
            .GroupBy(user => user.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

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
                    operatorCounts.GetValueOrDefault(role.Name),
                    ParsePermissions(role.Permissions)))
                .ToArray(),
            Operators: operators
                .Select(user => new OperatorAccountSnapshot(
                    user.UserName,
                    user.DisplayName,
                    user.Email,
                    user.Role,
                    user.IsActive,
                    user.LastSuccessfulSignInUtc))
                .ToArray());
    }

    public async Task<SecurityOperationResult> CreateRoleAsync(CreateAccessRoleRequest request, CancellationToken cancellationToken = default)
    {
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
            IsActive = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return SecurityOperationResult.Success();
    }

    public async Task<SecurityOperationResult> CreateOperatorAsync(CreateOperatorRequest request, CancellationToken cancellationToken = default)
    {
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
        if (await dbContext.Users.AnyAsync(user => user.UserName == trimmedUserName, cancellationToken))
        {
            return SecurityOperationResult.Failure("That username is already in use.");
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
                    IsActive = true
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
        var claimValue = httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        if (Guid.TryParse(claimValue, out var companyId))
        {
            return companyId;
        }

        return await dbContext.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .Select(company => company.Id)
            .FirstAsync(cancellationToken);
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
