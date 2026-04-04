using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Auth;

public sealed class UserAuthenticationService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    IPasswordHasher<AppUser> passwordHasher) : IUserAuthenticationService
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

        var now = DateTimeOffset.UtcNow;
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

        user.FailedSignInCount = 0;
        user.LastFailedSignInUtc = null;
        user.LockoutEndUtc = null;
        user.LastSuccessfulSignInUtc = now;
        user.SecurityStamp = EnsureSecurityStamp(user.SecurityStamp);

        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditEntryAsync(
            dbContext,
            user,
            user.UserName,
            "login_succeeded",
            true,
            ipAddress,
            userAgent,
            "The operator signed in successfully.",
            cancellationToken);

        return new AuthenticationResult(AuthenticationOutcome.Succeeded, new AuthenticatedUser(
            user.Id,
            user.CompanyId,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.Role,
            user.SecurityStamp));
    }

    private static string EnsureSecurityStamp(string currentSecurityStamp)
    {
        return string.IsNullOrWhiteSpace(currentSecurityStamp)
            ? Guid.NewGuid().ToString("N")
            : currentSecurityStamp;
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
        dbContext.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            CompanyId = user?.CompanyId,
            UserName = userName,
            EventType = eventType,
            Succeeded = succeeded,
            OccurredUtc = DateTimeOffset.UtcNow,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Detail = detail
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
