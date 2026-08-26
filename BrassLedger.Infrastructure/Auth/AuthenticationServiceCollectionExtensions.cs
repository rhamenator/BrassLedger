using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace BrassLedger.Infrastructure.Auth;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddBrassLedgerCookieAuthentication(this IServiceCollection services)
    {
        services.AddScoped<BrassLedgerCookieEvents>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                if (context.HttpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    var isAccountRecovery = context.HttpContext.Request.Path.StartsWithSegments("/api/auth/password-reset", StringComparison.OrdinalIgnoreCase)
                        || context.HttpContext.Request.Path.StartsWithSegments("/api/auth/account-action", StringComparison.OrdinalIgnoreCase)
                        || context.HttpContext.Request.Path.StartsWithSegments("/api/auth/email", StringComparison.OrdinalIgnoreCase);
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { Error = isAccountRecovery ? "too_many_account_recovery_attempts" : "too_many_login_attempts", RetryAfterSeconds = 60 },
                        cancellationToken);
                    return;
                }

                context.HttpContext.Response.Redirect(
                    context.HttpContext.Request.Path.StartsWithSegments("/account/password-reset", StringComparison.OrdinalIgnoreCase)
                        ? "/forgot-password?error=too-many-requests"
                        : context.HttpContext.Request.Path.StartsWithSegments("/account/email", StringComparison.OrdinalIgnoreCase)
                            ? "/account/security?error=too-many-requests"
                        : context.HttpContext.Request.Path.StartsWithSegments("/account/action", StringComparison.OrdinalIgnoreCase)
                            ? "/account/action?error=too-many-requests"
                            : "/login?error=too-many-requests");
            };
            options.AddPolicy(BrassLedgerAuthenticationDefaults.LoginRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = BrassLedgerAuthenticationDefaults.LoginRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(BrassLedgerAuthenticationDefaults.AccountRecoveryRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = BrassLedgerAuthenticationDefaults.AccountRecoveryRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        services
            .AddAuthentication(BrassLedgerAuthenticationDefaults.Scheme)
            .AddCookie(BrassLedgerAuthenticationDefaults.Scheme, options =>
            {
                options.Cookie.Name = BrassLedgerAuthenticationDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(BrassLedgerAuthenticationDefaults.SessionMinutes);
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.EventsType = typeof(BrassLedgerCookieEvents);
            });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => IsPrivilegedMfaSatisfied(context.User))
                .Build();
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageAccountSecurity, policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ViewWorkspace, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.WorkspaceView));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageLedger, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.LedgerManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PrepareJournals, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.JournalPrepare));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ApproveJournals, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.JournalApprove));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PostJournals, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.JournalPost));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ReverseJournals, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.JournalReverse));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageReceivables, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ReceivablesManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManagePayables, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayablesManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ReversePayments, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PaymentReverse));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PrepareSubledgerDocuments, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.SubledgerPrepare));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ApproveSubledgerDocuments, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.SubledgerApprove));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PostSubledgerDocuments, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.SubledgerPost));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageOperations, policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.RequisitionManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PurchasingManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.SalesManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.FulfillmentManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ReceivablesManage)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManagePayroll, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.AccessPayroll, policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollPrepare)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollApprove)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollPost)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollReverse)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollSensitiveData)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PreparePayroll, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollPrepare));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ApprovePayroll, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollApprove));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PostPayroll, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollPost));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ReversePayroll, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollReverse));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManagePayrollSensitiveData, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollSensitiveData));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.MaintainEmployeePayrollSetup, policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollManage)
                    && context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollSensitiveData)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageProjects, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectsManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.AccessProjects, policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectsManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectChangeOrderPrepare)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectChangeOrderApprove)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectBillingPrepare)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PrepareProjectChangeOrders, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectChangeOrderPrepare));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ApproveProjectChangeOrders, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectChangeOrderApprove));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.PrepareProjectBilling, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ProjectBillingPrepare));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageReporting, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.ReportingManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManagePublishing, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PublishManage));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageUsers, policy =>
                policy.RequireAssertion(context => IsSystemAdministrator(context.User) || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageRoles, policy =>
                policy.RequireAssertion(context => IsSystemAdministrator(context.User) || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.RoleManage)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.AdministerSystem, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    IsSystemAdministrator(context.User)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.RoleManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.UserManage));
            });

            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageTaxes, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    IsSystemAdministrator(context.User)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.TaxManage));
            });
        });

        return services;
    }

    private static bool IsSystemAdministrator(System.Security.Claims.ClaimsPrincipal user) =>
        IsPrivilegedMfaSatisfied(user) && (user.IsInRole("Administrator") || user.IsInRole("Owner/CEO"));

    private static bool IsPrivilegedMfaSatisfied(System.Security.Claims.ClaimsPrincipal user) =>
        !user.HasClaim(BrassLedgerAuthenticationDefaults.MfaEnrollmentRequiredClaimType, "true");
}
