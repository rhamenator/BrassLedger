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
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { Error = "too_many_login_attempts", RetryAfterSeconds = 60 },
                        cancellationToken);
                    return;
                }

                context.HttpContext.Response.Redirect("/login?error=too-many-requests");
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
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PurchasingManage)));
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
                    context.User.IsInRole("Administrator")
                    || context.User.IsInRole("Owner/CEO")
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.TaxManage));
            });
        });

        return services;
    }

    private static bool IsSystemAdministrator(System.Security.Claims.ClaimsPrincipal user) =>
        user.IsInRole("Administrator") || user.IsInRole("Owner/CEO");
}
