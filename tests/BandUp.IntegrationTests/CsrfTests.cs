using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// The CSRF guard applies to requests that carry a session cookie and are not already covered
/// by the safe-method list. Unauthenticated posts must keep working, because there is no
/// ambient credential for a foreign site to ride on.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class CsrfTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task A_Post_Without_A_Session_Is_Not_Guarded()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/system/ping", new { message = "hi" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_Post_With_A_Session_But_Without_The_Header_Is_Rejected()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = Post("/api/system/ping", new { message = "hi" });
        request.Headers.Add("Cookie", "bu_at=anything");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
        body.GetProperty("errors").TryGetProperty("X-Requested-With", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_Post_With_A_Session_And_The_Header_Is_Allowed()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = Post("/api/system/ping", new { message = "hi" });
        request.Headers.Add("Cookie", "bu_at=anything");
        request.Headers.Add("X-Requested-With", "bandup");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_Get_With_A_Session_Is_Not_Guarded()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/time");
        request.Headers.Add("Cookie", "bu_at=anything");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Post(string path, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        return request;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}