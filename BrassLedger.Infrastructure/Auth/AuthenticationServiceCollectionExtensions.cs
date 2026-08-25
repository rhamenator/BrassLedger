using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Auth;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddBrassLedgerCookieAuthentication(this IServiceCollection services)
    {
        services.AddScoped<BrassLedgerCookieEvents>();

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
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManageOperations, policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.RequisitionManage)
                    || context.User.HasClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PurchasingManage)));
            options.AddPolicy(BrassLedgerAuthorizationPolicies.ManagePayroll, policy =>
                policy.RequireClaim(BrassLedgerAuthenticationDefaults.PermissionClaimType, BrassLedgerPermissions.PayrollManage));
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
