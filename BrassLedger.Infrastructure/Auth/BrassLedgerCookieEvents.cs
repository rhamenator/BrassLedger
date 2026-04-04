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

        var isValid = user is not null
            && user.IsActive
            && (user.LockoutEndUtc is null || user.LockoutEndUtc <= DateTimeOffset.UtcNow)
            && string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal)
            && string.Equals(user.Role, roleValue, StringComparison.Ordinal)
            && Guid.TryParse(companyIdValue, out var companyId)
            && companyId == user.CompanyId;

        if (isValid)
        {
            return;
        }

        dbContext.AuthenticationAuditEntries.Add(new AuthenticationAuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            CompanyId = user?.CompanyId,
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
