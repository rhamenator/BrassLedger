using System.Security.Claims;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BrassLedger.Infrastructure.Auth;

public sealed class BrassLedgerCookieEvents(IDbContextFactory<BrassLedgerDbContext> dbContextFactory) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        return HandleApiRedirectAsync(context, StatusCodes.Status401Unauthorized);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        return HandleApiRedirectAsync(context, StatusCodes.Status403Forbidden);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var securityStamp = context.Principal?.FindFirstValue(BrassLedgerAuthenticationDefaults.SecurityStampClaimType);
        var companyIdValue = context.Principal?.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType);
        var roleValue = context.Principal?.FindFirstValue(ClaimTypes.Role);
        var authenticationMethod = context.Principal?.FindFirstValue(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType);
        var enrollmentRequiredClaim = context.Principal?.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true") == true;

        if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(securityStamp))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, context.HttpContext.RequestAborted);
        var hasCompanyId = Guid.TryParse(companyIdValue, out var companyId);
        var membership = user is null || !hasCompanyId
            ? null
            : await dbContext.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(
                item => item.UserId == user.Id && item.CompanyId == companyId && item.IsActive,
                context.HttpContext.RequestAborted);
        var currentRole = membership is null
            ? null
            : await dbContext.AccessRoles.AsNoTracking().SingleOrDefaultAsync(
                role => role.CompanyId == membership.CompanyId && role.IsActive && role.Name == membership.Role,
                context.HttpContext.RequestAborted);
        var claimedPermissions = context.Principal?.FindAll(BrassLedgerAuthenticationDefaults.PermissionClaimType).Select(claim => claim.Value).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var roleTemplate = BrassLedgerRoleTemplates.BuiltIn.FirstOrDefault(template => string.Equals(template.Name, membership?.Role, StringComparison.OrdinalIgnoreCase));
        var roleRequiresMfa = currentRole?.RequiresMfa ?? roleTemplate?.RequiresMfa ?? false;
        var enrollmentRequired = roleRequiresMfa && user is { MfaEnabled: false };
        var currentPermissions = enrollmentRequired
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : currentRole is null
                ? (roleTemplate?.Permissions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : currentRole.Permissions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isValid = user is not null
            && user.IsActive
            && (user.LockoutEndUtc is null || user.LockoutEndUtc <= DateTimeOffset.UtcNow)
            && string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal)
            && membership is not null
            && string.Equals(membership.Role, roleValue, StringComparison.Ordinal)
            && (!user.MfaEnabled || string.Equals(authenticationMethod, "mfa", StringComparison.Ordinal))
            && enrollmentRequiredClaim == enrollmentRequired
            && claimedPermissions.SetEquals(currentPermissions);

        if (isValid)
        {
            return;
        }

        dbContext.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            CompanyId = membership?.CompanyId ?? user?.CompanyId,
            UserName = context.Principal?.Identity?.Name ?? string.Empty,
            EventType = "session_rejected",
            Succeeded = false,
            OccurredUtc = DateTimeOffset.UtcNow,
            IpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = context.HttpContext.Request.Headers.UserAgent.ToString(),
            Detail = "The session failed validation and was signed out."
        });

        await dbContext.SaveChangesAsync(context.HttpContext.RequestAborted);

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
    }

    private static Task HandleApiRedirectAsync(RedirectContext<CookieAuthenticationOptions> context, int apiStatusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = apiStatusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
