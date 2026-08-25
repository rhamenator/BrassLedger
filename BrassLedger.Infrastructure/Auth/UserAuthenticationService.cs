using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;

namespace BrassLedger.Infrastructure.Auth;

public sealed class UserAuthenticationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IPasswordHasher<AppUser> passwordHasher,
    TotpService totpService,
    TimeProvider timeProvider) : IUserAuthenticationService
{
    public async Task<AuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthenticationResult(AuthenticationOutcome.InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow();
        var normalizedUserName = userName.Trim().ToUpperInvariant();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .Where(candidate => candidate.IsActive)
            .SingleOrDefaultAsync(candidate => candidate.UserName.ToUpper() == normalizedUserName, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            await WriteAuditEntryAsync(
                dbContext,
                null,
                userName.Trim(),
                "login_failed",
                false,
                ipAddress,
                userAgent,
                "The supplied credentials did not match an active operator.",
                cancellationToken);
            return new AuthenticationResult(AuthenticationOutcome.InvalidCredentials);
        }

        if (user.LockoutEndUtc is not null && user.LockoutEndUtc > now)
        {
            await WriteAuditEntryAsync(
                dbContext,
                user,
                user.UserName,
                "login_locked_out",
                false,
                ipAddress,
                userAgent,
                "The operator is temporarily locked out.",
                cancellationToken);
            return new AuthenticationResult(AuthenticationOutcome.LockedOut, LockoutEndUtc: user.LockoutEndUtc);
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            user.FailedSignInCount += 1;
            user.LastFailedSignInUtc = now;

            if (user.FailedSignInCount >= BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts)
            {
                user.LockoutEndUtc = now.AddMinutes(BrassLedgerAuthenticationDefaults.LockoutMinutes);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var lockedOut = user.LockoutEndUtc is not null && user.LockoutEndUtc > now;
            await WriteAuditEntryAsync(
                dbContext,
                user,
                user.UserName,
                lockedOut ? "login_locked_out" : "login_failed",
                false,
                ipAddress,
                userAgent,
                lockedOut
                    ? "The operator exceeded the allowed failed sign-in threshold."
                    : "The supplied credentials did not match the stored password hash.",
                cancellationToken);

            return lockedOut
                ? new AuthenticationResult(AuthenticationOutcome.LockedOut, LockoutEndUtc: user.LockoutEndUtc)
                : new AuthenticationResult(AuthenticationOutcome.InvalidCredentials);
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            user.LastPasswordChangedUtc = now;
        }

        var membership = await ResolveMembershipAsync(dbContext, user, user.CompanyId, cancellationToken);
        if (membership is null) return new AuthenticationResult(AuthenticationOutcome.InvalidCredentials);
        user.FailedSignInCount = 0;
        user.LastFailedSignInUtc = null;
        user.LockoutEndUtc = null;
        user.SecurityStamp = EnsureSecurityStamp(user.SecurityStamp);

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecret))
            {
                dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                    user, "mfa_configuration_invalid", false, ipAddress, userAgent,
                    "MFA is enabled but no protected authenticator secret is available.", companyId: membership.CompanyId));
                await dbContext.SaveChangesAsync(cancellationToken);
                return new AuthenticationResult(AuthenticationOutcome.InvalidCredentials);
            }

            if (user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now)
            {
                dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                    user, "mfa_locked_out", false, ipAddress, userAgent,
                    "The operator's second factor is temporarily locked.", companyId: membership.CompanyId));
                await dbContext.SaveChangesAsync(cancellationToken);
                return new AuthenticationResult(AuthenticationOutcome.LockedOut, LockoutEndUtc: user.MfaLockoutEndUtc);
            }

            if (user.MfaLockoutEndUtc is not null)
            {
                user.MfaLockoutEndUtc = null;
                user.MfaFailedAttemptCount = 0;
            }

            var challengeToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            await PruneMfaChallengesAsync(dbContext, now, cancellationToken);
            dbContext.MfaSignInChallenges.Add(new MfaSignInChallenge
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CompanyId = membership.CompanyId,
                TokenHash = HashToken(challengeToken),
                SecurityStamp = user.SecurityStamp,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(BrassLedgerAuthenticationDefaults.MfaChallengeMinutes),
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_challenge_issued", true, ipAddress, userAgent,
                "The password was accepted and a bounded second-factor challenge was issued.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthenticationResult(AuthenticationOutcome.MfaRequired, MfaChallengeToken: challengeToken);
        }

        user.LastSuccessfulSignInUtc = now;
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user, "login_succeeded", true, ipAddress, userAgent,
            "The operator signed in successfully with a password.", companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);
        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);

        return new AuthenticationResult(AuthenticationOutcome.Succeeded, new AuthenticatedUser(
            user.Id,
            membership.CompanyId,
            user.UserName,
            user.DisplayName,
            user.Email,
            membership.Role,
            user.SecurityStamp,
            permissions,
            false));
    }

    public async Task<AuthenticatedUser?> SwitchCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return null;
        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null) return null;
        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);
        return new AuthenticatedUser(user.Id, membership.CompanyId, user.UserName, user.DisplayName, user.Email, membership.Role, user.SecurityStamp, permissions);
    }

    public async Task<AccountSecuritySnapshot?> GetAccountSecurityAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return null;

        var recentEntries = dbContext.Database.IsSqlite()
            ? await dbContext.AuthenticationAuditEntries
                .FromSqlInterpolated($"""SELECT * FROM "AuthenticationAuditEntries" WHERE "UserId" = {userId} ORDER BY "OccurredUtc" DESC LIMIT 20""")
                .AsNoTracking()
                .ToListAsync(cancellationToken)
            : await dbContext.AuthenticationAuditEntries.AsNoTracking()
                .Where(entry => entry.UserId == userId)
                .OrderByDescending(entry => entry.OccurredUtc)
                .Take(20)
                .ToListAsync(cancellationToken);
        var events = recentEntries
            .Select(entry => new AccountSecurityEventSnapshot(
                entry.EventType,
                entry.Succeeded,
                entry.OccurredUtc,
                entry.IpAddress,
                entry.UserAgent,
                entry.Detail))
            .ToArray();
        var recoveryCodesRemaining = user.MfaEnabled
            ? await dbContext.MfaRecoveryCodes.CountAsync(code => code.UserId == userId && code.UsedAtUtc == null, cancellationToken)
            : 0;

        return new AccountSecuritySnapshot(
            user.UserName,
            user.DisplayName,
            user.Email,
            user.LastPasswordChangedUtc,
            user.LastSuccessfulSignInUtc,
            user.MfaEnabled,
            user.MfaEnrolledAtUtc,
            recoveryCodesRemaining,
            events);
    }

    public async Task<AccountSecurityResult> ChangePasswordAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string newPassword,
        string confirmPassword,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId && candidate.IsActive,
            cancellationToken);
        if (user is null)
        {
            return new AccountSecurityResult(AccountSecurityOutcome.Unauthorized);
        }

        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null)
        {
            return new AccountSecurityResult(AccountSecurityOutcome.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(currentPassword)
            || string.IsNullOrWhiteSpace(newPassword)
            || newPassword.Length < 12
            || !string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user,
                "password_change_failed",
                false,
                ipAddress,
                userAgent,
                "The new password did not satisfy the password-change requirements.",
                companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AccountSecurityResult(AccountSecurityOutcome.InvalidRequest);
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user,
                "password_change_failed",
                false,
                ipAddress,
                userAgent,
                "The current password was not valid.",
                companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AccountSecurityResult(AccountSecurityOutcome.InvalidCurrentPassword);
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, newPassword) != PasswordVerificationResult.Failed)
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user,
                "password_change_failed",
                false,
                ipAddress,
                userAgent,
                "The proposed password matched the current password.",
                companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AccountSecurityResult(AccountSecurityOutcome.PasswordReused);
        }

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.LastPasswordChangedUtc = DateTimeOffset.UtcNow;
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user,
            "password_changed",
            true,
            ipAddress,
            userAgent,
            "The operator changed the account password and invalidated other sessions.",
            companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);

        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);
        return new AccountSecurityResult(AccountSecurityOutcome.Succeeded, new AuthenticatedUser(
            user.Id,
            membership.CompanyId,
            user.UserName,
            user.DisplayName,
            user.Email,
            membership.Role,
            user.SecurityStamp,
            permissions));
    }

    public async Task<AccountSecurityResult> RevokeOtherSessionsAsync(
        Guid userId,
        Guid companyId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId && candidate.IsActive,
            cancellationToken);
        if (user is null) return new AccountSecurityResult(AccountSecurityOutcome.Unauthorized);
        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null) return new AccountSecurityResult(AccountSecurityOutcome.Unauthorized);

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user,
            "other_sessions_revoked",
            true,
            ipAddress,
            userAgent,
            "All previously issued sessions were invalidated; this session was reissued.",
            companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);

        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);
        return new AccountSecurityResult(AccountSecurityOutcome.Succeeded, new AuthenticatedUser(
            user.Id,
            membership.CompanyId,
            user.UserName,
            user.DisplayName,
            user.Email,
            membership.Role,
            user.SecurityStamp,
            permissions));
    }

    public async Task<MfaEnrollmentResult> BeginMfaEnrollmentAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return new MfaEnrollmentResult(MfaOperationOutcome.Unauthorized);
        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null) return new MfaEnrollmentResult(MfaOperationOutcome.Unauthorized);
        if (user.MfaEnabled) return new MfaEnrollmentResult(MfaOperationOutcome.AlreadyEnabled);
        if (string.IsNullOrWhiteSpace(currentPassword)
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_enrollment_rejected", false, ipAddress, userAgent,
                "MFA enrollment was rejected because password reauthentication failed.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaEnrollmentResult(MfaOperationOutcome.InvalidPassword);
        }

        var now = timeProvider.GetUtcNow();
        var secret = totpService.GenerateSecret();
        var recoveryCodes = GenerateRecoveryCodes();
        var priorCodes = await dbContext.MfaRecoveryCodes.Where(code => code.UserId == user.Id).ToListAsync(cancellationToken);
        dbContext.MfaRecoveryCodes.RemoveRange(priorCodes);
        dbContext.MfaRecoveryCodes.AddRange(recoveryCodes.Select(code => new MfaRecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = HashRecoveryCode(user.Id, code),
            CreatedAtUtc = now
        }));
        user.MfaSecret = secret;
        user.MfaLastAcceptedTimeStep = null;
        user.MfaFailedAttemptCount = 0;
        user.MfaLockoutEndUtc = null;
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user, "mfa_enrollment_started", true, ipAddress, userAgent,
            "A new protected TOTP secret and one-use recovery-code set were created pending verification.", companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MfaEnrollmentResult(
            MfaOperationOutcome.Succeeded,
            secret,
            totpService.BuildOtpAuthUri(user.UserName, secret),
            recoveryCodes);
    }

    public async Task<MfaOperationResult> EnableMfaAsync(
        Guid userId,
        Guid companyId,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return new MfaOperationResult(MfaOperationOutcome.Unauthorized);
        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null) return new MfaOperationResult(MfaOperationOutcome.Unauthorized);
        if (user.MfaEnabled) return new MfaOperationResult(MfaOperationOutcome.AlreadyEnabled);
        if (string.IsNullOrWhiteSpace(user.MfaSecret)) return new MfaOperationResult(MfaOperationOutcome.InvalidRequest);
        var now = timeProvider.GetUtcNow();
        if (NormalizeAndCheckMfaLockout(user, now))
            return new MfaOperationResult(MfaOperationOutcome.LockedOut, user.MfaLockoutEndUtc);

        var acceptedStep = totpService.VerifyCode(user.MfaSecret, (verificationCode ?? string.Empty).Trim(), now, user.MfaLastAcceptedTimeStep);
        if (acceptedStep is null)
        {
            RecordMfaFailure(dbContext, user, membership.CompanyId, "mfa_enrollment_code_rejected", ipAddress, userAgent, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaOperationResult(
                user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now ? MfaOperationOutcome.LockedOut : MfaOperationOutcome.InvalidCode,
                user.MfaLockoutEndUtc);
        }

        if (!await dbContext.MfaRecoveryCodes.AnyAsync(code => code.UserId == user.Id && code.UsedAtUtc == null, cancellationToken))
            return new MfaOperationResult(MfaOperationOutcome.InvalidRequest);

        user.MfaEnabled = true;
        user.MfaEnrolledAtUtc = now;
        user.MfaLastAcceptedTimeStep = acceptedStep;
        user.MfaFailedAttemptCount = 0;
        user.MfaLockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user, "mfa_enabled", true, ipAddress, userAgent,
            "Authenticator MFA was enabled after password reauthentication and TOTP verification; existing sessions were invalidated.", companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MfaOperationResult(MfaOperationOutcome.Succeeded);
    }

    public async Task<AccountSecurityResult> DisableMfaAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return new AccountSecurityResult(AccountSecurityOutcome.Unauthorized);
        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null || !user.MfaEnabled) return new AccountSecurityResult(AccountSecurityOutcome.Unauthorized);
        if (string.IsNullOrWhiteSpace(currentPassword)
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_disable_rejected", false, ipAddress, userAgent,
                "MFA disablement was rejected because password reauthentication failed.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AccountSecurityResult(AccountSecurityOutcome.InvalidCurrentPassword);
        }

        var now = timeProvider.GetUtcNow();
        if (NormalizeAndCheckMfaLockout(user, now))
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_disable_rejected", false, ipAddress, userAgent,
                "MFA disablement was rejected because second-factor verification is temporarily locked.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AccountSecurityResult(AccountSecurityOutcome.InvalidRequest);
        }

        var credential = await VerifyMfaCredentialAsync(dbContext, user, verificationCode, now, cancellationToken);
        if (!credential.Succeeded)
        {
            RecordMfaFailure(dbContext, user, membership.CompanyId, "mfa_disable_rejected", ipAddress, userAgent, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AccountSecurityResult(AccountSecurityOutcome.InvalidRequest);
        }

        var recoveryCodes = await dbContext.MfaRecoveryCodes.Where(code => code.UserId == user.Id).ToListAsync(cancellationToken);
        dbContext.MfaRecoveryCodes.RemoveRange(recoveryCodes);
        user.MfaEnabled = false;
        user.MfaSecret = string.Empty;
        user.MfaEnrolledAtUtc = null;
        user.MfaLastAcceptedTimeStep = null;
        user.MfaFailedAttemptCount = 0;
        user.MfaLockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user, "mfa_disabled", true, ipAddress, userAgent,
            "Authenticator MFA and all remaining recovery codes were disabled after two-factor reauthentication.", companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);

        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);
        return new AccountSecurityResult(AccountSecurityOutcome.Succeeded, new AuthenticatedUser(
            user.Id, membership.CompanyId, user.UserName, user.DisplayName, user.Email,
            membership.Role, user.SecurityStamp, permissions, false));
    }

    public async Task<MfaEnrollmentResult> RegenerateMfaRecoveryCodesAsync(
        Guid userId,
        Guid companyId,
        string currentPassword,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null) return new MfaEnrollmentResult(MfaOperationOutcome.Unauthorized);
        var membership = await ResolveMembershipAsync(dbContext, user, companyId, cancellationToken);
        if (membership is null) return new MfaEnrollmentResult(MfaOperationOutcome.Unauthorized);
        if (!user.MfaEnabled) return new MfaEnrollmentResult(MfaOperationOutcome.NotEnabled);
        if (string.IsNullOrWhiteSpace(currentPassword)
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_recovery_codes_rejected", false, ipAddress, userAgent,
                "Recovery-code replacement was rejected because password reauthentication failed.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaEnrollmentResult(MfaOperationOutcome.InvalidPassword);
        }

        var now = timeProvider.GetUtcNow();
        if (NormalizeAndCheckMfaLockout(user, now))
        {
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_recovery_codes_rejected", false, ipAddress, userAgent,
                "Recovery-code replacement was rejected because second-factor verification is temporarily locked.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaEnrollmentResult(MfaOperationOutcome.LockedOut, LockoutEndUtc: user.MfaLockoutEndUtc);
        }

        var credential = await VerifyMfaCredentialAsync(dbContext, user, verificationCode, now, cancellationToken);
        if (!credential.Succeeded)
        {
            RecordMfaFailure(dbContext, user, membership.CompanyId, "mfa_recovery_codes_rejected", ipAddress, userAgent, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaEnrollmentResult(
                user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now ? MfaOperationOutcome.LockedOut : MfaOperationOutcome.InvalidCode,
                LockoutEndUtc: user.MfaLockoutEndUtc);
        }

        var oldCodes = await dbContext.MfaRecoveryCodes.Where(code => code.UserId == user.Id).ToListAsync(cancellationToken);
        dbContext.MfaRecoveryCodes.RemoveRange(oldCodes);
        var replacementCodes = GenerateRecoveryCodes();
        dbContext.MfaRecoveryCodes.AddRange(replacementCodes.Select(code => new MfaRecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = HashRecoveryCode(user.Id, code),
            CreatedAtUtc = now
        }));
        if (credential.TimeStep.HasValue) user.MfaLastAcceptedTimeStep = credential.TimeStep;
        user.MfaFailedAttemptCount = 0;
        user.MfaLockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user, "mfa_recovery_codes_replaced", true, ipAddress, userAgent,
            "All prior recovery codes were invalidated and replaced after two-factor reauthentication; existing sessions were invalidated.", companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MfaEnrollmentResult(MfaOperationOutcome.Succeeded, RecoveryCodes: replacementCodes);
    }

    public async Task<MfaChallengeResult> CompleteMfaChallengeAsync(
        string challengeToken,
        string verificationCode,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(challengeToken) || string.IsNullOrWhiteSpace(verificationCode))
            return new MfaChallengeResult(MfaOperationOutcome.InvalidRequest);
        var now = timeProvider.GetUtcNow();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tokenHash = HashToken(challengeToken);
        var challenge = await dbContext.MfaSignInChallenges.SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (challenge is null || challenge.ConsumedAtUtc is not null)
            return new MfaChallengeResult(MfaOperationOutcome.InvalidCode);
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == challenge.UserId && candidate.IsActive, cancellationToken);
        if (user is null || !user.MfaEnabled || !string.Equals(user.SecurityStamp, challenge.SecurityStamp, StringComparison.Ordinal))
        {
            challenge.ConsumedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaChallengeResult(MfaOperationOutcome.Unauthorized);
        }
        var membership = await ResolveMembershipAsync(dbContext, user, challenge.CompanyId, cancellationToken);
        if (membership is null)
        {
            challenge.ConsumedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaChallengeResult(MfaOperationOutcome.Unauthorized);
        }
        if (challenge.ExpiresAtUtc <= now)
        {
            challenge.ConsumedAtUtc = now;
            dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
                user, "mfa_challenge_expired", false, ipAddress, userAgent,
                "The second-factor challenge expired before successful verification.", companyId: membership.CompanyId));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaChallengeResult(MfaOperationOutcome.Expired);
        }
        if (user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now)
            return new MfaChallengeResult(MfaOperationOutcome.LockedOut, LockoutEndUtc: user.MfaLockoutEndUtc);

        var credential = await VerifyMfaCredentialAsync(dbContext, user, verificationCode, now, cancellationToken);
        if (!credential.Succeeded)
        {
            challenge.FailedAttemptCount += 1;
            RecordMfaFailure(dbContext, user, membership.CompanyId, "mfa_challenge_rejected", ipAddress, userAgent, now);
            if (challenge.FailedAttemptCount >= BrassLedgerAuthenticationDefaults.MaxMfaAttempts)
                challenge.ConsumedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MfaChallengeResult(
                user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now ? MfaOperationOutcome.LockedOut : MfaOperationOutcome.InvalidCode,
                LockoutEndUtc: user.MfaLockoutEndUtc);
        }

        await using var completionTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimedChallenge = await dbContext.MfaSignInChallenges
            .Where(item => item.Id == challenge.Id && item.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAtUtc, now), cancellationToken);
        if (claimedChallenge != 1)
        {
            await completionTransaction.RollbackAsync(cancellationToken);
            return new MfaChallengeResult(MfaOperationOutcome.InvalidCode);
        }

        if (credential.RecoveryCode is not null)
        {
            var claimedRecoveryCode = await dbContext.MfaRecoveryCodes
                .Where(code => code.Id == credential.RecoveryCode.Id && code.UsedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(code => code.UsedAtUtc, now), cancellationToken);
            if (claimedRecoveryCode != 1)
            {
                await completionTransaction.RollbackAsync(cancellationToken);
                return new MfaChallengeResult(MfaOperationOutcome.InvalidCode);
            }
        }
        else if (credential.TimeStep.HasValue)
        {
            var acceptedTimeStep = credential.TimeStep.Value;
            var claimedTimeStep = await dbContext.Users
                .Where(candidate => candidate.Id == user.Id
                    && (!candidate.MfaLastAcceptedTimeStep.HasValue || candidate.MfaLastAcceptedTimeStep < acceptedTimeStep))
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.MfaLastAcceptedTimeStep, acceptedTimeStep), cancellationToken);
            if (claimedTimeStep != 1)
            {
                await completionTransaction.RollbackAsync(cancellationToken);
                return new MfaChallengeResult(MfaOperationOutcome.InvalidCode);
            }
        }

        challenge.ConsumedAtUtc = now;
        if (credential.TimeStep.HasValue) user.MfaLastAcceptedTimeStep = credential.TimeStep;
        if (credential.RecoveryCode is not null) credential.RecoveryCode.UsedAtUtc = now;
        user.MfaFailedAttemptCount = 0;
        user.MfaLockoutEndUtc = null;
        user.FailedSignInCount = 0;
        user.LastFailedSignInUtc = null;
        user.LockoutEndUtc = null;
        user.LastSuccessfulSignInUtc = now;
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user, "login_succeeded", true, ipAddress, userAgent,
            credential.RecoveryCode is null
                ? "The operator signed in successfully with a password and authenticator code."
                : "The operator signed in successfully with a password and one-use recovery code.",
            companyId: membership.CompanyId));
        await dbContext.SaveChangesAsync(cancellationToken);
        await completionTransaction.CommitAsync(cancellationToken);

        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);
        return new MfaChallengeResult(MfaOperationOutcome.Succeeded, new AuthenticatedUser(
            user.Id, membership.CompanyId, user.UserName, user.DisplayName, user.Email,
            membership.Role, user.SecurityStamp, permissions, true), UsedRecoveryCode: credential.RecoveryCode is not null);
    }

    private async Task<MfaCredentialVerification> VerifyMfaCredentialAsync(
        BrassLedgerDbContext dbContext,
        AppUser user,
        string verificationCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = (verificationCode ?? string.Empty).Trim();
        var acceptedStep = totpService.VerifyCode(user.MfaSecret, normalized, now, user.MfaLastAcceptedTimeStep);
        if (acceptedStep.HasValue) return new MfaCredentialVerification(true, acceptedStep, null);

        var recoveryHash = HashRecoveryCode(user.Id, normalized);
        var recoveryCode = await dbContext.MfaRecoveryCodes.SingleOrDefaultAsync(
            code => code.UserId == user.Id && code.CodeHash == recoveryHash && code.UsedAtUtc == null,
            cancellationToken);
        return recoveryCode is null
            ? new MfaCredentialVerification(false, null, null)
            : new MfaCredentialVerification(true, null, recoveryCode);
    }

    private static void RecordMfaFailure(
        BrassLedgerDbContext dbContext,
        AppUser user,
        Guid companyId,
        string eventType,
        string ipAddress,
        string userAgent,
        DateTimeOffset now)
    {
        user.MfaFailedAttemptCount += 1;
        if (user.MfaFailedAttemptCount >= BrassLedgerAuthenticationDefaults.MaxMfaAttempts)
            user.MfaLockoutEndUtc = now.AddMinutes(BrassLedgerAuthenticationDefaults.LockoutMinutes);
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(
            user,
            user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now ? "mfa_locked_out" : eventType,
            false,
            ipAddress,
            userAgent,
            user.MfaLockoutEndUtc is not null && user.MfaLockoutEndUtc > now
                ? "The maximum allowed second-factor failures was reached."
                : "The supplied second factor was not accepted.",
            companyId: companyId));
    }

    private static IReadOnlyList<string> GenerateRecoveryCodes()
    {
        return Enumerable.Range(0, BrassLedgerAuthenticationDefaults.RecoveryCodeCount)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            .Select(value => string.Join('-', Enumerable.Range(0, 4).Select(index => value.Substring(index * 8, 8))))
            .ToArray();
    }

    private static string HashRecoveryCode(Guid userId, string recoveryCode)
    {
        var normalized = new string(recoveryCode.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{normalized}")));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task PruneMfaChallengesAsync(
        BrassLedgerDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-1);
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "MfaSignInChallenges" WHERE "ExpiresAtUtc" < {cutoff.ToString("O")}""",
                cancellationToken);
            return;
        }

        await dbContext.MfaSignInChallenges
            .Where(challenge => challenge.ExpiresAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private sealed record MfaCredentialVerification(bool Succeeded, long? TimeStep, MfaRecoveryCode? RecoveryCode);

    private static bool NormalizeAndCheckMfaLockout(AppUser user, DateTimeOffset now)
    {
        if (user.MfaLockoutEndUtc is null) return false;
        if (user.MfaLockoutEndUtc > now) return true;
        user.MfaLockoutEndUtc = null;
        user.MfaFailedAttemptCount = 0;
        return false;
    }

    private static string EnsureSecurityStamp(string currentSecurityStamp)
    {
        return string.IsNullOrWhiteSpace(currentSecurityStamp)
            ? Guid.NewGuid().ToString("N")
            : currentSecurityStamp;
    }

    private static async Task<IReadOnlyList<string>> ResolvePermissionsAsync(
        BrassLedgerDbContext dbContext,
        Guid companyId,
        string role,
        CancellationToken cancellationToken)
    {
        var accessRole = await dbContext.AccessRoles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CompanyId == companyId
                    && candidate.IsActive
                    && candidate.Name == role,
                cancellationToken);

        if (accessRole is not null)
        {
            return ParsePermissions(accessRole.Permissions);
        }

        return BrassLedgerRoleTemplates.GetPermissionsForRoleName(role);
    }

    private static async Task<CompanyMembership?> ResolveMembershipAsync(BrassLedgerDbContext dbContext, AppUser user, Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.CompanyMemberships.SingleOrDefaultAsync(item => item.UserId == user.Id && item.CompanyId == companyId && item.IsActive, cancellationToken);
        if (membership is not null) return membership;
        if (user.CompanyId != companyId) return null;
        membership = new CompanyMembership { Id = Guid.NewGuid(), UserId = user.Id, CompanyId = user.CompanyId, Role = user.Role, IsOwner = true, IsActive = true, GrantedAtUtc = DateTimeOffset.UtcNow };
        dbContext.CompanyMemberships.Add(membership);
        await dbContext.SaveChangesAsync(cancellationToken);
        return membership;
    }

    private static IReadOnlyList<string> ParsePermissions(string permissions)
    {
        return permissions
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task WriteAuditEntryAsync(
        BrassLedgerDbContext dbContext,
        AppUser? user,
        string userName,
        string eventType,
        bool succeeded,
        string ipAddress,
        string userAgent,
        string detail,
        CancellationToken cancellationToken)
    {
        dbContext.AuthenticationAuditEntries.Add(CreateAuditEntry(user, eventType, succeeded, ipAddress, userAgent, detail, userName));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AuthenticationAuditEntry CreateAuditEntry(
        AppUser? user,
        string eventType,
        bool succeeded,
        string ipAddress,
        string userAgent,
        string detail,
        string? userName = null,
        Guid? companyId = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            CompanyId = companyId ?? user?.CompanyId,
            UserName = userName ?? user?.UserName ?? string.Empty,
            EventType = eventType,
            Succeeded = succeeded,
            OccurredUtc = DateTimeOffset.UtcNow,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Detail = detail
        };
}
