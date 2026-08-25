using System.Security.Claims;
using BrassLedger.Application.Security;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.SecurityAdministration;

public sealed class SecurityAdministrationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    IAccountActionService accountActionService,
    TimeProvider timeProvider) : ISecurityAdministrationService
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
        var deliveries = (await dbContext.SecurityEmailOutboxMessages
            .AsNoTracking()
            .Join(
                dbContext.AccountActionTokens.Where(action => action.CompanyId == companyId),
                message => message.AccountActionTokenId,
                action => action.Id,
                (message, action) => new { message, action.Purpose })
            .ToListAsync(cancellationToken))
            .OrderByDescending(item => item.message.CreatedAtUtc)
            .Take(20)
            .Select(item => new SecurityEmailDeliverySnapshot(
                item.message.Id,
                item.Purpose,
                MaskEmail(item.message.RecipientEmail),
                item.message.Status,
                item.message.AttemptCount,
                item.message.CreatedAtUtc,
                item.message.NextAttemptAtUtc,
                item.message.DeliveredAtUtc,
                item.message.LastError))
            .ToArray();

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
                .ToArray(),
            SecurityEmailDeliveryConfigured: accountActionService.EmailDeliveryConfigured,
            SecurityEmailDeliveries: deliveries);
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

    public async Task<SecurityOperationResult> InviteOperatorAsync(CreateOperatorInvitationRequest request, CancellationToken cancellationToken = default)
    {
        if (!CanManage(BrassLedgerPermissions.UserManage)) return SecurityOperationResult.Failure("You are not authorized to manage operator accounts.");
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        var httpContext = httpContextAccessor.HttpContext;
        var result = await accountActionService.IssueInvitationAsync(new AccountInvitationRequest(
            companyId,
            CurrentUserId(),
            request.UserName,
            request.DisplayName,
            request.Email,
            request.RoleName,
            httpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            httpContext?.Request.Headers.UserAgent.ToString() ?? string.Empty), cancellationToken);
        return result.Succeeded ? SecurityOperationResult.Success() : SecurityOperationResult.Failure(result.ErrorMessage);
    }

    public async Task<SecurityOperationResult> RetrySecurityEmailAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        if (!CanManage(BrassLedgerPermissions.UserManage)) return SecurityOperationResult.Failure("You are not authorized to manage security-email delivery.");
        if (!accountActionService.EmailDeliveryConfigured) return SecurityOperationResult.Failure("Security-email delivery is not configured.");
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(dbContext, cancellationToken);
        var record = await dbContext.SecurityEmailOutboxMessages
            .Join(
                dbContext.AccountActionTokens.Where(action => action.CompanyId == companyId),
                candidate => candidate.AccountActionTokenId,
                action => action.Id,
                (message, action) => new { message, action })
            .SingleOrDefaultAsync(candidate => candidate.message.Id == messageId, cancellationToken);
        if (record is null) return SecurityOperationResult.Failure("The security-email delivery record was not found for this company.");
        var message = record.message;
        if (message.DeliveredAtUtc is not null) return SecurityOperationResult.Failure("The SMTP server already accepted this message; it cannot be sent again from this record.");
        if (message.Status is not ("Failed" or "FailedPermanent")) return SecurityOperationResult.Failure("Only a failed security-email delivery can be retried.");
        if (string.IsNullOrEmpty(message.Body)) return SecurityOperationResult.Failure("The protected message body is no longer retained and cannot be retried.");
        var now = timeProvider.GetUtcNow();
        if (message.RequiresUsableAction && (record.action.ConsumedAtUtc is not null || record.action.ExpiresAtUtc <= now))
            return SecurityOperationResult.Failure("The one-use account action expired or was invalidated; issue a new invitation, verification, or reset action instead.");

        message.Status = "Pending";
        message.AttemptCount = 0;
        message.NextAttemptAtUtc = now;
        message.LeaseExpiresAtUtc = null;
        message.LastError = string.Empty;
        dbContext.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = CurrentUserId(),
            Action = "security.email-delivery-retried", EntityType = "SecurityEmailOutboxMessage", EntityId = message.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { message.AccountActionTokenId }),
            OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return SecurityOperationResult.Success();
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "protected recipient" : $"{email[0]}***{email[at..]}";
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
