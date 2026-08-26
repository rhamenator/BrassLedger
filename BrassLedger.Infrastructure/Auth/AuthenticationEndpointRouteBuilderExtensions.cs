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
        Delegate formLoginHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, IAntiforgery, Task<IResult>>)HandleFormLoginAsync;
        Delegate formLogoutHandler = (Func<HttpContext, IUserSessionService, IAntiforgery, Task<IResult>>)HandleFormLogoutAsync;
        Delegate formSwitchCompanyHandler = (Func<HttpContext, IUserAuthenticationService, IAntiforgery, Task<IResult>>)HandleFormSwitchCompanyAsync;
        Delegate bootstrapHandler = (Func<HttpContext, IBootstrapWorkspaceService, IUserSessionService, IAntiforgery, Task<IResult>>)HandleBootstrapAsync;
        Delegate changePasswordHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, IAntiforgery, Task<IResult>>)HandleChangePasswordAsync;
        Delegate revokeOtherSessionsHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, IAntiforgery, Task<IResult>>)HandleRevokeOtherSessionsAsync;
        Delegate formMfaHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, IAntiforgery, Task<IResult>>)HandleFormMfaAsync;
        Delegate disableMfaHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, IAntiforgery, Task<IResult>>)HandleDisableMfaAsync;
        Delegate apiLoginHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, Task<IResult>>)HandleApiLoginAsync;
        Delegate apiMfaHandler = (Func<HttpContext, IUserAuthenticationService, IUserSessionService, Task<IResult>>)HandleApiMfaAsync;
        Delegate apiLogoutHandler = (Func<HttpContext, IUserSessionService, IAntiforgery, Task<IResult>>)HandleApiLogoutAsync;
        Delegate switchCompanyHandler = (Func<HttpContext, IUserAuthenticationService, IAntiforgery, Task<IResult>>)HandleSwitchCompanyAsync;
        Delegate passwordResetRequestHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandlePasswordResetRequestAsync;
        Delegate accountActionStartHandler = (Func<HttpContext, IAccountActionService, Task<IResult>>)HandleAccountActionStartAsync;
        Delegate accountActionCompleteHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandleAccountActionCompleteAsync;
        Delegate emailVerificationRequestHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandleEmailVerificationRequestAsync;
        Delegate emailChangeHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandleEmailChangeAsync;
        Delegate emailVerificationCompleteHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandleEmailVerificationCompleteAsync;
        Delegate apiPasswordResetRequestHandler = (Func<HttpContext, IAccountActionService, Task<IResult>>)HandleApiPasswordResetRequestAsync;
        Delegate apiAccountActionCompleteHandler = (Func<HttpContext, IAccountActionService, Task<IResult>>)HandleApiAccountActionCompleteAsync;
        Delegate apiEmailVerificationRequestHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandleApiEmailVerificationRequestAsync;
        Delegate apiEmailVerificationCompleteHandler = (Func<HttpContext, IAccountActionService, Task<IResult>>)HandleApiEmailVerificationCompleteAsync;
        Delegate apiEmailChangeHandler = (Func<HttpContext, IAccountActionService, IAntiforgery, Task<IResult>>)HandleApiEmailChangeAsync;

        endpoints.MapPost("/account/login", formLoginHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.LoginRateLimitPolicy);
        endpoints.MapPost("/account/logout", formLogoutHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity);
        endpoints.MapPost("/account/active-company", formSwitchCompanyHandler).RequireAuthorization();
        endpoints.MapPost("/account/change-password", changePasswordHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity);
        endpoints.MapPost("/account/revoke-other-sessions", revokeOtherSessionsHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity);
        endpoints.MapPost("/account/mfa", formMfaHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.LoginRateLimitPolicy);
        endpoints.MapPost("/account/disable-mfa", disableMfaHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity);
        endpoints.MapPost("/account/bootstrap", bootstrapHandler).AllowAnonymous();
        endpoints.MapPost("/account/password-reset/request", passwordResetRequestHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapGet("/account/action/start", accountActionStartHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/account/action", accountActionCompleteHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/account/email-verification/request", emailVerificationRequestHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity).RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/account/email/change", emailChangeHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity).RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/account/action/verify-email", emailVerificationCompleteHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);

        endpoints.MapPost("/api/auth/login", apiLoginHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.LoginRateLimitPolicy);
        endpoints.MapPost("/api/auth/mfa", apiMfaHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.LoginRateLimitPolicy);
        endpoints.MapPost("/api/auth/logout", apiLogoutHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity).WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapGet("/api/auth/me", (ClaimsPrincipal principal) => Results.Ok(ToResponse(principal))).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity);
        endpoints.MapPost("/api/auth/active-company", switchCompanyHandler).RequireAuthorization().WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapPost("/api/auth/password-reset/request", apiPasswordResetRequestHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/api/auth/account-action", apiAccountActionCompleteHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/api/auth/email-verification/request", apiEmailVerificationRequestHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity).RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy).WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapPost("/api/auth/email-verification/complete", apiEmailVerificationCompleteHandler).AllowAnonymous().RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy);
        endpoints.MapPost("/api/auth/email/change", apiEmailChangeHandler).RequireAuthorization(BrassLedgerAuthorizationPolicies.ManageAccountSecurity).RequireRateLimiting(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        return endpoints;
    }

    private static async Task<IResult> HandlePasswordResetRequestAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        ApplyNoStoreHeaders(context.Response);
        var form = await context.Request.ReadFormAsync();
        await accountActionService.RequestPasswordResetAsync(
            form["identifier"].ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        return Results.LocalRedirect("/forgot-password?status=requested");
    }

    private static async Task<IResult> HandleAccountActionStartAsync(HttpContext context, IAccountActionService accountActionService)
    {
        ApplyNoStoreHeaders(context.Response);
        var token = context.Request.Query["token"].ToString();
        if (await accountActionService.GetActionAsync(token, context.RequestAborted) is null)
        {
            DeleteAccountActionCookie(context);
            return Results.LocalRedirect("/account/action?error=invalid-or-expired");
        }
        context.Response.Cookies.Append(
            BrassLedgerAuthenticationDefaults.AccountActionCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromHours(24),
                Path = "/account/action"
            });
        return Results.LocalRedirect("/account/action");
    }

    private static async Task<IResult> HandleAccountActionCompleteAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        ApplyNoStoreHeaders(context.Response);
        if (!context.Request.Cookies.TryGetValue(BrassLedgerAuthenticationDefaults.AccountActionCookieName, out var token))
            return Results.LocalRedirect("/account/action?error=invalid-or-expired");
        var form = await context.Request.ReadFormAsync();
        var result = await accountActionService.CompleteAsync(
            token,
            form["newPassword"].ToString(),
            form["confirmPassword"].ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (result.Outcome == AccountActionCompletionOutcome.InvalidPassword)
            return Results.LocalRedirect("/account/action?error=invalid-password");
        if (result.Outcome != AccountActionCompletionOutcome.Succeeded)
        {
            DeleteAccountActionCookie(context);
            return Results.LocalRedirect("/account/action?error=invalid-or-expired");
        }
        DeleteAccountActionCookie(context);
        return Results.LocalRedirect(result.Purpose == "Invitation" ? "/login?status=invitation-accepted" : "/login?status=password-reset");
    }

    private static async Task<IResult> HandleApiPasswordResetRequestAsync(HttpContext context, IAccountActionService accountActionService)
    {
        ApplyNoStoreHeaders(context.Response);
        var request = await context.Request.ReadFromJsonAsync<PasswordResetRequest>(cancellationToken: context.RequestAborted);
        if (request is not null)
            await accountActionService.RequestPasswordResetAsync(request.Identifier, context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        return Results.Accepted(value: new { Message = "If the account and verified email are eligible, a one-time reset link will be sent." });
    }

    private static async Task<IResult> HandleApiAccountActionCompleteAsync(HttpContext context, IAccountActionService accountActionService)
    {
        ApplyNoStoreHeaders(context.Response);
        var request = await context.Request.ReadFromJsonAsync<AccountActionCompleteRequest>(cancellationToken: context.RequestAborted);
        if (request is null) return Results.BadRequest();
        var result = await accountActionService.CompleteAsync(request.Token, request.NewPassword, request.ConfirmPassword, context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        return result.Outcome switch
        {
            AccountActionCompletionOutcome.Succeeded => Results.Ok(new { result.Purpose, Message = "The account credential was updated. Sign in through the normal login flow." }),
            AccountActionCompletionOutcome.InvalidPassword => Results.BadRequest(new { Error = "invalid_password" }),
            _ => Results.BadRequest(new { Error = "invalid_or_expired_action" })
        };
    }

    private static async Task<IResult> HandleEmailVerificationRequestAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        ApplyNoStoreHeaders(context.Response);
        var result = await accountActionService.RequestEmailVerificationAsync(
            userId,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        return Results.LocalRedirect(result.Succeeded
            ? "/account/security?status=email-verification-requested"
            : "/account/security?error=email-verification-unavailable");
    }

    private static async Task<IResult> HandleEmailVerificationCompleteAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        ApplyNoStoreHeaders(context.Response);
        if (!context.Request.Cookies.TryGetValue(BrassLedgerAuthenticationDefaults.AccountActionCookieName, out var token))
            return Results.LocalRedirect("/account/action?error=invalid-or-expired");
        var result = await accountActionService.CompleteEmailVerificationAsync(
            token,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        DeleteAccountActionCookie(context);
        return result.Outcome == AccountActionCompletionOutcome.Succeeded
            ? Results.LocalRedirect("/login?status=email-verified")
            : Results.LocalRedirect("/account/action?error=invalid-or-expired");
    }

    private static async Task<IResult> HandleEmailChangeAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        ApplyNoStoreHeaders(context.Response);
        var form = await context.Request.ReadFormAsync();
        var result = await accountActionService.ChangeEmailAsync(
            userId,
            form["newEmail"].ToString(),
            form["currentPassword"].ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (!result.Succeeded) return Results.LocalRedirect("/account/security?error=email-change-rejected");
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.LocalRedirect("/login?status=email-change-verification-sent");
    }

    private static async Task<IResult> HandleApiEmailVerificationRequestAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        ApplyNoStoreHeaders(context.Response);
        var result = await accountActionService.RequestEmailVerificationAsync(
            userId,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        return result.Succeeded ? Results.Accepted() : Results.BadRequest(new { Error = "email_verification_unavailable" });
    }

    private static async Task<IResult> HandleApiEmailVerificationCompleteAsync(HttpContext context, IAccountActionService accountActionService)
    {
        ApplyNoStoreHeaders(context.Response);
        var request = await context.Request.ReadFromJsonAsync<EmailVerificationCompleteRequest>(cancellationToken: context.RequestAborted);
        if (request is null) return Results.BadRequest();
        var result = await accountActionService.CompleteEmailVerificationAsync(
            request.Token,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        return result.Outcome == AccountActionCompletionOutcome.Succeeded
            ? Results.Ok(new { Message = "The account email address was verified." })
            : Results.BadRequest(new { Error = "invalid_or_expired_action" });
    }

    private static async Task<IResult> HandleApiEmailChangeAsync(HttpContext context, IAccountActionService accountActionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Results.Unauthorized();
        ApplyNoStoreHeaders(context.Response);
        var request = await context.Request.ReadFromJsonAsync<EmailChangeRequest>(cancellationToken: context.RequestAborted);
        if (request is null) return Results.BadRequest();
        var result = await accountActionService.ChangeEmailAsync(
            userId,
            request.NewEmail,
            request.CurrentPassword,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (!result.Succeeded) return Results.BadRequest(new { Error = "email_change_rejected" });
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.Accepted(value: new { Message = "The replacement address is pending verification; prior sessions were invalidated." });
    }

    private static async Task<IResult> HandleChangePasswordAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!TryGetSessionIdentity(context.User, out var userId, out var companyId)) return Results.Unauthorized();
        var form = await context.Request.ReadFormAsync();
        var result = await authenticationService.ChangePasswordAsync(
            userId,
            companyId,
            form["currentPassword"].ToString(),
            form["newPassword"].ToString(),
            form["confirmPassword"].ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (result.Outcome == AccountSecurityOutcome.Unauthorized) return Results.Unauthorized();
        if (result.Outcome != AccountSecurityOutcome.Succeeded || result.User is null)
        {
            var error = result.Outcome switch
            {
                AccountSecurityOutcome.InvalidCurrentPassword => "current-password",
                AccountSecurityOutcome.PasswordReused => "password-reused",
                _ => "invalid-password"
            };
            return Results.LocalRedirect($"/account/security?error={error}");
        }

        var reissuedUser = await sessionService.IssueAsync(
            result.User with { MfaAuthenticated = HasMfaClaim(context.User) },
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(reissuedUser),
            CreateAuthenticationProperties());
        return Results.LocalRedirect("/account/security?status=password-changed");
    }

    private static async Task<IResult> HandleRevokeOtherSessionsAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!TryGetSessionIdentity(context.User, out var userId, out var companyId)) return Results.Unauthorized();
        var result = await authenticationService.RevokeOtherSessionsAsync(
            userId,
            companyId,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (result.Outcome != AccountSecurityOutcome.Succeeded || result.User is null) return Results.Unauthorized();
        var reissuedUser = await sessionService.IssueAsync(
            result.User with { MfaAuthenticated = HasMfaClaim(context.User) },
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(reissuedUser),
            CreateAuthenticationProperties());
        return Results.LocalRedirect("/account/security?status=sessions-revoked");
    }

    private static async Task<IResult> HandleFormLoginAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService, IAntiforgery antiforgery)
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
        if (authenticationResult.Outcome == AuthenticationOutcome.MfaRequired && !string.IsNullOrWhiteSpace(authenticationResult.MfaChallengeToken))
        {
            SetMfaChallengeCookie(context, authenticationResult.MfaChallengeToken);
            return Results.LocalRedirect($"/login/mfa?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
        if (authenticationResult.Outcome != AuthenticationOutcome.Succeeded || authenticationResult.User is null)
        {
            var errorCode = authenticationResult.Outcome == AuthenticationOutcome.LockedOut
                ? "account-locked"
                : "invalid-credentials";

            return Results.LocalRedirect($"/login?error={errorCode}&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var signedInUser = await sessionService.IssueAsync(authenticationResult.User, ipAddress, userAgent, context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(signedInUser),
            CreateAuthenticationProperties());

        return Results.LocalRedirect(signedInUser.MfaEnrollmentRequired ? "/account/security?status=mfa-required" : returnUrl);
    }

    private static async Task<IResult> HandleApiLoginAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService)
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
        if (authenticationResult.Outcome == AuthenticationOutcome.MfaRequired)
        {
            return Results.Json(
                new { MfaRequired = true, ChallengeToken = authenticationResult.MfaChallengeToken, ExpiresInSeconds = BrassLedgerAuthenticationDefaults.MfaChallengeMinutes * 60 },
                statusCode: StatusCodes.Status202Accepted);
        }
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

        var signedInUser = await sessionService.IssueAsync(authenticationResult.User, ipAddress, userAgent, context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(signedInUser),
            CreateAuthenticationProperties());

        return Results.Ok(ToResponse(signedInUser));
    }

    private static async Task<IResult> HandleFormMfaAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        ApplyNoStoreHeaders(context.Response);
        var form = await context.Request.ReadFormAsync();
        var returnUrl = SanitizeReturnUrl(form["returnUrl"].ToString());
        if (!context.Request.Cookies.TryGetValue(BrassLedgerAuthenticationDefaults.MfaChallengeCookieName, out var challengeToken))
            return Results.LocalRedirect($"/login?error=mfa-expired&returnUrl={Uri.EscapeDataString(returnUrl)}");
        var result = await authenticationService.CompleteMfaChallengeAsync(
            challengeToken,
            form["verificationCode"].ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (result.Outcome != MfaOperationOutcome.Succeeded || result.User is null)
        {
            if (result.Outcome == MfaOperationOutcome.Expired)
            {
                DeleteMfaChallengeCookie(context);
                return Results.LocalRedirect($"/login?error=mfa-expired&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }
            if (result.Outcome is MfaOperationOutcome.LockedOut or MfaOperationOutcome.Unauthorized)
            {
                DeleteMfaChallengeCookie(context);
                return Results.LocalRedirect($"/login?error=mfa-locked&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }
            return Results.LocalRedirect($"/login/mfa?error=invalid-code&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        DeleteMfaChallengeCookie(context);
        var signedInUser = await sessionService.IssueAsync(
            result.User,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(signedInUser),
            CreateAuthenticationProperties());
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> HandleApiMfaAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService)
    {
        var request = await context.Request.ReadFromJsonAsync<MfaLoginRequest>(cancellationToken: context.RequestAborted);
        if (request is null) return Results.BadRequest();
        ApplyNoStoreHeaders(context.Response);
        var result = await authenticationService.CompleteMfaChallengeAsync(
            request.ChallengeToken,
            request.VerificationCode,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (result.Outcome == MfaOperationOutcome.LockedOut)
            return Results.Json(new { Error = "mfa_locked", LockedUntilUtc = result.LockoutEndUtc }, statusCode: StatusCodes.Status423Locked);
        if (result.Outcome != MfaOperationOutcome.Succeeded || result.User is null) return Results.Unauthorized();
        var signedInUser = await sessionService.IssueAsync(
            result.User,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(signedInUser),
            CreateAuthenticationProperties());
        return Results.Ok(ToResponse(signedInUser));
    }

    private static async Task<IResult> HandleDisableMfaAsync(HttpContext context, IUserAuthenticationService authenticationService, IUserSessionService sessionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (!TryGetSessionIdentity(context.User, out var userId, out var companyId)) return Results.Unauthorized();
        var form = await context.Request.ReadFormAsync();
        var result = await authenticationService.DisableMfaAsync(
            userId,
            companyId,
            form["currentPassword"].ToString(),
            form["verificationCode"].ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(),
            context.RequestAborted);
        if (result.Outcome != AccountSecurityOutcome.Succeeded || result.User is null)
            return Results.LocalRedirect("/account/security?error=mfa-disable-rejected");
        var signedInUser = await sessionService.IssueAsync(
            result.User,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(signedInUser),
            CreateAuthenticationProperties());
        return Results.LocalRedirect("/account/security?status=mfa-disabled");
    }

    private static async Task<IResult> HandleBootstrapAsync(HttpContext context, IBootstrapWorkspaceService bootstrapWorkspaceService, IUserSessionService sessionService, IAntiforgery antiforgery)
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

        var signedInUser = await sessionService.IssueAsync(
            result.User,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignInAsync(
            BrassLedgerAuthenticationDefaults.Scheme,
            CreatePrincipal(signedInUser),
            CreateAuthenticationProperties());

        return Results.LocalRedirect(signedInUser.MfaEnrollmentRequired ? "/account/security?status=mfa-required" : "/");
    }

    private static async Task<IResult> HandleFormLogoutAsync(HttpContext context, IUserSessionService sessionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && Guid.TryParse(context.User.FindFirstValue(BrassLedgerAuthenticationDefaults.SessionIdClaimType), out var sessionId))
            await sessionService.RevokeCurrentAsync(userId, TryGetCompanyId(context.User), sessionId, context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
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
        if (!Guid.TryParse(context.User.FindFirstValue(BrassLedgerAuthenticationDefaults.SessionIdClaimType), out var sessionId)) return Results.Unauthorized();
        user = user with { MfaAuthenticated = HasMfaClaim(context.User), SessionId = sessionId };
        await context.SignInAsync(BrassLedgerAuthenticationDefaults.Scheme, CreatePrincipal(user), CreateAuthenticationProperties());
        return Results.LocalRedirect(user.MfaEnrollmentRequired ? "/account/security?status=mfa-required" : returnUrl);
    }

    private static async Task<IResult> HandleApiLogoutAsync(HttpContext context, IUserSessionService sessionService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && Guid.TryParse(context.User.FindFirstValue(BrassLedgerAuthenticationDefaults.SessionIdClaimType), out var sessionId))
            await sessionService.RevokeCurrentAsync(userId, TryGetCompanyId(context.User), sessionId, context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, context.Request.Headers.UserAgent.ToString(), context.RequestAborted);
        await context.SignOutAsync(BrassLedgerAuthenticationDefaults.Scheme);
        return Results.NoContent();
    }

    private static async Task<bool> IsAntiforgeryRequestValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var validationFeature = context.Features.Get<IAntiforgeryValidationFeature>();
        if (validationFeature is not null) return validationFeature.IsValid;
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static async Task<IResult> HandleSwitchCompanyAsync(HttpContext context, IUserAuthenticationService authenticationService, IAntiforgery antiforgery)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery)) return Results.BadRequest();
        var request = await context.Request.ReadFromJsonAsync<SwitchCompanyRequest>(cancellationToken: context.RequestAborted);
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (request is null || !Guid.TryParse(userId, out var parsedUserId) || request.CompanyId == Guid.Empty) return Results.BadRequest();
        var user = await authenticationService.SwitchCompanyAsync(parsedUserId, request.CompanyId, context.RequestAborted);
        if (user is null) return Results.Forbid();
        if (!Guid.TryParse(context.User.FindFirstValue(BrassLedgerAuthenticationDefaults.SessionIdClaimType), out var sessionId)) return Results.Unauthorized();
        user = user with { MfaAuthenticated = HasMfaClaim(context.User), SessionId = sessionId };
        await context.SignInAsync(BrassLedgerAuthenticationDefaults.Scheme, CreatePrincipal(user), CreateAuthenticationProperties());
        return Results.Ok(ToResponse(user));
    }

    private static ClaimsPrincipal CreatePrincipal(AuthenticatedUser authenticatedUser)
    {
        if (!authenticatedUser.SessionId.HasValue || authenticatedUser.SessionId == Guid.Empty)
            throw new InvalidOperationException("A durable user session must be issued before creating an authentication cookie.");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, authenticatedUser.UserId.ToString()),
            new(ClaimTypes.Name, authenticatedUser.UserName),
            new(ClaimTypes.Email, authenticatedUser.Email),
            new(ClaimTypes.Role, authenticatedUser.Role),
            new(BrassLedgerAuthenticationDefaults.DisplayNameClaimType, authenticatedUser.DisplayName),
            new(BrassLedgerAuthenticationDefaults.CompanyIdClaimType, authenticatedUser.CompanyId.ToString()),
            new(BrassLedgerAuthenticationDefaults.SecurityStampClaimType, authenticatedUser.SecurityStamp),
            new(BrassLedgerAuthenticationDefaults.SessionIdClaimType, authenticatedUser.SessionId.Value.ToString()),
            new(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType, authenticatedUser.MfaAuthenticated ? "mfa" : "pwd")
        };

        if (authenticatedUser.MfaEnrollmentRequired)
            claims.Add(new Claim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true"));

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
            CompanyId = principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType) ?? string.Empty,
            MfaAuthenticated = HasMfaClaim(principal),
            MfaEnrollmentRequired = principal.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true")
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
            CompanyId = authenticatedUser.CompanyId,
            authenticatedUser.MfaAuthenticated,
            authenticatedUser.MfaEnrollmentRequired
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

    private static bool TryGetSessionIdentity(ClaimsPrincipal principal, out Guid userId, out Guid companyId)
    {
        var hasUserId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        var hasCompanyId = Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out companyId);
        return hasUserId && hasCompanyId;
    }

    private static Guid? TryGetCompanyId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(BrassLedgerAuthenticationDefaults.CompanyIdClaimType), out var companyId)
            ? companyId
            : null;

    private static bool HasMfaClaim(ClaimsPrincipal principal) =>
        principal.HasClaim(BrassLedgerAuthenticationDefaults.AuthenticationMethodClaimType, "mfa");

    private static void SetMfaChallengeCookie(HttpContext context, string token)
    {
        context.Response.Cookies.Append(
            BrassLedgerAuthenticationDefaults.MfaChallengeCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(BrassLedgerAuthenticationDefaults.MfaChallengeMinutes),
                Path = "/account/mfa"
            });
    }

    private static void DeleteMfaChallengeCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(
            BrassLedgerAuthenticationDefaults.MfaChallengeCookieName,
            new CookieOptions { Path = "/account/mfa", SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps });
    }

    private static void DeleteAccountActionCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(
            BrassLedgerAuthenticationDefaults.AccountActionCookieName,
            new CookieOptions { Path = "/account/action", SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps });
    }

    private sealed record LoginRequest(string UserName, string Password);
    private sealed record MfaLoginRequest(string ChallengeToken, string VerificationCode);
    private sealed record SwitchCompanyRequest(Guid CompanyId);
    private sealed record PasswordResetRequest(string Identifier);
    private sealed record AccountActionCompleteRequest(string Token, string NewPassword, string ConfirmPassword);
    private sealed record EmailVerificationCompleteRequest(string Token);
    private sealed record EmailChangeRequest(string NewEmail, string CurrentPassword);
}
