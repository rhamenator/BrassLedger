using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BrassLedger.Infrastructure.Auth;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddBrassLedgerCookieAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(BrassLedgerAuthenticationDefaults.Scheme)
            .AddCookie(BrassLedgerAuthenticationDefaults.Scheme, options =>
            {
                options.Cookie.Name = BrassLedgerAuthenticationDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context => HandleApiRedirectAsync(context, StatusCodes.Status401Unauthorized),
                    OnRedirectToAccessDenied = context => HandleApiRedirectAsync(context, StatusCodes.Status403Forbidden)
                };
            });

        services.AddAuthorization();

        return services;
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
