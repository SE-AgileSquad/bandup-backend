using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Infrastructure.Logging;
using Serilog.Events;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class ErrorResponseTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task INF_04_Unhandled_Exception_Returns_500_With_The_Request_Id_And_No_Stack_Trace()
    {
        TestLogSink logs = new();
        await using BandUpApiFactory factory = new(environment, logSink: logs);
        using HttpClient client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Request-Id", "trace-error-0001");

        HttpResponseMessage response = await client.GetAsync("/api/_debug/throw");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        JsonElement body = await ReadJsonAsync(response);

        body.GetProperty("code").GetString().ShouldBe("INTERNAL_ERROR");
        body.GetProperty("traceId").GetString().ShouldBe("trace-error-0001");
        body.GetProperty("status").GetInt32().ShouldBe(500);

        string raw = body.GetRawText();
        raw.ShouldNotContain("InvalidOperationException");
        raw.ShouldNotContain("at BandUp.");
        raw.ShouldNotContain("Diagnostics endpoint: deliberate failure.");

        // The exception text goes to the log instead, with the same request id.
        LogEvent? failure = await logs.WaitForAsync(entry =>
            entry.Level == LogEventLevel.Error && entry.Exception is not null);

        failure.ShouldNotBeNull();
        failure.Properties[BandUpLogProperties.RequestId].ToString().ShouldContain("trace-error-0001");
    }

    [Fact]
    public async Task INF_05_Invalid_Ping_Returns_400_With_A_Validation_Error_Map()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/system/ping",
            new { message = string.Empty });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        JsonElement body = await ReadJsonAsync(response);

        body.GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
        body.GetProperty("type").GetString().ShouldBe("https://bandup.app/errors/validation-error");

        JsonElement errors = body.GetProperty("errors");
        errors.TryGetProperty("Message", out JsonElement messages).ShouldBeTrue();
        messages.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task INF_05_A_Message_Longer_Than_The_Limit_Is_Rejected()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/system/ping",
            new { message = new string('a', 101) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}