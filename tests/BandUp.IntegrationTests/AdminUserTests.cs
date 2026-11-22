using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminUserTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task Users_Are_Paginated_And_Role_Changes_Take_Effect_With_Existing_Tokens()
    {
        User admin = CreateUser(Role.SuperAdmin);
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
        using HttpResponseMessage promote = await ChangeRoleAsync(adminClient, target.Id, Role.SuperAdmin);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent, await promote.Content.ReadAsStringAsync());
        using HttpResponseMessage access = await targetClient.GetAsync("/api/admin/users");
        access.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpClient targetAdminToken = CreateClient(factory, target, Role.SuperAdmin);
        using HttpResponseMessage teacher = await ChangeRoleAsync(adminClient, target.Id, Role.Admin);
        teacher.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using HttpResponseMessage contentAccess = await targetAdminToken.GetAsync("/api/admin/passages");
        contentAccess.StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpResponseMessage usersDenied = await targetAdminToken.GetAsync("/api/admin/users");
        usersDenied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using HttpResponseMessage demote = await ChangeRoleAsync(adminClient, target.Id, Role.User);
        demote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using HttpResponseMessage stale = await targetAdminToken.GetAsync("/api/admin/passages");
        stale.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrent_Super_Admins_Cannot_Demote_Each_Other()
    {
        User first = CreateUser(Role.SuperAdmin);
        User second = CreateUser(Role.SuperAdmin);
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
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task A_Super_Admin_Can_Create_Filter_And_Deactivate_A_User_With_The_Requested_Role(Role role)
    {
        User actor = CreateUser(Role.SuperAdmin);
        await SaveUsersAsync(actor);
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = CreateClient(factory, actor);
        string marker = Guid.NewGuid().ToString("N");
        string email = $"{marker}@example.test";
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email,
            displayName = $"Learner {marker}",
            password = "StrongPassword1",
            role = role.ToString(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        JsonElement page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/users?role={role}&search={marker.ToUpperInvariant()}&pageSize=1");
        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        JsonElement row = page.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("id").GetGuid().ShouldBe(id);
        row.GetProperty("role").GetString().ShouldBe(role.ToString());
        row.GetProperty("createdAt").GetDateTimeOffset().ShouldBeGreaterThan(actor.CreatedAt);
        row.GetProperty("creditBalance").GetInt32().ShouldBe(3);
        row.TryGetProperty("passwordHash", out _).ShouldBeFalse();

        JsonElement wrongRole = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/users?role={(role == Role.User ? Role.Admin : Role.User)}&search={marker}");
        wrongRole.GetProperty("totalCount").GetInt32().ShouldBe(0);
        JsonElement emptyPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/users?search={marker}&page=2147483647&pageSize=100");
        emptyPage.GetProperty("totalCount").GetInt32().ShouldBe(1);
        emptyPage.GetProperty("items").GetArrayLength().ShouldBe(0);

        using HttpResponseMessage duplicate = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = email.ToUpperInvariant(), displayName = "Duplicate", password = "StrongPassword1", role = "User",
        });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        User stored = await db.Users.SingleAsync(user => user.Id == id);
        factory.Services.GetRequiredService<IPasswordHasher>().Verify("StrongPassword1", stored.PasswordHash).ShouldBeTrue();
        using HttpClient targetClient = CreateClient(factory, stored);
        using HttpResponseMessage deleted = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{id}")
        {
            Content = JsonContent.Create(new { }),
        });
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        JsonElement active = await client.GetFromJsonAsync<JsonElement>($"/api/admin/users?search={marker}");
        active.GetProperty("totalCount").GetInt32().ShouldBe(0);
        JsonElement inactive = await client.GetFromJsonAsync<JsonElement>($"/api/admin/users?search={marker}&isActive=false");
        inactive.GetProperty("totalCount").GetInt32().ShouldBe(1);
        inactive.GetProperty("items")[0].GetProperty("isActive").GetBoolean().ShouldBeFalse();
        using HttpResponseMessage denied = await targetClient.GetAsync("/api/reading/passages");
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await db.Entry(stored).ReloadAsync();
        stored.IsActive.ShouldBeFalse();
        (await db.CreditAccounts.SingleAsync(account => account.UserId == id)).Balance.ShouldBe(3);
        using HttpResponseMessage selfDelete = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{actor.Id}") { Content = JsonContent.Create(new { }) });
        selfDelete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
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
