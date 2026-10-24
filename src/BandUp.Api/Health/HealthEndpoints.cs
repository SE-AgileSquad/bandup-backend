using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BandUp.Api.Health;

/// <summary>
/// Infrastructure probes. They sit at the root rather than under <c>/api</c> because they
/// describe the process, not the product.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/live", () => Results.Ok(new { status = "Healthy" }))
            .WithName("Health_Live")
            .WithTags("health");

        endpoints.MapGet("/health", (HealthCheckService health, HttpContext httpContext) =>
                WriteReportAsync(health, httpContext, includeReadyChecks: false))
            .WithName("Health_All")
            .WithTags("health");

        endpoints.MapGet("/ready", (HealthCheckService health, HttpContext httpContext) =>
                WriteReportAsync(health, httpContext, includeReadyChecks: true))
            .WithName("Health_Ready")
            .WithTags("health");

        return endpoints;
    }

    private static async Task<IResult> WriteReportAsync(
        HealthCheckService health,
        HttpContext httpContext,
        bool includeReadyChecks)
    {
        HealthReport report = await health.CheckHealthAsync(
            registration => includeReadyChecks || registration.Tags.Contains(HealthCheckNames.DependencyTag),
            httpContext.RequestAborted);

        Dictionary<string, string> checks = report.Entries
            .ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString(), StringComparer.Ordinal);

        object body = new { status = report.Status.ToString(), checks };

        return report.Status == HealthStatus.Healthy
            ? Results.Json(body, statusCode: StatusCodes.Status200OK)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}