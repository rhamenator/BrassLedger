using System.Security.Claims;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Accounting;

internal static class SsaWageFileActorAuthority
{
    internal static async Task<Actor> RequireAsync(
        BrassLedgerDbContext db,
        ClaimsPrincipal? principal,
        TimeProvider timeProvider,
        IReadOnlyCollection<string> requiredPermissions,
        CancellationToken cancellationToken)
    {
        if (principal?.Identity?.IsAuthenticated != true
            || principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId)
            || !Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.SessionIdClaimType), out var sessionId)
            || string.IsNullOrWhiteSpace(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.SecurityStampClaimType)))
            throw new UnauthorizedAccessException("A current, authenticated company operator is required for protected SSA wage files.");

        var now = timeProvider.GetUtcNow();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        var session = await db.UserSessions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == sessionId && item.UserId == userId, cancellationToken);
        var membership = await db.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(
            item => item.UserId == userId && item.CompanyId == companyId && item.IsActive, cancellationToken);
        var stamp = principal.FindFirstValue(BrassLedgerAuthenticationDefaults.SecurityStampClaimType);
        var method = principal.FindFirstValue(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType);
        if (user is null || user.LockoutEndUtc > now || membership is null
            || session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now
            || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)
            || !string.Equals(session.SecurityStamp, stamp, StringComparison.Ordinal)
            || !string.Equals(session.AuthenticationMethod,
                method == "mfa" ? "Password + MFA" : method == "pwd" ? "Password" : null, StringComparison.Ordinal)
            || !string.Equals(membership.Role, principal.FindFirstValue(ClaimTypes.Role), StringComparison.Ordinal)
            || !await db.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId, cancellationToken))
            throw new UnauthorizedAccessException("The protected SSA wage-file actor is no longer authorized.");

        var role = await db.AccessRoles.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == companyId && item.Name == membership.Role && item.IsActive, cancellationToken);
        if (role is null
            || ((role.RequiresMfa || BrassLedgerRoleTemplates.IsMandatoryMfaRole(role.Name)) && !user.MfaEnabled)
            || (user.MfaEnabled && !string.Equals(method, "mfa", StringComparison.Ordinal)))
            throw new UnauthorizedAccessException("The protected SSA wage-file actor requires a valid MFA session.");

        var storedPermissions = role.Permissions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requiredPermissions.Any(permission =>
                !storedPermissions.Contains(permission)
                || !principal.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)))
            throw new UnauthorizedAccessException("The current role lacks protected SSA wage-file permission.");

        return new Actor(userId, companyId, sessionId);
    }

    internal sealed record Actor(Guid UserId, Guid CompanyId, Guid SessionId);
}
