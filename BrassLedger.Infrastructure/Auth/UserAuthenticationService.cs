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

        var membership = await ResolveMembershipAsync(dbContext, user, user.CompanyId, cancellationToken);
        if (membership is null) return new AuthenticationResult(AuthenticationOutcome.InvalidCredentials);
        var permissions = await ResolvePermissionsAsync(dbContext, membership.CompanyId, membership.Role, cancellationToken);

        return new AuthenticationResult(AuthenticationOutcome.Succeeded, new AuthenticatedUser(
            user.Id,
            membership.CompanyId,
            user.UserName,
            user.DisplayName,
            user.Email,
            membership.Role,
            user.SecurityStamp,
            permissions));
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

        return new AccountSecuritySnapshot(
            user.UserName,
            user.DisplayName,
            user.Email,
            user.LastPasswordChangedUtc,
            user.LastSuccessfulSignInUtc,
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
