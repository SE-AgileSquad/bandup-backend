using System.Net;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class HealthTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task INF_01_Health_Reports_All_Dependencies_Healthy()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Healthy");

        JsonElement checks = body.GetProperty("checks");
        checks.GetProperty("postgres").GetString().ShouldBe("Healthy");
        checks.GetProperty("redis").GetString().ShouldBe("Healthy");
        checks.GetProperty("rabbitmq").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task INF_02_Health_Returns_503_Naming_The_Dependency_That_Is_Down()
    {
        await using BandUpApiFactory factory = new(
            environment,
            overrides: new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = $"127.0.0.1:{BandUpTestEnvironment.UnreachablePort}",
            });

        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        body.GetProperty("checks").GetProperty("redis").GetString().ShouldBe("Unhealthy");
    }

    [Fact]
    public async Task INF_03_Live_Is_Healthy_Without_Dependencies_And_Ready_Withholds_Traffic_While_Migrations_Are_Pending()
    {
        await using BandUpApiFactory factory = new(
            environment,
            overrides: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = environment.UnmigratedConnectionString,
            });

        using HttpClient client = factory.CreateClient();

        // /live never touches a dependency, so it is healthy even against an empty database.
        HttpResponseMessage live = await client.GetAsync("/live");

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(live)).GetProperty("status").GetString().ShouldBe("Healthy");

        HttpResponseMessage ready = await client.GetAsync("/ready");

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ReadJsonAsync(ready)).GetProperty("checks").GetProperty("migrations").GetString()
            .ShouldBe("Unhealthy");
    }

    [Fact]
    public async Task INF_03_Ready_Is_Healthy_Once_Migrations_Have_Been_Applied()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("checks").GetProperty("migrations").GetString()
            .ShouldBe("Healthy");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}