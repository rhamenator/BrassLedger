using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BrassLedger.Infrastructure.Auth;

public interface IAccountActionService
{
    bool EmailDeliveryConfigured { get; }
    Task<AccountInvitationResult> IssueInvitationAsync(AccountInvitationRequest request, CancellationToken cancellationToken = default);
    Task<AccountActionRequestResult> RequestEmailVerificationAsync(Guid userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task<AccountActionRequestResult> ChangeEmailAsync(Guid userId, string newEmail, string currentPassword, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(string identifier, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task<AccountActionSnapshot?> GetActionAsync(string token, CancellationToken cancellationToken = default);
    Task<AccountActionCompletionResult> CompleteAsync(string token, string newPassword, string confirmPassword, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task<AccountActionCompletionResult> CompleteEmailVerificationAsync(string token, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
}

public sealed record AccountInvitationRequest(
    Guid CompanyId,
    Guid? CreatedByUserId,
    string UserName,
    string DisplayName,
    string Email,
    string RoleName,
    string IpAddress,
    string UserAgent);

public sealed record AccountInvitationResult(bool Succeeded, string ErrorMessage)
{
    public static AccountInvitationResult Success() => new(true, string.Empty);
    public static AccountInvitationResult Failure(string message) => new(false, message);
}

public sealed record AccountActionRequestResult(bool Succeeded, string ErrorMessage)
{
    public static AccountActionRequestResult Success() => new(true, string.Empty);
    public static AccountActionRequestResult Failure(string message) => new(false, message);
}

public sealed record AccountActionSnapshot(string Purpose, string MaskedEmail, DateTimeOffset ExpiresAtUtc);

public enum AccountActionCompletionOutcome
{
    Succeeded,
    InvalidOrExpired,
    InvalidPassword
}

public sealed record AccountActionCompletionResult(AccountActionCompletionOutcome Outcome, string Purpose = "");

public sealed class AccountActionService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IPasswordHasher<AppUser> passwordHasher,
    ISecurityEmailTransport emailTransport,
    IOptions<AccountEmailOptions> emailOptions,
    TimeProvider timeProvider) : IAccountActionService
{
    private const string InvitationPurpose = "Invitation";
    private const string EmailVerificationPurpose = "EmailVerification";
    private const string PasswordResetPurpose = "PasswordReset";
    private readonly AccountEmailOptions _emailOptions = emailOptions.Value;

    public bool EmailDeliveryConfigured => emailTransport.IsConfigured;

    public async Task<AccountInvitationResult> IssueInvitationAsync(AccountInvitationRequest request, CancellationToken cancellationToken = default)
    {
        if (!EmailDeliveryConfigured) return AccountInvitationResult.Failure("Configure verified HTTPS security-email delivery before inviting an operator.");
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.RoleName))
            return AccountInvitationResult.Failure("Enter a username, display name, email address, and role.");
        if (!AccountEmailIdentity.TryNormalize(request.Email, out var normalizedEmail, out var emailLookupHash)) return AccountInvitationResult.Failure("Enter a valid email address.");
        if (request.UserName.Trim().Length > 100 || request.DisplayName.Trim().Length > 200)
            return AccountInvitationResult.Failure("The username or display name is too long.");

        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var role = await db.AccessRoles.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.CompanyId == request.CompanyId && candidate.IsActive && candidate.Name == request.RoleName.Trim(), cancellationToken);
        if (role is null) return AccountInvitationResult.Failure("Select a valid role.");
        var normalizedUserName = request.UserName.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.UserName.ToUpper() == normalizedUserName, cancellationToken))
            return AccountInvitationResult.Failure("That username is already in use.");
        if (await db.Users.AnyAsync(user => user.EmailLookupHash == emailLookupHash, cancellationToken))
            return AccountInvitationResult.Failure("That email address is already assigned to an operator.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            UserName = request.UserName.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Email = normalizedEmail,
            EmailLookupHash = emailLookupHash,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role.Name,
            IsActive = false
        };
        var tokenValue = GenerateToken();
        var action = CreateActionToken(user, request.CompanyId, InvitationPurpose, tokenValue, now, now.AddHours(Math.Clamp(_emailOptions.InvitationLifetimeHours, 1, 168)), request.CreatedByUserId, request.IpAddress);
        var link = BuildActionLink(tokenValue);
        db.Users.Add(user);
        db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(), UserId = user.Id, CompanyId = request.CompanyId, Role = role.Name,
            IsOwner = false, IsActive = false, GrantedAtUtc = now
        });
        db.AccountActionTokens.Add(action);
        db.SecurityEmailOutboxMessages.Add(CreateOutboxMessage(
            action.Id,
            user.Email,
            "Your BrassLedger operator invitation",
            $"Hello {user.DisplayName},\n\nAn authorized BrassLedger administrator invited you to the {role.Name} role. Use this one-time link within {_emailOptions.InvitationLifetimeHours} hours to verify this email address and choose your password:\n\n{link}\n\nIf you did not expect this invitation, do not use the link and contact the business directly.",
            now));
        db.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = request.CompanyId, UserId = request.CreatedByUserId,
            Action = "security.operator.invited", EntityType = "AppUser", EntityId = user.Id,
            DetailJson = System.Text.Json.JsonSerializer.Serialize(new { user.UserName, user.DisplayName, Role = role.Name, Delivery = "Queued", ExpiresAtUtc = action.ExpiresAtUtc }),
            OccurredAtUtc = now
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return AccountInvitationResult.Failure("That username or email address is already assigned to an operator.");
        }
        return AccountInvitationResult.Success();
    }

    public async Task<AccountActionRequestResult> RequestEmailVerificationAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        if (!EmailDeliveryConfigured) return AccountActionRequestResult.Failure("Security-email delivery is not configured.");
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return AccountActionRequestResult.Failure("The active operator account was not found.");
        if (user.EmailConfirmedAtUtc is not null) return AccountActionRequestResult.Failure("This email address is already verified.");
        if (!AccountEmailIdentity.TryNormalize(user.Email, out var normalizedEmail, out var lookupHash))
            return AccountActionRequestResult.Failure("The account email address is invalid. Ask an administrator to correct it.");
        if (user.EmailLookupHash is null)
        {
            user.Email = normalizedEmail;
            user.EmailLookupHash = lookupHash;
        }

        var outstanding = await db.AccountActionTokens
            .Where(action => action.UserId == user.Id && action.Purpose == EmailVerificationPurpose && action.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (outstanding.Any(action => action.CreatedAtUtc > now.AddMinutes(-5))) return AccountActionRequestResult.Success();
        foreach (var prior in outstanding) prior.ConsumedAtUtc = now;

        var tokenValue = GenerateToken();
        var action = CreateActionToken(
            user,
            user.CompanyId,
            EmailVerificationPurpose,
            tokenValue,
            now,
            now.AddHours(Math.Clamp(_emailOptions.EmailVerificationLifetimeHours, 1, 168)),
            user.Id,
            ipAddress);
        db.AccountActionTokens.Add(action);
        db.SecurityEmailOutboxMessages.Add(CreateOutboxMessage(
            action.Id,
            user.Email,
            "Verify your BrassLedger email address",
            $"Verify the email address for your BrassLedger operator account using this one-time link within {_emailOptions.EmailVerificationLifetimeHours} hours:\n\n{BuildActionLink(tokenValue)}\n\nIf you did not request this message, do not use the link and contact your administrator.",
            now));
        db.AuthenticationAuditEntries.Add(CreateAuthenticationAudit(
            user,
            "email_verification_requested",
            true,
            ipAddress,
            userAgent,
            "A one-time email-verification message was queued for the operator email address.",
            now));
        await db.SaveChangesAsync(cancellationToken);
        return AccountActionRequestResult.Success();
    }

    public async Task<AccountActionRequestResult> ChangeEmailAsync(
        Guid userId,
        string newEmail,
        string currentPassword,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        if (!EmailDeliveryConfigured) return AccountActionRequestResult.Failure("Security-email delivery is not configured.");
        if (!AccountEmailIdentity.TryNormalize(newEmail, out var normalizedEmail, out var lookupHash))
            return AccountActionRequestResult.Failure("Enter a valid replacement email address.");
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return AccountActionRequestResult.Failure("The active operator account was not found.");
        if (string.IsNullOrWhiteSpace(currentPassword) || currentPassword.Length > 1024
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            db.AuthenticationAuditEntries.Add(CreateAuthenticationAudit(
                user, "email_change_rejected", false, ipAddress, userAgent,
                "The account email change was rejected because password reauthentication failed.", now));
            await db.SaveChangesAsync(cancellationToken);
            return AccountActionRequestResult.Failure("The current password was not correct.");
        }
        if (string.Equals(user.EmailLookupHash, lookupHash, StringComparison.Ordinal))
            return AccountActionRequestResult.Failure(user.EmailConfirmedAtUtc is null
                ? "That is already the unverified account email address. Send a new verification message instead."
                : "That is already the verified account email address.");
        if (await db.Users.AnyAsync(candidate => candidate.Id != user.Id && candidate.EmailLookupHash == lookupHash, cancellationToken))
            return AccountActionRequestResult.Failure("That email address is already assigned to an operator.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var priorVerifiedEmail = user.EmailConfirmedAtUtc is not null ? user.Email : null;
        user.Email = normalizedEmail;
        user.EmailLookupHash = lookupHash;
        user.EmailConfirmedAtUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var outstanding = await db.AccountActionTokens.Where(action => action.UserId == user.Id && action.ConsumedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var prior in outstanding) prior.ConsumedAtUtc = now;

        var tokenValue = GenerateToken();
        var action = CreateActionToken(
            user,
            user.CompanyId,
            EmailVerificationPurpose,
            tokenValue,
            now,
            now.AddHours(Math.Clamp(_emailOptions.EmailVerificationLifetimeHours, 1, 168)),
            user.Id,
            ipAddress);
        db.AccountActionTokens.Add(action);
        db.SecurityEmailOutboxMessages.Add(CreateOutboxMessage(
            action.Id,
            user.Email,
            "Verify your new BrassLedger email address",
            $"The email address for your BrassLedger operator account was changed after password reauthentication. Verify the replacement address using this one-time link within {_emailOptions.EmailVerificationLifetimeHours} hours:\n\n{BuildActionLink(tokenValue)}\n\nIf you did not make this change, contact your administrator immediately.",
            now));
        if (priorVerifiedEmail is not null)
        {
            db.SecurityEmailOutboxMessages.Add(CreateOutboxMessage(
                action.Id,
                priorVerifiedEmail,
                "Your BrassLedger email address was changed",
                "The email address for your BrassLedger operator account was changed after password reauthentication, and all prior sessions were invalidated. If you did not make this change, contact your administrator immediately.",
                now,
                requiresUsableAction: false));
        }
        db.AuthenticationAuditEntries.Add(CreateAuthenticationAudit(
            user,
            "email_change_requested",
            true,
            ipAddress,
            userAgent,
            "The operator changed the account email address after password reauthentication; sessions were invalidated and verification was queued.",
            now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountActionRequestResult.Success();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountActionRequestResult.Failure("That email address is already assigned to an operator.");
        }
    }

    public async Task RequestPasswordResetAsync(
        string identifier,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!EmailDeliveryConfigured || string.IsNullOrWhiteSpace(identifier) || identifier.Length > 320) return;
            var now = timeProvider.GetUtcNow();
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var normalized = identifier.Trim();
            var normalizedUserName = normalized.ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.IsActive && candidate.UserName.ToUpper() == normalizedUserName, cancellationToken);
            if (user is null && AccountEmailIdentity.TryNormalize(normalized, out _, out var lookupHash))
                user = await db.Users.SingleOrDefaultAsync(candidate => candidate.IsActive && candidate.EmailLookupHash == lookupHash, cancellationToken);
            if (user is null || user.EmailConfirmedAtUtc is null || !AccountEmailIdentity.TryNormalize(user.Email, out _, out _)) return;

            var outstanding = await db.AccountActionTokens
                .Where(action => action.UserId == user.Id && action.Purpose == PasswordResetPurpose && action.ConsumedAtUtc == null)
                .ToListAsync(cancellationToken);
            var recentlyIssued = outstanding.Any(action => action.CreatedAtUtc > now.AddMinutes(-5));
            if (recentlyIssued) return;
            foreach (var prior in outstanding) prior.ConsumedAtUtc = now;
            var tokenValue = GenerateToken();
            var action = CreateActionToken(user, user.CompanyId, PasswordResetPurpose, tokenValue, now, now.AddMinutes(Math.Clamp(_emailOptions.PasswordResetLifetimeMinutes, 10, 120)), null, ipAddress);
            db.AccountActionTokens.Add(action);
            db.SecurityEmailOutboxMessages.Add(CreateOutboxMessage(
                action.Id,
                user.Email,
                "Reset your BrassLedger password",
                $"A password reset was requested for your BrassLedger operator account. Use this one-time link within {_emailOptions.PasswordResetLifetimeMinutes} minutes:\n\n{BuildActionLink(tokenValue)}\n\nIf you did not request this change, do not use the link. Your password has not been changed.",
                now));
            db.AuthenticationAuditEntries.Add(CreateAuthenticationAudit(user, "password_reset_requested", true, ipAddress, userAgent, "A password-reset message was queued for the verified operator email address.", now));
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            var remaining = TimeSpan.FromMilliseconds(300) - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
        }
    }

    public async Task<AccountActionSnapshot?> GetActionAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!IsPlausibleToken(token)) return null;
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hash = HashToken(token);
        var action = await db.AccountActionTokens.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (action is null || action.ConsumedAtUtc is not null || action.ExpiresAtUtc <= now) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == action.UserId, cancellationToken);
        if (user is null || !string.Equals(user.SecurityStamp, action.SecurityStamp, StringComparison.Ordinal)) return null;
        if (action.Purpose == InvitationPurpose && user.IsActive) return null;
        if (action.Purpose == PasswordResetPurpose && !user.IsActive) return null;
        if (action.Purpose == EmailVerificationPurpose && (!user.IsActive || user.EmailConfirmedAtUtc is not null)) return null;
        if (action.Purpose is not (InvitationPurpose or PasswordResetPurpose or EmailVerificationPurpose)) return null;
        return new AccountActionSnapshot(action.Purpose, MaskEmail(user.Email), action.ExpiresAtUtc);
    }

    public async Task<AccountActionCompletionResult> CompleteAsync(
        string token,
        string newPassword,
        string confirmPassword,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlausibleToken(token)) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 12 or > 1024 || !string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            return new(AccountActionCompletionOutcome.InvalidPassword);
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hash = HashToken(token);
        var action = await db.AccountActionTokens.SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (action is null || action.ConsumedAtUtc is not null || action.ExpiresAtUtc <= now) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        if (action.Purpose is not (InvitationPurpose or PasswordResetPurpose)) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == action.UserId, cancellationToken);
        if (user is null || !string.Equals(user.SecurityStamp, action.SecurityStamp, StringComparison.Ordinal)) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        if (action.Purpose == InvitationPurpose && user.IsActive) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        if (action.Purpose == PasswordResetPurpose && !user.IsActive) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        if (!string.IsNullOrWhiteSpace(user.PasswordHash)
            && passwordHasher.VerifyHashedPassword(user, user.PasswordHash, newPassword) != PasswordVerificationResult.Failed)
            return new(AccountActionCompletionOutcome.InvalidPassword);

        var newPasswordHash = passwordHasher.HashPassword(user, newPassword);
        var newSecurityStamp = Guid.NewGuid().ToString("N");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await ClaimActionAsync(db, action.Id, now, cancellationToken);
        if (claimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AccountActionCompletionOutcome.InvalidOrExpired);
        }

        var userUpdated = await UpdateUserCredentialAsync(
            db, user, action, newPasswordHash, newSecurityStamp, now, cancellationToken);
        if (userUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AccountActionCompletionOutcome.InvalidOrExpired);
        }

        user.PasswordHash = newPasswordHash;
        user.SecurityStamp = newSecurityStamp;
        user.LastPasswordChangedUtc = now;
        user.FailedSignInCount = 0;
        user.LastFailedSignInUtc = null;
        user.LockoutEndUtc = null;
        if (action.Purpose == InvitationPurpose)
        {
            user.IsActive = true;
            user.EmailConfirmedAtUtc = now;
            await db.CompanyMemberships
                .Where(membership => membership.UserId == user.Id && membership.CompanyId == action.CompanyId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(membership => membership.IsActive, true), cancellationToken);
        }

        var otherActions = await db.AccountActionTokens
            .Where(candidate => candidate.UserId == user.Id && candidate.Id != action.Id && candidate.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var other in otherActions) other.ConsumedAtUtc = now;
        db.Entry(user).State = EntityState.Detached;
        db.Entry(action).State = EntityState.Detached;
        db.AuthenticationAuditEntries.Add(CreateAuthenticationAudit(
            user,
            action.Purpose == InvitationPurpose ? "invitation_accepted" : "password_reset_completed",
            true,
            ipAddress,
            userAgent,
            action.Purpose == InvitationPurpose
                ? "The operator verified the invited email address, chose a password, and activated the account."
                : "The operator used a one-time email token to reset the password and invalidate existing sessions.",
            now));
        if (action.Purpose == PasswordResetPurpose && EmailDeliveryConfigured)
        {
            db.SecurityEmailOutboxMessages.Add(CreateOutboxMessage(
                action.Id,
                user.Email,
                "Your BrassLedger password was changed",
                "The password for your BrassLedger operator account was changed using a one-time reset link. All prior sessions were invalidated. If you did not make this change, contact your administrator immediately.",
                now,
                requiresUsableAction: false));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AccountActionCompletionOutcome.Succeeded, action.Purpose);
    }

    public async Task<AccountActionCompletionResult> CompleteEmailVerificationAsync(
        string token,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlausibleToken(token)) return new(AccountActionCompletionOutcome.InvalidOrExpired);
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var action = await db.AccountActionTokens.SingleOrDefaultAsync(candidate => candidate.TokenHash == HashToken(token), cancellationToken);
        if (action is null || action.Purpose != EmailVerificationPurpose || action.ConsumedAtUtc is not null || action.ExpiresAtUtc <= now)
            return new(AccountActionCompletionOutcome.InvalidOrExpired);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == action.UserId, cancellationToken);
        if (user is null || !user.IsActive || user.EmailConfirmedAtUtc is not null || !string.Equals(user.SecurityStamp, action.SecurityStamp, StringComparison.Ordinal))
            return new(AccountActionCompletionOutcome.InvalidOrExpired);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await ClaimActionAsync(db, action.Id, now, cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AccountActionCompletionOutcome.InvalidOrExpired);
        }
        var updated = await db.Users
            .Where(candidate => candidate.Id == user.Id
                && candidate.IsActive
                && candidate.EmailConfirmedAtUtc == null
                && candidate.SecurityStamp == action.SecurityStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.EmailConfirmedAtUtc, now), cancellationToken);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(AccountActionCompletionOutcome.InvalidOrExpired);
        }

        var otherActions = await db.AccountActionTokens
            .Where(candidate => candidate.UserId == user.Id && candidate.Id != action.Id && candidate.Purpose == EmailVerificationPurpose && candidate.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var other in otherActions) other.ConsumedAtUtc = now;
        db.Entry(action).State = EntityState.Detached;
        db.Entry(user).State = EntityState.Detached;
        db.AuthenticationAuditEntries.Add(CreateAuthenticationAudit(
            user,
            "email_verified",
            true,
            ipAddress,
            userAgent,
            "The operator verified the account email address using a one-time link.",
            now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AccountActionCompletionOutcome.Succeeded, EmailVerificationPurpose);
    }

    private static Task<int> ClaimActionAsync(
        BrassLedgerDbContext db,
        Guid actionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) => db.Database.IsSqlite()
        ? db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "AccountActionTokens" SET "ConsumedAtUtc" = {now.ToString("O")} WHERE "Id" = {actionId} AND "ConsumedAtUtc" IS NULL AND julianday("ExpiresAtUtc") > julianday({now.ToString("O")})""",
            cancellationToken)
        : db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "AccountActionTokens" SET "ConsumedAtUtc" = {now} WHERE "Id" = {actionId} AND "ConsumedAtUtc" IS NULL AND "ExpiresAtUtc" > {now}""",
            cancellationToken);

    private static Task<int> UpdateUserCredentialAsync(
        BrassLedgerDbContext db,
        AppUser user,
        AccountActionToken action,
        string passwordHash,
        string securityStamp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = db.Users.Where(candidate => candidate.Id == user.Id
            && candidate.SecurityStamp == action.SecurityStamp
            && candidate.IsActive == (action.Purpose != InvitationPurpose));
        return action.Purpose == InvitationPurpose
            ? candidates.ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                .SetProperty(candidate => candidate.SecurityStamp, securityStamp)
                .SetProperty(candidate => candidate.LastPasswordChangedUtc, now)
                .SetProperty(candidate => candidate.FailedSignInCount, 0)
                .SetProperty(candidate => candidate.LastFailedSignInUtc, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.LockoutEndUtc, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.IsActive, true)
                .SetProperty(candidate => candidate.EmailConfirmedAtUtc, now), cancellationToken)
            : candidates.ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                .SetProperty(candidate => candidate.SecurityStamp, securityStamp)
                .SetProperty(candidate => candidate.LastPasswordChangedUtc, now)
                .SetProperty(candidate => candidate.FailedSignInCount, 0)
                .SetProperty(candidate => candidate.LastFailedSignInUtc, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.LockoutEndUtc, (DateTimeOffset?)null), cancellationToken);
    }

    private AccountActionToken CreateActionToken(
        AppUser user,
        Guid? companyId,
        string purpose,
        string token,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        Guid? createdByUserId,
        string ipAddress) => new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, CompanyId = companyId, Purpose = purpose,
        TokenHash = HashToken(token), SecurityStamp = user.SecurityStamp,
        CreatedAtUtc = createdAt, ExpiresAtUtc = expiresAt, CreatedByUserId = createdByUserId,
        RequestedIpAddress = ipAddress
    };

    private SecurityEmailOutboxMessage CreateOutboxMessage(Guid actionId, string recipient, string subject, string body, DateTimeOffset now, bool requiresUsableAction = true) => new()
    {
        Id = Guid.NewGuid(), AccountActionTokenId = actionId, RecipientEmail = recipient,
        RequiresUsableAction = requiresUsableAction,
        Subject = subject, Body = body, Status = "Pending", CreatedAtUtc = now, NextAttemptAtUtc = now
    };

    private string BuildActionLink(string token) =>
        $"{_emailOptions.PublicBaseUrl.TrimEnd('/')}/account/action/start?token={Uri.EscapeDataString(token)}";

    private static string GenerateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static bool IsPlausibleToken(string? token) => token is { Length: 43 }
        && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "verified email address";
        var local = email[..at];
        return $"{local[0]}***{email[at..]}";
    }

    private static AuthenticationAuditEntry CreateAuthenticationAudit(
        AppUser user, string eventType, bool succeeded, string ipAddress, string userAgent, string detail, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, CompanyId = user.CompanyId, UserName = user.UserName,
        EventType = eventType, Succeeded = succeeded, OccurredUtc = now,
        IpAddress = ipAddress, UserAgent = userAgent, Detail = detail
    };
}
