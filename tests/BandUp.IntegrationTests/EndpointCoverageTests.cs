using System.Net;
using System.Text.RegularExpressions;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// COV-01 and COV-02. Keeps the "every endpoint has an integration test" claim honest.
/// The manifest below lists every endpoint the API maps together with the tests that exercise it;
/// COV-01 fails when an endpoint is added or removed without updating it.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed partial class EndpointCoverageTests(BandUpTestEnvironment environment)
{
    /// <summary>
    /// Endpoint to the tests that call it. The values are documentation for the reader and for
    /// the coverage report; the keys are what COV-01 checks against the running API.
    /// </summary>
    private static readonly Dictionary<string, string> Manifest = new(StringComparer.Ordinal)
    {
        ["GET /live"] = "INF_03 (HealthTests)",
        ["GET /health"] = "INF_01, INF_02 (HealthTests)",
        ["GET /ready"] = "INF_03 (HealthTests)",
        ["GET /api/_debug/throw"] = "INF_04 (ErrorResponseTests)",
        ["GET /api/system/time"] = "INF_07 (SystemSliceTests)",
        ["POST /api/system/ping"] = "INF_05, INF_07 (SystemSliceTests)",
        ["POST /api/auth/register"] = "AUTH_01..04 (AuthTests)",
        ["POST /api/auth/login"] = "AUTH_06, AUTH_07 (AuthTests)",
        ["GET /api/auth/me"] = "AUTH_08 (AuthTests)",
        ["GET /api/reading/passages"] = "READ_01..03",
        ["GET /api/reading/passages/{passageId}"] = "READ_04..07",
        ["GET /api/lessons"] = "LES_01..03",
        ["GET /api/lessons/{lessonId}"] = "LES_04..06",
        ["POST /api/attempts"] = "EX_01..04, EX_11, EX_23",
        ["GET /api/attempts/{attemptId}"] = "EX_05..07",
        ["PUT /api/attempts/{attemptId}/answers/{questionId}"] = "EX_07..10, EX_24, EX_25..26",
        ["POST /api/attempts/{attemptId}/submit"] = "EX_12..20, EX_22, EX_27",
        ["GET /api/attempts/{attemptId}/result"] = "EX_13, EX_16, EX_21",
    };

    /// <summary>
    /// Every endpoint that requires a signed-in user. Without a session each must answer 401
    /// before it looks at the route, the body or any header.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/reading/passages")]
    [InlineData("GET", "/api/reading/passages/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/lessons")]
    [InlineData("GET", "/api/lessons/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/attempts")]
    [InlineData("GET", "/api/attempts/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/attempts/00000000-0000-0000-0000-000000000001/answers/00000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/api/attempts/00000000-0000-0000-0000-000000000001/submit")]
    [InlineData("GET", "/api/attempts/00000000-0000-0000-0000-000000000001/result")]
    public async Task COV_02_Protected_Endpoints_Return_401_Without_A_Session(string method, string url)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = ContentTestData.CreateClient(factory);

        using HttpRequestMessage request = new(new HttpMethod(method), url);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task COV_01_Every_Mapped_Endpoint_Is_Listed_In_The_Coverage_Manifest()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient warmUp = ContentTestData.CreateClient(factory);

        SortedSet<string> mapped = new(StringComparer.Ordinal);

        foreach (RouteEndpoint endpoint in factory.Services.GetRequiredService<EndpointDataSource>()
                     .Endpoints.OfType<RouteEndpoint>())
        {
            string path = Normalize(endpoint.RoutePattern.RawText ?? string.Empty);

            if (!IsApplicationRoute(path))
            {
                continue;
            }

            IReadOnlyList<string> methods =
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];

            foreach (string method in methods)
            {
                mapped.Add($"{method} {path}");
            }
        }

        SortedSet<string> listed = new(Manifest.Keys, StringComparer.Ordinal);

        mapped.Except(listed).ShouldBeEmpty("These endpoints have no entry in the coverage manifest.");
        listed.Except(mapped).ShouldBeEmpty("The manifest lists endpoints that are no longer mapped.");
    }

    private static bool IsApplicationRoute(string path) =>
        path.StartsWith("/api/", StringComparison.Ordinal)
        || path is "/live" or "/health" or "/ready";

    /// <summary>
    /// Drops route constraints ({id:guid} becomes {id}) and a trailing slash, so the manifest is
    /// written the way people read routes.
    /// </summary>
    private static string Normalize(string pattern)
    {
        string withoutConstraints = ConstraintPattern().Replace(pattern, "{$1}");
        string trimmed = withoutConstraints.Length > 1 ? withoutConstraints.TrimEnd('/') : withoutConstraints;

        return trimmed;
    }

    [GeneratedRegex(@"\{(\w+):[^}]+\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConstraintPattern();
}
