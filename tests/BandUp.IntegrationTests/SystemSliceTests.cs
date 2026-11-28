using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Infrastructure.Logging;
using BandUp.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog.Events;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class SystemSliceTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task INF_07_Send_Ping_Returns_202_And_The_Worker_Receives_The_Message()
    {
        TestLogSink workerLogs = new();
        TestLogSink apiLogs = new();

        string database = await environment.CreateIsolatedMigratedDatabaseAsync(CancellationToken.None);
        Dictionary<string, string?> settings = environment.SettingsFor(database);

        using IHost worker = WorkerHost.Build(
            [],
            workerLogs,
            builder => builder.Configuration.AddInMemoryCollection(settings));

        await worker.StartAsync(CancellationToken.None);

        try
        {
            await using BandUpApiFactory factory = new(environment, logSink: apiLogs,
                overrides: new() { ["ConnectionStrings:Default"] = database });
            using HttpClient client = factory.CreateClient();

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/system/ping",
                new { message = "hello from the integration test" });

            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

            JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Guid pingId = body.GetProperty("pingId").GetGuid();
            pingId.ShouldNotBe(Guid.Empty);

            // The worker logs the id it handled, which is the proof that the message the API
            // published is the message that was consumed.
            LogEvent? handled = await workerLogs.WaitForAsync(
                entry => entry.MessageTemplate.Text.Contains("Received ping", StringComparison.Ordinal) &&
                         entry.Properties.TryGetValue("PingId", out LogEventPropertyValue? value) &&
                         value.ToString().Contains(pingId.ToString(), StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));

            handled.ShouldNotBeNull($"the worker never logged handling ping {pingId}");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task INF_07_Get_Server_Time_Returns_The_Clock_Environment_And_Version()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/system/time");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        body.GetProperty("environment").GetString().ShouldBe("Development");
        body.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();

        DateTimeOffset utcNow = body.GetProperty("utcNow").GetDateTimeOffset();
        utcNow.Offset.ShouldBe(TimeSpan.Zero);
        (DateTimeOffset.UtcNow - utcNow).Duration().ShouldBeLessThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task INF_07_The_Slice_Endpoints_Are_Absent_Without_The_Diagnostics_Flag()
    {
        await using BandUpApiFactory factory = new(
            environment,
            overrides: new Dictionary<string, string?> { ["Features:Diagnostics"] = "false" });

        using HttpClient client = factory.CreateClient();

        (await client.GetAsync("/api/system/time")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/system/ping", new { message = "hi" })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }
}
