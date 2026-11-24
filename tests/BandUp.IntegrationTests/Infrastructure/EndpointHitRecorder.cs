using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// Measures endpoint coverage for real: every request the tests send is matched to the endpoint
/// that handled it and counted together with its status code. When the test run ends the result
/// is written to a report, so "X of Y endpoints have a test" is a measured number.
/// </summary>
public static partial class EndpointHitRecorder
{
    /// <summary>Set to a number (for example 70) to fail the run when coverage is lower.</summary>
    public const string MinimumEnvironmentVariable = "BANDUP_ENDPOINT_COVERAGE_MIN";

    private static readonly ConcurrentDictionary<string, byte> Known = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, int>> Hits =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Drops route constraints ({id:guid} becomes {id}) and a trailing slash, so endpoints are
    /// written the way people read routes.
    /// </summary>
    public static string Normalize(string pattern)
    {
        string withoutConstraints = ConstraintPattern().Replace(pattern, "{$1}");

        return withoutConstraints.Length > 1 ? withoutConstraints.TrimEnd('/') : withoutConstraints;
    }

    public static bool IsApplicationRoute(string path) =>
        path.StartsWith("/api/", StringComparison.Ordinal)
        || path is "/live" or "/health" or "/ready";

    public static void Record(HttpContext context, Endpoint? remembered)
    {
        foreach (RouteEndpoint endpoint in context.RequestServices
                     .GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            string path = Normalize(endpoint.RoutePattern.RawText ?? string.Empty);

            if (!IsApplicationRoute(path))
            {
                continue;
            }

            foreach (string method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                Known.TryAdd($"{method} {path}", 0);
            }
        }

        if ((remembered ?? context.GetEndpoint()) is not RouteEndpoint matched)
        {
            return;
        }

        string matchedPath = Normalize(matched.RoutePattern.RawText ?? string.Empty);

        if (!IsApplicationRoute(matchedPath))
        {
            return;
        }

        ConcurrentDictionary<int, int> statuses = Hits.GetOrAdd(
            $"{context.Request.Method} {matchedPath}",
            _ => new ConcurrentDictionary<int, int>());
        statuses.AddOrUpdate(context.Response.StatusCode, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Writes the report next to the test assembly and, when the minimum variable is set,
    /// fails the run if too few endpoints were hit.
    /// </summary>
    public static void WriteReportAndEnforceMinimum()
    {
        string[] known = [.. Known.Keys.Order(StringComparer.Ordinal)];

        if (known.Length == 0)
        {
            return;
        }

        List<string> lines =
        [
            "| Endpoint | Requests | Status codes seen | Tested |",
            "|---|---|---|---|",
        ];

        int covered = 0;

        foreach (string endpoint in known)
        {
            bool hit = Hits.TryGetValue(endpoint, out ConcurrentDictionary<int, int>? statuses);
            int requests = hit ? statuses!.Values.Sum() : 0;
            string codes = hit ? string.Join(", ", statuses!.Keys.Order()) : "-";

            if (hit)
            {
                covered++;
            }

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"| `{endpoint}` | {requests} | {codes} | {(hit ? "yes" : "NO")} |"));
        }

        double percent = 100.0 * covered / known.Length;
        string summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Endpoint coverage: {covered} of {known.Length} endpoints = {percent:0.0}%");

        string report = string.Join(Environment.NewLine, [summary, string.Empty, .. lines]);
        string path = Path.Combine(AppContext.BaseDirectory, "endpoint-coverage.md");

        File.WriteAllText(path, report + Environment.NewLine);
        Console.WriteLine($"{summary}  (report: {path})");

        string? minimumText = Environment.GetEnvironmentVariable(MinimumEnvironmentVariable);

        if (double.TryParse(minimumText, NumberStyles.Float, CultureInfo.InvariantCulture, out double minimum)
            && percent < minimum)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{summary}, below the required {minimum:0.#}%. See {path}."));
        }
    }

    [GeneratedRegex(@"\{(\w+):[^}]+\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConstraintPattern();
}

/// <summary>Wraps the whole pipeline so every request is counted once it has been answered.</summary>
public sealed class EndpointHitStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                // The exception handler clears the endpoint of a request that threw, so the
                // endpoint is remembered here the moment routing sets it.
                RememberingEndpointFeature feature = new();
                context.Features.Set<IEndpointFeature>(feature);

                try
                {
                    await nextMiddleware(context);
                }
                finally
                {
                    EndpointHitRecorder.Record(context, feature.LastNonNull);
                }
            });

            next(app);
        };
}

/// <summary>Behaves like the default endpoint feature but keeps the last endpoint that was set.</summary>
internal sealed class RememberingEndpointFeature : IEndpointFeature
{
    private Endpoint? _endpoint;

    public Endpoint? LastNonNull { get; private set; }

    public Endpoint? Endpoint
    {
        get => _endpoint;
        set
        {
            _endpoint = value;

            if (value is not null)
            {
                LastNonNull = value;
            }
        }
    }
}
