using Microsoft.AspNetCore.Builder;

namespace BrassLedger.Infrastructure.Security;

public static class SecurityHeadersApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBrassLedgerSecurityHeaders(this IApplicationBuilder applicationBuilder)
    {
        return applicationBuilder.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

            await next();
        });
    }
}
