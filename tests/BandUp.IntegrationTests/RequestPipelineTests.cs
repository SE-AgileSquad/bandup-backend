using System.Net;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Infrastructure.Logging;
using Serilog.Events;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class RequestPipelineTests(BandUpTestEnvironment environment)
{
    private const string HeaderName = "X-Request-Id";

    [Fact]
    public async Task INF_06_Every_Response_Carries_A_Request_Id()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        foreach (string path in new[] { "/live", "/health", "/api/system/time" })
        {
            HttpResponseMessage response = await client.GetAsync(path);

            response.Headers.TryGetValues(HeaderName, out IEnumerable<string>? values).ShouldBeTrue();
            string id = values!.Single();

            id.Length.ShouldBe(32);
            id.ShouldAllBe(character => Uri.IsHexDigit(character));
            id.ShouldBe(id.ToLowerInvariant());
        }
    }

    [Fact]
    public async Task INF_06_A_Valid_Incoming_Request_Id_Is_Preserved()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "/live");
        request.Headers.Add(HeaderName, "abc12345-valid-id");

        HttpResponseMessage response = await client.SendAsync(request);

        response.Headers.GetValues(HeaderName).Single().ShouldBe("abc12345-valid-id");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces and is long enough")]
    [InlineData("way-too-long-to-be-acceptable-because-it-exceeds-the-sixty-four-character-limit")]
    public async Task INF_06_An_Invalid_Incoming_Request_Id_Is_Replaced(string candidate)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "/live");
        request.Headers.Add(HeaderName, candidate);

        HttpResponseMessage response = await client.SendAsync(request);

        string id = response.Headers.GetValues(HeaderName).Single();

        id.ShouldNotBe(candidate);
        id.Length.ShouldBe(32);
    }

    [Fact]
    public async Task INF_06_The_Request_Id_Appears_As_A_Log_Property()
    {
        TestLogSink logs = new();
        await using BandUpApiFactory factory = new(environment, logSink: logs);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/time");
        request.Headers.Add(HeaderName, "log-correlation-01");

        await client.SendAsync(request);

        LogEvent? requestLine = await logs.WaitForAsync(entry =>
            entry.Properties.ContainsKey(BandUpLogProperties.Route) &&
            entry.Properties.ContainsKey(BandUpLogProperties.StatusCode) &&
            entry.Properties.ContainsKey(BandUpLogProperties.DurationMs));

        requestLine.ShouldNotBeNull();
        requestLine.Properties[BandUpLogProperties.RequestId].ToString().ShouldContain("log-correlation-01");
        requestLine.Properties[BandUpLogProperties.Service].ToString().ShouldContain("api");
    }

    [Fact]
    public async Task INF_12_Cors_Allows_Only_The_Configured_Origin_With_Credentials()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage allowed = Preflight("http://localhost:3000");
        HttpResponseMessage allowedResponse = await client.SendAsync(allowed);

        allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single()
            .ShouldBe("http://localhost:3000");
        allowedResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single()
            .ShouldBe("true");
        allowedResponse.Headers.GetValues("Access-Control-Allow-Methods").Single()
            .ShouldNotContain("*");
        allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single()
            .ShouldNotBe("*");

        using HttpRequestMessage denied = Preflight("http://not-the-frontend.example");
        HttpResponseMessage deniedResponse = await client.SendAsync(denied);

        deniedResponse.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task INF_13_OpenApi_Document_Lists_The_System_Endpoints_With_Their_Responses()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement paths = document.GetProperty("paths");

        JsonElement time = paths.GetProperty("/api/system/time").GetProperty("get");
        time.GetProperty("responses").TryGetProperty("200", out _).ShouldBeTrue();

        JsonElement ping = paths.GetProperty("/api/system/ping").GetProperty("post");
        JsonElement pingResponses = ping.GetProperty("responses");

        pingResponses.TryGetProperty("202", out _).ShouldBeTrue();
        pingResponses.TryGetProperty("400", out JsonElement badRequest).ShouldBeTrue();

        badRequest.GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        HttpRequestMessage request = new(HttpMethod.Options, "/api/system/time");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        return request;
    }
}