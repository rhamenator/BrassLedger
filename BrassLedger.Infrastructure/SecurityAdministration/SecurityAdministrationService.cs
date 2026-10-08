using System.Security.Claims;
using System.Security.Cryptography;
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
    IAccountActionService accountActionService,
    IPasswordHasher<AppUser> passwordHasher,
    TimeProvider timeProvider) : ISecurityAdministrationService
{
    private static readonly HashSet<string> MfaRecoveryVerificationMethods =
    [
        "In-person identity verification",
        "Video verification with known employee",
        "Manager and HR attestation",
        "Company-approved recovery procedure"
    ];

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
                    item.user.Id,
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

    public async Task<SecurityOperationResult> ResetOperatorMfaAsync(
        AdministratorMfaRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage(BrassLedgerPermissions.UserManage))
            return SecurityOperationResult.Failure("You are not authorized to perform administrator MFA recovery.");
        if (!string.Equals(
                httpContextAccessor.HttpContext?.User.FindFirstValue(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType),
                "mfa",
                StringComparison.Ordinal))
            return SecurityOperationResult.Failure("Sign in with multi-factor authentication before performing administrator MFA recovery.");
        var administratorId = CurrentUserId();
        if (!administratorId.HasValue || request.TargetUserId == Guid.Empty || request.TargetUserId == administratorId)
            return SecurityOperationResult.Failure("Administrator MFA recovery cannot be used for your own account.");
        if (string.IsNullOrWhiteSpace(request.CurrentAdministratorPassword) || request.CurrentAdministratorPassword.Length > 1024)
            return SecurityOperationResult.Failure("Re-enter your current administrator password.");
        var verificationMethod = request.VerificationMethod?.Trim() ?? string.Empty;
        if (!MfaRecoveryVerificationMethods.Contains(verificationMethod))
            return SecurityOperationResult.Failure("Select the documented identity-verification method used for this recovery.");
        var caseReference = request.CaseReference?.Trim() ?? string.Empty;
        if (caseReference.Length is < 8 or > 200 || caseReference.Any(char.IsControl))
            return SecurityOperationResult.Failure("Enter a recovery case or incident reference of 8 to 200 characters.");

        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(db, cancellationToken);
        var administrator = await db.Users.SingleOrDefaultAsync(user => user.Id == administratorId && user.IsActive, cancellationToken);
        var administratorMembership = administrator is null ? null : await db.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(
            membership => membership.UserId == administrator.Id && membership.CompanyId == companyId && membership.IsActive,
            cancellationToken);
        if (administrator is null || administratorMembership is null || string.IsNullOrWhiteSpace(administrator.PasswordHash))
            return SecurityOperationResult.Failure("The current administrator password was not accepted.");
        var requestIpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var requestUserAgent = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        if (administrator.LockoutEndUtc is not null && administrator.LockoutEndUtc > now)
            return SecurityOperationResult.Failure("The current administrator password was not accepted.");
        var passwordVerification = passwordHasher.VerifyHashedPassword(administrator, administrator.PasswordHash, request.CurrentAdministratorPassword);
        if (passwordVerification == PasswordVerificationResult.Failed)
        {
            administrator.FailedSignInCount += 1;
            administrator.LastFailedSignInUtc = now;
            if (administrator.FailedSignInCount >= BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts)
                administrator.LockoutEndUtc = now.AddMinutes(BrassLedgerAuthenticationDefaults.LockoutMinutes);
            db.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
            {
                Id = Guid.NewGuid(), UserId = administrator.Id, CompanyId = companyId, UserName = administrator.UserName,
                EventType = "mfa_administrator_recovery_reauthentication_failed", Succeeded = false, OccurredUtc = now,
                IpAddress = requestIpAddress, UserAgent = requestUserAgent,
                Detail = "The current administrator password was rejected during an administrator MFA-recovery attempt."
            });
            await db.SaveChangesAsync(cancellationToken);
            return SecurityOperationResult.Failure("The current administrator password was not accepted.");
        }
        if (passwordVerification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            administrator.PasswordHash = passwordHasher.HashPassword(administrator, request.CurrentAdministratorPassword);
            administrator.LastPasswordChangedUtc = now;
        }
        administrator.FailedSignInCount = 0;
        administrator.LastFailedSignInUtc = null;
        administrator.LockoutEndUtc = null;

        var targetRecord = await db.CompanyMemberships
            .Where(membership => membership.UserId == request.TargetUserId && membership.CompanyId == companyId && membership.IsActive)
            .Join(db.Users.Where(user => user.IsActive), membership => membership.UserId, user => user.Id, (membership, user) => new { membership, user })
            .SingleOrDefaultAsync(cancellationToken);
        if (targetRecord is null || !targetRecord.user.MfaEnabled)
        {
            await AuditMfaRecoveryDenialAsync(db, administrator, companyId, "The selected active MFA-enabled operator was not available in the current company.", now, requestIpAddress, requestUserAgent, cancellationToken);
            return SecurityOperationResult.Failure("The active MFA-enabled operator was not found in this company.");
        }
        var confirmedUserName = request.ConfirmUserName?.Trim() ?? string.Empty;
        if (!string.Equals(targetRecord.user.UserName, confirmedUserName, StringComparison.Ordinal))
        {
            await AuditMfaRecoveryDenialAsync(db, administrator, companyId, "The exact target username confirmation did not match.", now, requestIpAddress, requestUserAgent, cancellationToken);
            return SecurityOperationResult.Failure("The confirmation username did not exactly match the selected operator.");
        }
        if (targetRecord.membership.IsOwner && !administratorMembership.IsOwner)
        {
            await AuditMfaRecoveryDenialAsync(db, administrator, companyId, "A non-owner attempted to recover a company owner account.", now, requestIpAddress, requestUserAgent, cancellationToken);
            return SecurityOperationResult.Failure("Only another company owner can authorize MFA recovery for an owner account.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.MfaRecoveryCodes.RemoveRange(await db.MfaRecoveryCodes.Where(code => code.UserId == targetRecord.user.Id).ToListAsync(cancellationToken));
        db.MfaSignInChallenges.RemoveRange(await db.MfaSignInChallenges.Where(challenge => challenge.UserId == targetRecord.user.Id).ToListAsync(cancellationToken));
        foreach (var action in await db.AccountActionTokens.Where(action => action.UserId == targetRecord.user.Id && action.ConsumedAtUtc == null).ToListAsync(cancellationToken))
            action.ConsumedAtUtc = now;
        if (db.Database.IsSqlite())
        {
            foreach (var session in await db.UserSessions.Where(session => session.UserId == targetRecord.user.Id && session.RevokedAtUtc == null).ToListAsync(cancellationToken))
                session.RevokedAtUtc = now;
        }
        else
        {
            await db.UserSessions.Where(session => session.UserId == targetRecord.user.Id && session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAtUtc, now), cancellationToken);
        }

        targetRecord.user.MfaEnabled = false;
        targetRecord.user.MfaSecret = string.Empty;
        targetRecord.user.MfaEnrolledAtUtc = null;
        targetRecord.user.MfaLastAcceptedTimeStep = null;
        targetRecord.user.MfaFailedAttemptCount = 0;
        targetRecord.user.MfaLockoutEndUtc = null;
        targetRecord.user.SecurityStamp = Guid.NewGuid().ToString("N");
        var notificationEmail = string.Empty;
        var securityNotificationQueued = accountActionService.EmailDeliveryConfigured
            && AccountEmailIdentity.TryNormalize(targetRecord.user.Email, out notificationEmail, out _);
        if (securityNotificationQueued)
        {
            var notificationAction = new AccountActionToken
            {
                Id = Guid.NewGuid(), UserId = targetRecord.user.Id, CompanyId = companyId,
                Purpose = "MfaAdministratorRecoveryNotice", TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                SecurityStamp = targetRecord.user.SecurityStamp, CreatedAtUtc = now, ExpiresAtUtc = now,
                ConsumedAtUtc = now, CreatedByUserId = administrator.Id, RequestedIpAddress = requestIpAddress
            };
            db.AccountActionTokens.Add(notificationAction);
            db.SecurityEmailOutboxMessages.Add(new SecurityEmailOutboxMessage
            {
                Id = Guid.NewGuid(), AccountActionTokenId = notificationAction.Id, RequiresUsableAction = false,
                RecipientEmail = notificationEmail, Subject = "Your BrassLedger multi-factor authentication was reset",
                Body = $"Hello {targetRecord.user.DisplayName},\n\nAn authorized administrator reset multi-factor authentication for your BrassLedger operator account. Every signed-in browser, authenticator secret, recovery code, pending challenge, and account-action link was invalidated.\n\nSign in again and enroll a new authenticator before continuing if your role requires MFA. Contact your company immediately if you did not complete identity verification for this recovery.\n\nRecovery reference: {caseReference}",
                Status = "Pending", CreatedAtUtc = now, NextAttemptAtUtc = now
            });
        }
        db.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(), UserId = targetRecord.user.Id, CompanyId = companyId, UserName = targetRecord.user.UserName,
            EventType = "mfa_administrator_recovery", Succeeded = true, OccurredUtc = now,
            IpAddress = requestIpAddress,
            UserAgent = requestUserAgent,
            Detail = "An authorized administrator cleared MFA after documented identity verification; all sessions and recovery credentials were invalidated."
        });
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = administrator.Id,
            Action = "security.operator.mfa-recovered", EntityType = "AppUser", EntityId = targetRecord.user.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { VerificationMethod = verificationMethod, CaseReference = caseReference, TargetRole = targetRecord.membership.Role, SecurityNotificationQueued = securityNotificationQueued }),
            OccurredAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SecurityOperationResult.Success();
    }

    private static async Task AuditMfaRecoveryDenialAsync(
        BrassLedgerDbContext db,
        AppUser administrator,
        Guid companyId,
        string detail,
        DateTimeOffset now,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        db.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(), UserId = administrator.Id, CompanyId = companyId, UserName = administrator.UserName,
            EventType = "mfa_administrator_recovery_denied", Succeeded = false, OccurredUtc = now,
            IpAddress = ipAddress, UserAgent = userAgent, Detail = detail
        });
        await db.SaveChangesAsync(cancellationToken);
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
