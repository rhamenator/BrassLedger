using System.Security.Claims;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BrassLedger.Infrastructure.Auth;

public static class AuthenticationEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapBrassLedgerAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Delegate formLoginHandler = (Func<HttpContext, IUserAuthenticationService, IAntiforgery, Task<IResult>>)HandleFormLoginAsync;
        Delegate formLogoutHandler = (Func<HttpContext, IAntiforgery, Task<IResult>>)HandleFormLogoutAsync;
        Delegate formSwitchCompanyHandler = (Func<HttpContext, IUserAuthenticationService, IAntiforgery, Task<IResult>>)HandleFormSwitchCompanyAsync;
        Delegate bootstrapHandler = (Func<HttpContext, IBootstrapWorkspaceService, IAntiforgery, Task<IResult>>)HandleBootstrapAsync;
        Delegate apiLoginHandler = (Func<HttpContext, IUserAuthenticationService, Task<IResult>>)HandleApiLoginAsync;
        Delegate apiLogoutHandler = (Func<HttpContext, Task<IResult>>)HandleApiLogoutAsync;
        Delegate switchCompanyHandler = (Func<HttpContext, IUserAuthenticationService, Task<IResult>>)HandleSwitchCompanyAsync;

        endpoints.MapPost("/account/login", formLoginHandler).AllowAnonymous();
        endpoints.MapPost("/account/logout", formLogoutHandler).RequireAuthorization();
        endpoints.MapPost("/account/active-company", formSwitchCompanyHandler).RequireAuthorization();
        endpoints.MapPost("/account/bootstrap", bootstrapHandler).AllowAnonymous();

        endpoints.MapPost("/api/auth/login", apiLoginHandler).AllowAnonymous();
        endpoints.MapPost("/api/auth/logout", apiLogoutHandler).RequireAuthorization();
        endpoints.MapGet("/api/auth/me", (ClaimsPrincipal principal) => Results.Ok(ToResponse(principal))).RequireAuthorization();
        endpoints.MapPost("/api/auth/active-company", switchCompanyHandler).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> HandleFormLoginAsync(HttpContext context, IUserAuthenticationService authenticationService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
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

    private static async Task<IResult> HandleBootstrapAsync(HttpContext context, IBootstrapWorkspaceService bootstrapWorkspaceService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync();
        var request = new BootstrapWorkspaceRequest(
            form["companyName"].ToString(),
            form["legalName"].ToString(),
            form["taxId"].ToString(),
            form["baseCurrency"].ToString(),
            int.TryParse(form["fiscalYearStartMonth"], out var fiscalMonth) ? fiscalMonth : 1,
            form["adminUserName"].ToString(),
            form["adminDisplayName"].ToString(),
            form["adminEmail"].ToString(),
            form["adminPassword"].ToString(),
            form["confirmAdminPassword"].ToString());

        var result = await bootstrapWorkspaceService.CreateInitialWorkspaceAsync(request, context.RequestAborted);
        if (result.Outcome == BootstrapWorkspaceOutcome.AlreadyConfigured)
        {
            return Results.LocalRedirect("/login");
        }

        if (result.Outcome == BootstrapWorkspaceOutcome.Invalid || result.User is null)
        {
            var message = Uri.EscapeDataString(result.ErrorMessage);
            return Results.LocalRedirect($"/setup?error={message}");
        }

        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(result.User),
            CreateAuthenticationProperties());

        return Results.LocalRedirect("/");
    }

    private static async Task<IResult> HandleFormLogoutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.LocalRedirect("/login");
    }

    private static async Task<IResult> HandleFormSwitchCompanyAsync(HttpContext context, IUserAuthenticationService authenticationService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync();
        var returnUrl = SanitizeReturnUrl(form["returnUrl"].ToString());
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || !Guid.TryParse(form["companyId"].ToString(), out var companyId)) return Results.BadRequest();
        var user = await authenticationService.SwitchCompanyAsync(userId, companyId, context.RequestAborted);
        if (user is null) return Results.Forbid();
        await context.SignInAsync(BrassLedgerAuthenticationDefaults.Scheme, CreatePrincipal(user), CreateAuthenticationProperties());
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> HandleApiLogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.NoContent();
    }

    private static async Task<bool> IsAntiforgeryRequestValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static async Task<IResult> HandleSwitchCompanyAsync(HttpContext context, IUserAuthenticationService authenticationService)
    {
        var request = await context.Request.ReadFromJsonAsync<SwitchCompanyRequest>(cancellationToken: context.RequestAborted);
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (request is null || !Guid.TryParse(userId, out var parsedUserId) || request.CompanyId == Guid.Empty) return Results.BadRequest();
        var user = await authenticationService.SwitchCompanyAsync(parsedUserId, request.CompanyId, context.RequestAborted);
        if (user is null) return Results.Forbid();
        await context.SignInAsync(BrassLedgerAuthenticationDefaults.Scheme, CreatePrincipal(user), CreateAuthenticationProperties());
        return Results.Ok(ToResponse(user));
    }

    private static ClaimsPrincipal CreatePrincipal(AuthenticatedUser authenticatedUser)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, authenticatedUser.UserId.ToString()),
            new(ClaimTypes.Name, authenticatedUser.UserName),
            new(ClaimTypes.Email, authenticatedUser.Email),
            new(ClaimTypes.Role, authenticatedUser.Role),
            new(BrassLedgerAuthenticationDefaults.DisplayNameClaimType, authenticatedUser.DisplayName),
            new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, authenticatedUser.CompanyId.ToString()),
            new(BrassLedgerAuthenticationDefaults.SecurityStampClaimType, authenticatedUser.SecurityStamp)
        };

        claims.AddRange(authenticatedUser.Permissions.Select(permission => new Claim(BrassLedgerAuthenticationDefaults.PermissionClaimType, permission)));

        var identity = new ClaimsIdentity(claims, BrassLedgerAuthenticationDefaults.Scheme);

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
    private sealed record SwitchCompanyRequest(Guid CompanyId);
}
