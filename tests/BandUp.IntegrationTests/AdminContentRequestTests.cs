using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace BandUp.IntegrationTests;

public sealed class AdminContentRequestTests
{
    [Theory]
    [InlineData("/api/admin/passages")]
    [InlineData("/api/admin/lessons")]
    public async Task Admin_Lists_Reject_Anonymous_And_Ordinary_Users(string path)
    {
        await using AuthorizationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage anonymous = await client.GetAsync(path);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        AccessToken token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(
            Guid.CreateVersion7(), "User", TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"bu_at={token.Value}");
        using HttpResponseMessage ordinaryUser = await client.SendAsync(request);
        ordinaryUser.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/admin/passages", "sourceNote")]
    [InlineData("/api/admin/lessons", "questionType")]
    public async Task Invalid_Admin_Input_Is_Rejected_Before_Opening_A_Database_Transaction(
        string path, string invalidField)
    {
        await using AuthorizationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(
            Guid.CreateVersion7(), "Admin", TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { title = "", body = "" }),
        };
        request.Headers.Add("Cookie", $"bu_at={token.Value}");
        request.Headers.Add("X-Requested-With", "bandup");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement errors = body.GetProperty("errors");
        errors.TryGetProperty("title", out _).ShouldBeTrue();
        errors.TryGetProperty(invalidField, out _).ShouldBeTrue();
    }

    private sealed class AuthorizationApiFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth:SigningKey"] = "test-only-admin-content-signing-key-0123456789",
                    ["ConnectionStrings:Default"] = "Host=127.0.0.1;Database=unused",
                    ["Cors:AllowedOrigin"] = "https://frontend.example.test",
                    ["Cache:Provider"] = "InMemory",
                    ["MessagePublisher:Provider"] = "InMemory",
                    ["Sentry:Dsn"] = string.Empty,
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
                services.AddDataProtection().UseEphemeralDataProtectionProvider());
        }
    }
}
