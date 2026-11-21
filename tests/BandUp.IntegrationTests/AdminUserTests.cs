using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminUserTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task Users_Are_Paginated_And_Role_Changes_Take_Effect_With_Existing_Tokens()
    {
        User admin = CreateUser(Role.Admin);
        User target = CreateUser(Role.User);
        await SaveUsersAsync(admin, target);
        await using BandUpApiFactory factory = new(environment);
        using HttpClient adminClient = CreateClient(factory, admin);
        using HttpClient targetClient = CreateClient(factory, target);

        using HttpResponseMessage ordinary = await ChangeRoleAsync(targetClient, admin.Id, Role.User);
        ordinary.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement page = await adminClient.GetFromJsonAsync<JsonElement>("/api/admin/users?pageSize=1");
        page.GetProperty("pageSize").GetInt32().ShouldBe(1);
        page.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        JsonElement item = page.GetProperty("items").EnumerateArray().Single();
        item.TryGetProperty("passwordHash", out _).ShouldBeFalse();

        using HttpResponseMessage missing = await ChangeRoleAsync(adminClient, Guid.CreateVersion7(), Role.Admin);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using HttpResponseMessage promote = await ChangeRoleAsync(adminClient, target.Id, Role.Admin);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent, await promote.Content.ReadAsStringAsync());
        using HttpResponseMessage access = await targetClient.GetAsync("/api/admin/users");
        access.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpClient targetAdminToken = CreateClient(factory, target, Role.Admin);
        using HttpResponseMessage demote = await ChangeRoleAsync(adminClient, target.Id, Role.User);
        demote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using HttpResponseMessage stale = await targetAdminToken.GetAsync("/api/admin/passages");
        stale.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrent_Admins_Cannot_Demote_Each_Other()
    {
        User first = CreateUser(Role.Admin);
        User second = CreateUser(Role.Admin);
        await SaveUsersAsync(first, second);
        await using BandUpApiFactory factory = new(environment);
        using HttpClient firstClient = CreateClient(factory, first);
        using HttpClient secondClient = CreateClient(factory, second);

        using HttpResponseMessage self = await ChangeRoleAsync(firstClient, first.Id, Role.User);
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        Task<HttpResponseMessage> firstChange = ChangeRoleAsync(firstClient, second.Id, Role.User);
        Task<HttpResponseMessage> secondChange = ChangeRoleAsync(secondClient, first.Id, Role.User);
        await Task.WhenAll(firstChange, secondChange);
        using HttpResponseMessage firstResult = await firstChange;
        using HttpResponseMessage secondResult = await secondChange;
        HttpStatusCode[] statuses = [firstResult.StatusCode, secondResult.StatusCode];
        statuses.Order().ShouldBe(new[] { HttpStatusCode.NoContent, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task Inactive_Admin_With_Valid_Token_Cannot_Access_The_Panel()
    {
        User admin = CreateUser(Role.Admin);
        admin.Deactivate();
        await SaveUsersAsync(admin);
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = CreateClient(factory, admin);

        using HttpResponseMessage response = await client.GetAsync("/api/admin/users");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static User CreateUser(Role role)
    {
        User user = User.Create($"{Guid.NewGuid():N}@example.test", "Panel user", "test-hash", DateTimeOffset.UtcNow);
        user.ChangeRole(role);
        return user;
    }

    private async Task SaveUsersAsync(params User[] users)
    {
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
    }

    private static HttpClient CreateClient(BandUpApiFactory factory, User user, Role? tokenRole = null)
    {
        HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(user.Id, (tokenRole ?? user.Role).ToString(), DateTimeOffset.UtcNow);
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token.Value}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient client, Guid userId, Role role) =>
        client.PatchAsJsonAsync($"/api/admin/users/{userId}/role", new { role = role.ToString() });
}
