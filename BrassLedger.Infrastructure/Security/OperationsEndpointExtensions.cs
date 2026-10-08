using System.Diagnostics;
using System.Text.RegularExpressions;
using BrassLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace BrassLedger.Infrastructure.Security;

/// <summary>
/// Operational endpoints and middleware shared by the API and web hosts: correlation IDs,
/// RFC 9457 problem-details error responses, and liveness/readiness health checks.
/// </summary>
public static class OperationsEndpointExtensions
{
    public const string CorrelationIdHeader = "X-Correlation-ID";

    private static readonly Regex SafeCorrelationId = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IServiceCollection AddBrassLedgerOperations(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        });
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"]);
        return services;
    }

    /// <summary>
    /// Must run first: it assigns a correlation ID (accepting a well-formed caller value, otherwise
    /// generating one), echoes it on the response, and scopes logging with it.
    /// </summary>
    public static IApplicationBuilder UseBrassLedgerCorrelationId(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers[CorrelationIdHeader].ToString().Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
            var correlationId = SafeCorrelationId.IsMatch(supplied) ? supplied : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            context.TraceIdentifier = correlationId;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationIdHeader] = correlationId;
                return Task.CompletedTask;
            });
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BrassLedger.Request");
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
                await next();
        });

    public static IEndpointRouteBuilder MapBrassLedgerHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
        return endpoints;
    }

    private sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BrassLedgerDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is unreachable.");
        }
    }
}
