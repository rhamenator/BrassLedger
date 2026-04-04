using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

        services.AddAuthorization();

        return services;
    }
}
