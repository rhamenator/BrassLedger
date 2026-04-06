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
            options.AddPolicy(BrassLedgerAuthorizationPolicies.AdministerSystem, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.IsInRole("Administrator")
                    || context.User.IsInRole("Owner/CEO")
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
}
