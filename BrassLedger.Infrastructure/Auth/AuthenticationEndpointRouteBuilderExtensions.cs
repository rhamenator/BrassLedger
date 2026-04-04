using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BrassLedger.Infrastructure.Auth;

public static class AuthenticationEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapBrassLedgerAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Delegate formLoginHandler = (Func<HttpContext, IUserAuthenticationService, Task<IResult>>)HandleFormLoginAsync;
        Delegate formLogoutHandler = (Func<HttpContext, Task<IResult>>)HandleFormLogoutAsync;
        Delegate apiLoginHandler = (Func<HttpContext, IUserAuthenticationService, Task<IResult>>)HandleApiLoginAsync;
        Delegate apiLogoutHandler = (Func<HttpContext, Task<IResult>>)HandleApiLogoutAsync;

        endpoints.MapPost("/account/login", formLoginHandler).AllowAnonymous();
        endpoints.MapPost("/account/logout", formLogoutHandler).RequireAuthorization();

        endpoints.MapPost("/api/auth/login", apiLoginHandler).AllowAnonymous();
        endpoints.MapPost("/api/auth/logout", apiLogoutHandler).RequireAuthorization();
        endpoints.MapGet("/api/auth/me", (ClaimsPrincipal principal) => Results.Ok(ToResponse(principal))).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> HandleFormLoginAsync(HttpContext context, IUserAuthenticationService authenticationService)
    {
        var form = await context.Request.ReadFormAsync();
        var userName = form["userName"].ToString();
        var password = form["password"].ToString();
        var returnUrl = SanitizeReturnUrl(form["returnUrl"].ToString());
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = context.Request.Headers.UserAgent.ToString();

        ApplyNoStoreHeaders(context.Response);

        var authenticationResult = await authenticationService.AuthenticateAsync(userName, password, ipAddress, userAgent, context.RequestAborted);
        if (authenticationResult.Outcome != AuthenticationOutcome.Succeeded || authenticationResult.User is null)
        {
            var errorCode = authenticationResult.Outcome == AuthenticationOutcome.LockedOut
                ? "account-locked"
                : "invalid-credentials";

            return Results.LocalRedirect($"/login?error={errorCode}&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(authenticationResult.User),
            CreateAuthenticationProperties());

        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> HandleApiLoginAsync(HttpContext context, IUserAuthenticationService authenticationService)
    {
        var loginRequest = await context.Request.ReadFromJsonAsync<LoginRequest>(cancellationToken: context.RequestAborted);
        if (loginRequest is null)
        {
            return Results.BadRequest();
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        ApplyNoStoreHeaders(context.Response);

        var authenticationResult = await authenticationService.AuthenticateAsync(loginRequest.UserName, loginRequest.Password, ipAddress, userAgent, context.RequestAborted);
        if (authenticationResult.Outcome != AuthenticationOutcome.Succeeded || authenticationResult.User is null)
        {
            if (authenticationResult.Outcome == AuthenticationOutcome.LockedOut)
            {
                return Results.Json(
                    new
                    {
                        Error = "account_locked",
                        LockedUntilUtc = authenticationResult.LockoutEndUtc
                    },
                    statusCode: StatusCodes.Status423Locked);
            }

            return Results.Unauthorized();
        }

        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(authenticationResult.User),
            CreateAuthenticationProperties());

        return Results.Ok(ToResponse(authenticationResult.User));
    }

    private static async Task<IResult> HandleFormLogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.LocalRedirect("/login");
    }

    private static async Task<IResult> HandleApiLogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.NoContent();
    }

    private static ClaimsPrincipal CreatePrincipal(AuthenticatedUser authenticatedUser)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, authenticatedUser.UserId.ToString()),
                new Claim(ClaimTypes.Name, authenticatedUser.UserName),
                new Claim(ClaimTypes.Email, authenticatedUser.Email),
                new Claim(ClaimTypes.Role, authenticatedUser.Role),
                new Claim(BrassLedgerAuthenticationDefaults.DisplayNameClaimType, authenticatedUser.DisplayName),
                new Claim(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, authenticatedUser.CompanyId.ToString()),
                new Claim(BrassLedgerAuthenticationDefaults.SecurityStampClaimType, authenticatedUser.SecurityStamp)
            },
            BrassLedgerAuthenticationDefaults.Scheme);

        return new ClaimsPrincipal(identity);
    }

    private static AuthenticationProperties CreateAuthenticationProperties()
    {
        return new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = false,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(BrassLedgerAuthenticationDefaults.SessionMinutes)
        };
    }

    private static object ToResponse(ClaimsPrincipal principal)
    {
        return new
        {
            UserName = principal.Identity?.Name ?? string.Empty,
            DisplayName = principal.FindFirstValue(BrassLedgerAuthenticationDefaults.DisplayNameClaimType) ?? string.Empty,
            Email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            Role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            CompanyId = principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType) ?? string.Empty
        };
    }

    private static object ToResponse(AuthenticatedUser authenticatedUser)
    {
        return new
        {
            authenticatedUser.UserName,
            authenticatedUser.DisplayName,
            authenticatedUser.Email,
            authenticatedUser.Role,
            CompanyId = authenticatedUser.CompanyId
        };
    }

    private static void ApplyNoStoreHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache";
        response.Headers.Pragma = "no-cache";
    }

    private static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (!returnUrl.StartsWith("/", StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    private sealed record LoginRequest(string UserName, string Password);
}
