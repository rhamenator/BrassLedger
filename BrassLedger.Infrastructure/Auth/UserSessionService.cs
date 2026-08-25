using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Auth;

public interface IUserSessionService
{
    Task<AuthenticatedUser> IssueAsync(AuthenticatedUser user, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSessionSnapshot>> GetActiveAsync(Guid userId, Guid currentSessionId, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid userId, Guid companyId, Guid sessionId, Guid currentSessionId, ClaimsPrincipal principal, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task RevokeCurrentAsync(Guid userId, Guid? companyId, Guid sessionId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
}

public sealed record UserSessionSnapshot(
    Guid Id,
    string Device,
    string MaskedNetwork,
    string AuthenticationMethod,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsCurrent);

public sealed class UserSessionService(
    IDbContextFactory<BrassLedgerDbContext> dbContextFactory,
    TimeProvider timeProvider) : IUserSessionService
{
    private const int SessionHistoryRetentionDays = 90;

    public async Task<AuthenticatedUser> IssueAsync(AuthenticatedUser user, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var retentionCutoff = now.AddDays(-SessionHistoryRetentionDays);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (db.Database.IsSqlite())
        {
            var retainedHistory = await db.UserSessions
                .Where(session => session.UserId == user.UserId && session.RevokedAtUtc != null)
                .ToListAsync(cancellationToken);
            db.UserSessions.RemoveRange(retainedHistory.Where(session => session.RevokedAtUtc <= retentionCutoff));
            var existingSessions = await db.UserSessions
                .Where(session => session.UserId == user.UserId && session.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var existing in existingSessions.Where(session => session.SecurityStamp != user.SecurityStamp || session.ExpiresAtUtc <= now))
                existing.RevokedAtUtc = now;
        }
        else
        {
            await db.UserSessions
                .Where(session => session.UserId == user.UserId && session.RevokedAtUtc != null && session.RevokedAtUtc <= retentionCutoff)
                .ExecuteDeleteAsync(cancellationToken);
            await db.UserSessions
                .Where(session => session.UserId == user.UserId && session.RevokedAtUtc == null
                    && (session.SecurityStamp != user.SecurityStamp || session.ExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAtUtc, now), cancellationToken);
        }

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            SecurityStamp = user.SecurityStamp,
            AuthenticationMethod = user.MfaAuthenticated ? "Password + MFA" : "Password",
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(BrassLedgerAuthenticationDefaults.SessionMinutes),
            IpAddress = Truncate(ipAddress, 128),
            UserAgent = Truncate(userAgent, 512)
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return user with { SessionId = session.Id };
    }

    public async Task<IReadOnlyList<UserSessionSnapshot>> GetActiveAsync(Guid userId, Guid currentSessionId, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!IsCurrentOperator(principal, userId, currentSessionId: currentSessionId)) return [];
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var securityStamp = await db.Users.AsNoTracking().Where(user => user.Id == userId && user.IsActive)
            .Select(user => user.SecurityStamp).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(securityStamp)) return [];

        var sessions = await db.UserSessions.AsNoTracking()
            .Where(session => session.UserId == userId && session.SecurityStamp == securityStamp && session.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        return sessions
            .Where(session => session.ExpiresAtUtc > now)
            .OrderByDescending(session => session.LastSeenAtUtc)
            .Select(session => new UserSessionSnapshot(
                session.Id, DescribeDevice(session.UserAgent), MaskNetwork(session.IpAddress),
                session.AuthenticationMethod, session.CreatedAtUtc, session.LastSeenAtUtc,
                session.ExpiresAtUtc, session.Id == currentSessionId))
            .ToArray();
    }

    public async Task<bool> RevokeAsync(
        Guid userId, Guid companyId, Guid sessionId, Guid currentSessionId,
        ClaimsPrincipal principal, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || sessionId == currentSessionId) return false;
        if (!IsCurrentOperator(principal, userId, companyId, currentSessionId)) return false;
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);
        if (user is null || !await db.CompanyMemberships.AsNoTracking().AnyAsync(
                membership => membership.UserId == userId && membership.CompanyId == companyId && membership.IsActive,
                cancellationToken)) return false;

        if (db.Database.IsSqlite())
        {
            var session = await db.UserSessions.SingleOrDefaultAsync(candidate => candidate.Id == sessionId
                && candidate.UserId == userId && candidate.SecurityStamp == user.SecurityStamp && candidate.RevokedAtUtc == null,
                cancellationToken);
            if (session is null || session.ExpiresAtUtc <= now) return false;
            session.RevokedAtUtc = now;
        }
        else
        {
            var updated = await db.UserSessions
                .Where(session => session.Id == sessionId && session.UserId == userId
                    && session.SecurityStamp == user.SecurityStamp && session.RevokedAtUtc == null && session.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAtUtc, now), cancellationToken);
            if (updated != 1) return false;
        }

        db.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = companyId,
            UserName = user.UserName,
            EventType = "session_revoked",
            Succeeded = true,
            OccurredUtc = now,
            IpAddress = Truncate(ipAddress, 128),
            UserAgent = Truncate(userAgent, 512),
            Detail = "The operator individually revoked another named session."
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RevokeCurrentAsync(Guid userId, Guid? companyId, Guid sessionId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty) return;
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.UserSessions.Where(session => session.Id == sessionId && session.UserId == userId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAtUtc, now), cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null) return;
        db.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = companyId,
            UserName = user.UserName,
            EventType = "logout",
            Succeeded = true,
            OccurredUtc = now,
            IpAddress = Truncate(ipAddress, 128),
            UserAgent = Truncate(userAgent, 512),
            Detail = "The operator signed out and revoked the current named session."
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsCurrentOperator(ClaimsPrincipal? principal, Guid userId, Guid? companyId = null, Guid? currentSessionId = null)
    {
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
            || currentUserId != userId)
            return false;
        if (currentSessionId.HasValue
            && (!Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.SessionIdClaimType), out var claimedSessionId)
                || claimedSessionId != currentSessionId.Value))
            return false;
        return !companyId.HasValue
            || (Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var currentCompanyId)
                && currentCompanyId == companyId.Value);
    }

    private static string DescribeDevice(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unidentified browser";
        var browser = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" : "Browser";
        var platform = userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
            : userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
            : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : "unknown platform";
        return $"{browser} on {platform}";
    }

    private static string MaskNetwork(string address)
    {
        if (System.Net.IPAddress.TryParse(address, out var parsed))
        {
            var bytes = parsed.GetAddressBytes();
            if (bytes.Length == 4) return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.xxx";
            return string.Join(':', parsed.ToString().Split(':', StringSplitOptions.RemoveEmptyEntries).Take(3)) + ":…";
        }
        return string.IsNullOrWhiteSpace(address) ? "Not recorded" : "Protected network";
    }

    private static string Truncate(string? value, int maximumLength) => string.IsNullOrEmpty(value)
        ? string.Empty
        : value.Length <= maximumLength ? value : value[..maximumLength];
}
