using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Dashboard.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace BandUp.IntegrationTests;

public sealed class AdminContentRequestTests
{
    [Theory]
    [InlineData("/api/admin/passages")]
    [InlineData("/api/admin/lessons")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/dashboard")]
    [InlineData("/api/admin/writing-prompts")]
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
    [InlineData("/api/admin/users/01998a61-7ca4-7c0b-8051-9ee86f71afab/role", "role")]
    public async Task Invalid_Admin_Input_Is_Rejected_Before_Opening_A_Database_Transaction(string path, string field)
    {
        Guid adminId = Guid.CreateVersion7();
        await using AuthorizationApiFactory factory = new(adminId);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(
            adminId, "Admin", TimeProvider.System.GetUtcNow());
        HttpMethod method = field == "role" ? HttpMethod.Patch : HttpMethod.Post;
        using HttpRequestMessage request = new(method, path)
        {
            Content = JsonContent.Create(new { title = "", body = "" }),
        };
        request.Headers.Add("Cookie", $"bu_at={token.Value}");
        request.Headers.Add("X-Requested-With", "bandup");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement errors = body.GetProperty("errors");
        errors.TryGetProperty(field, out _).ShouldBeTrue();
        if (method == HttpMethod.Post)
        {
            errors.TryGetProperty("title", out _).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("PATCH", "/api/admin/users/01998a61-7ca4-7c0b-8051-9ee86f71afab/role")]
    [InlineData("DELETE", "/api/admin/users/01998a61-7ca4-7c0b-8051-9ee86f71afab")]
    [InlineData("GET", "/api/admin/users/01998a61-7ca4-7c0b-8051-9ee86f71afab/credits")]
    [InlineData("GET", "/api/admin/users/01998a61-7ca4-7c0b-8051-9ee86f71afab/credits/history")]
    [InlineData("POST", "/api/admin/users/01998a61-7ca4-7c0b-8051-9ee86f71afab/credits/adjustments")]
    public async Task An_Ordinary_Admin_Cannot_Use_Any_User_Management_Endpoint_Even_With_A_Super_Admin_Claim(
        string method, string path)
    {
        Guid actorId = Guid.CreateVersion7();
        await using AuthorizationApiFactory factory = new(actorId, superAdmin: false);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, "SuperAdmin", TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        request.Headers.Add("Cookie", $"bu_at={token.Value}");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task A_Deactivated_Accounts_Existing_Token_Is_Rejected_On_Learner_Endpoints(string role)
    {
        Guid actorId = Guid.CreateVersion7();
        await using AuthorizationApiFactory factory = new(actorId, active: false);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, role, TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/reading/passages");
        request.Headers.Add("Cookie", $"bu_at={token.Value}");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_Admin_Roles_Can_Manage_Content_And_Reach_Its_Validation(bool superAdmin)
    {
        Guid actorId = Guid.CreateVersion7();
        await using AuthorizationApiFactory factory = new(actorId, superAdmin);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, superAdmin ? "SuperAdmin" : "Admin", TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/admin/passages")
        {
            Content = JsonContent.Create(new { title = "", body = "" }),
        };
        request.Headers.Add("Cookie", $"bu_at={token.Value}");
        request.Headers.Add("X-Requested-With", "bandup");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Owner")]
    [InlineData("999")]
    public async Task Creating_A_User_Rejects_Missing_And_Unsupported_Roles(string? role)
    {
        Guid actorId = Guid.CreateVersion7();
        await using AuthorizationApiFactory factory = new(actorId);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, "SuperAdmin", TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new { email = "new@example.test", password = "StrongPassword1", displayName = "New", role }),
        };
        request.Headers.Add("Cookie", $"bu_at={token.Value}");
        request.Headers.Add("X-Requested-With", "bandup");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(false, 24, 6, 18, 128)]
    [InlineData(true, 24, 6, 18, 128)]
    [InlineData(false, 0, 0, 0, 0)]
    public async Task Both_Admin_Roles_Can_Read_All_Four_Dashboard_Counters(
        bool superAdmin, int published, int drafts, int lessons, int users)
    {
        Guid actorId = Guid.CreateVersion7();
        AdminDashboardDto expected = new(published, drafts, lessons, users);
        await using AuthorizationApiFactory factory = new(actorId, superAdmin, dashboard: expected);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, superAdmin ? "SuperAdmin" : "Admin", TimeProvider.System.GetUtcNow());
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/admin/dashboard");
        request.Headers.Add("Cookie", $"bu_at={token.Value}");

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement body = document.RootElement;
        body.GetProperty("publishedPassages").GetInt32().ShouldBe(expected.PublishedPassages);
        body.GetProperty("draftPassages").GetInt32().ShouldBe(expected.DraftPassages);
        body.GetProperty("totalLessons").GetInt32().ShouldBe(expected.TotalLessons);
        body.GetProperty("totalUsers").GetInt32().ShouldBe(users);
        body.EnumerateObject().Count().ShouldBe(4);
    }

    [Theory]
    [InlineData("passages", "status=Unknown")]
    [InlineData("passages", "status=999")]
    [InlineData("passages", "search=")]
    [InlineData("lessons", "status=Unknown")]
    [InlineData("lessons", "status=999")]
    [InlineData("lessons", "search=")]
    [InlineData("lessons", "questionType=Unknown")]
    [InlineData("lessons", "questionType=999")]
    [InlineData("writing-prompts", "status=Unknown")]
    [InlineData("writing-prompts", "status=999")]
    [InlineData("writing-prompts", "search=")]
    public async Task Invalid_Content_Filters_Are_Rejected_Before_Reading_The_Database(string resource, string filter)
    {
        Guid actorId = Guid.CreateVersion7();
        await using AuthorizationApiFactory factory = new(actorId);
        using HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, "Admin", TimeProvider.System.GetUtcNow());
        if (filter == "search=")
        {
            filter += new string('a', 321);
        }

        using HttpRequestMessage request = new(HttpMethod.Get, $"/api/admin/{resource}?{filter}");
        request.Headers.Add("Cookie", $"bu_at={token.Value}");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archiving_A_Lesson_Rejects_Anonymous_And_Ordinary_Users()
    {
        await using AuthorizationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string path = $"/api/admin/lessons/{Guid.NewGuid()}/archive";
        using HttpResponseMessage anonymous = await client.PostAsJsonAsync(path, new { });
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(Guid.CreateVersion7(), "User", TimeProvider.System.GetUtcNow()).Value;
        using HttpRequestMessage request = new(HttpMethod.Post, path) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Cookie", $"bu_at={token}");
        request.Headers.Add("X-Requested-With", "bandup");
        using HttpResponseMessage ordinaryUser = await client.SendAsync(request);
        ordinaryUser.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("PUT", "/order")]
    public async Task Lesson_Example_Collection_Actions_Reject_Anonymous_And_Ordinary_Users(string method, string suffix)
    {
        await using AuthorizationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string path = $"/api/admin/lessons/{Guid.NewGuid()}/examples{suffix}";
        using HttpRequestMessage anonymousRequest = new(new HttpMethod(method), path);
        using HttpResponseMessage anonymous = await client.SendAsync(anonymousRequest);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(Guid.CreateVersion7(), "User", TimeProvider.System.GetUtcNow()).Value;
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        request.Headers.Add("Cookie", $"bu_at={token}");
        request.Headers.Add("X-Requested-With", "bandup");
        using HttpResponseMessage denied = await client.SendAsync(request);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "")]
    [InlineData("PUT", "/01998a61-7ca4-7c0b-8051-9ee86f71afab")]
    [InlineData("DELETE", "/01998a61-7ca4-7c0b-8051-9ee86f71afab")]
    [InlineData("POST", "/01998a61-7ca4-7c0b-8051-9ee86f71afab/publish")]
    [InlineData("POST", "/01998a61-7ca4-7c0b-8051-9ee86f71afab/archive")]
    [InlineData("POST", "/01998a61-7ca4-7c0b-8051-9ee86f71afab/duplicate")]
    [InlineData("GET", "/01998a61-7ca4-7c0b-8051-9ee86f71afab")]
    public async Task Writing_Prompt_Actions_Require_Admin_Permissions(string method, string suffix)
    {
        await using AuthorizationApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string path = "/api/admin/writing-prompts" + suffix;
        using HttpRequestMessage anonymousRequest = new(new HttpMethod(method), path);
        using HttpResponseMessage anonymous = await client.SendAsync(anonymousRequest);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(Guid.CreateVersion7(), "User", TimeProvider.System.GetUtcNow()).Value;
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        request.Headers.Add("Cookie", $"bu_at={token}");
        request.Headers.Add("X-Requested-With", "bandup");
        using HttpResponseMessage denied = await client.SendAsync(request);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("title", 100, 350)]
    [InlineData("taskText", 100, 350)]
    [InlineData("essayType", 100, 350)]
    [InlineData("minWords", 0, 350)]
    [InlineData("maxWords", 100, 99)]
    public async Task Writing_Prompt_Validation_Runs_Before_A_Database_Transaction(string field, int min, int max)
    {
        Guid actor = Guid.NewGuid();
        await using AuthorizationApiFactory factory = new(actor);
        using HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actor, "Admin", TimeProvider.System.GetUtcNow()).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            string path = "/api/admin/writing-prompts" + (method == HttpMethod.Put ? $"/{Guid.NewGuid()}" : "");
            using HttpRequestMessage request = new(method, path)
            {
                Content = JsonContent.Create(new
                {
                    title = field == "title" ? " " : "Title", taskText = field == "taskText" ? "" : "Text",
                    essayType = field == "essayType" ? "" : "Opinion essay", minWords = min, maxWords = max,
                }),
            };
            using HttpResponseMessage response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
        }
    }

    private sealed class AuthorizationApiFactory(
        Guid? adminId = null, bool superAdmin = true, bool active = true, AdminDashboardDto? dashboard = null)
        : WebApplicationFactory<Program>
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
                    ["PasswordReset:CodeProtectionKey"] =
                        Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()),
                    ["PasswordReset:RateLimitProvider"] = "InMemory",
                    ["Email:Provider"] = "InMemory",
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                IUserRepository users = Substitute.For<IUserRepository>();
                users.IsActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(active);
                users.IsActiveAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(call => call.ArgAt<Guid>(0) == adminId);
                users.IsActiveSuperAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(call => superAdmin && call.ArgAt<Guid>(0) == adminId);
                services.RemoveAll<IUserRepository>();
                services.AddSingleton(users);
                IAdminDashboardQueries dashboardQueries = Substitute.For<IAdminDashboardQueries>();
                dashboardQueries.GetAsync(Arg.Any<CancellationToken>())
                    .Returns(dashboard ?? new AdminDashboardDto(0, 0, 0, 0));
                services.RemoveAll<IAdminDashboardQueries>();
                services.AddSingleton(dashboardQueries);
            });
        }
    }
}
