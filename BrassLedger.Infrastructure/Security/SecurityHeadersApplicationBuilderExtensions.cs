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
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "base-uri 'self'; " +
                "frame-ancestors 'none'; " +
                "object-src 'none'; " +
                "form-action 'self'; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "script-src 'self'; " +
                "connect-src 'self' ws: wss:";

            await next();
        });
    }
}
