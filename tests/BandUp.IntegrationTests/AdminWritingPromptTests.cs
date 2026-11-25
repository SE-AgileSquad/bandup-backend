using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing.Admin;
using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminWritingPromptTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Prompt_Lifecycle_Protects_Published_Content_And_Supports_Independent_Copies(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, role);
        string path = await CreateAsync(client, new { title = "Remote work", taskText = "Discuss remote work." });
        WritingPromptDetails draft = await DetailsAsync(client, path);
        draft.Status.ShouldBe(WritingPromptStatus.Draft);
        draft.TaskNumber.ShouldBe(2);
        draft.MinWords.ShouldBe(100);
        draft.MaxWords.ShouldBe(350);
        draft.EssayType.ShouldBe("Opinion essay");
        draft.IsReadOnly.ShouldBeFalse();
        await ChangeAsync(client, HttpMethod.Post, $"{path}/archive", new { }, HttpStatusCode.Conflict);
        object input = new { title = "Updated remote work", taskText = "Discuss benefits.", essayType = "Discussion", minWords = 150, maxWords = 400 };
        await ChangeAsync(client, HttpMethod.Put, path, input);
        WritingPromptDetails edited = await DetailsAsync(client, path);
        edited.Title.ShouldBe("Updated remote work");
        edited.MinWords.ShouldBe(150);
        await ChangeAsync(client, HttpMethod.Put, path, input);
        (await DetailsAsync(client, path)).UpdatedAt.ShouldBe(edited.UpdatedAt);
        await ChangeAsync(client, HttpMethod.Post, $"{path}/publish", new { });
        WritingPromptDetails published = await DetailsAsync(client, path);
        published.IsReadOnly.ShouldBeTrue();
        published.PublishedAt.ShouldNotBeNull();
        await ChangeAsync(client, HttpMethod.Post, $"{path}/publish", new { });
        (await DetailsAsync(client, path)).UpdatedAt.ShouldBe(published.UpdatedAt);
        await ChangeAsync(client, HttpMethod.Put, path, input, HttpStatusCode.Conflict);
        string copyPath = await CreateAsync(client, new { }, $"{path}/duplicate");
        WritingPromptDetails copy = await DetailsAsync(client, copyPath);
        copy.Id.ShouldNotBe(published.Id);
        copy.TaskText.ShouldBe(published.TaskText);
        copy.Status.ShouldBe(WritingPromptStatus.Draft);
        copy.PublishedAt.ShouldBeNull();
        await ChangeAsync(client, HttpMethod.Delete, copyPath, new { });
        using (HttpResponseMessage missingCopy = await client.GetAsync(copyPath))
        {
            missingCopy.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await ChangeAsync(client, HttpMethod.Delete, path, new { });
        WritingPromptDetails archived = await DetailsAsync(client, path);
        archived.Status.ShouldBe(WritingPromptStatus.Archived);
        archived.TaskText.ShouldBe(published.TaskText);
        await ChangeAsync(client, HttpMethod.Post, $"{path}/archive", new { });
        await ChangeAsync(client, HttpMethod.Delete, path, new { });
        (await DetailsAsync(client, path)).UpdatedAt.ShouldBe(archived.UpdatedAt);
        await ChangeAsync(client, HttpMethod.Put, path, input, HttpStatusCode.Conflict);
        await ChangeAsync(client, HttpMethod.Post, $"{path}/publish", new { }, HttpStatusCode.Conflict);
        foreach (string suffix in new[] { "", "/publish", "/archive", "/duplicate" })
        {
            string missing = $"/api/admin/writing-prompts/{Guid.NewGuid()}{suffix}";
            if (suffix.Length == 0)
            {
                await ChangeAsync(client, HttpMethod.Put, missing, input, HttpStatusCode.NotFound);
                await ChangeAsync(client, HttpMethod.Delete, missing, new { }, HttpStatusCode.NotFound);
            }
            else
            {
                await ChangeAsync(client, HttpMethod.Post, missing, new { }, HttpStatusCode.NotFound);
            }
        }
    }

    [Fact]
    public async Task List_Filters_Title_And_Status_Before_Paging_Without_Returning_Task_Text()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.Admin);
        string marker = Guid.NewGuid().ToString("N");
        string draft = await CreateAsync(client, new { title = $"{marker} Draft", taskText = "Text" });
        string published = await CreateAsync(client, new { title = $"{marker} Published", taskText = "Text" });
        string archived = await CreateAsync(client, new { title = $"{marker} Archived", taskText = "Text" });
        await CreateAsync(client, new { title = "Unrelated", taskText = marker });
        await ChangeAsync(client, HttpMethod.Post, $"{published}/publish", new { });
        await ChangeAsync(client, HttpMethod.Post, $"{archived}/publish", new { });
        await ChangeAsync(client, HttpMethod.Post, $"{archived}/archive", new { });
        string filter = $"/api/admin/writing-prompts?search=%20{marker.ToUpperInvariant()}%20";
        PagedResult<WritingPromptSummary> first = await PageAsync(client, $"{filter}&pageSize=2");
        first.TotalCount.ShouldBe(3);
        first.Items.Count.ShouldBe(2);
        first.Items[0].Status.ShouldBe(WritingPromptStatus.Archived);
        PagedResult<WritingPromptSummary> second = await PageAsync(client, $"{filter}&pageSize=2&page=2");
        second.Items.Single().Id.ShouldBe(Guid.Parse(draft.Split('/')[^1]));
        foreach (WritingPromptStatus status in Enum.GetValues<WritingPromptStatus>())
        {
            PagedResult<WritingPromptSummary> page = await PageAsync(client, $"{filter}&status={status}");
            page.TotalCount.ShouldBe(1);
            page.Items.Single().Status.ShouldBe(status);
        }

        (await PageAsync(client, $"{filter}&page=2147483647&pageSize=100")).Items.ShouldBeEmpty();
        PagedResult<WritingPromptSummary> missing = await PageAsync(client, $"/api/admin/writing-prompts?search=absent-{marker}");
        missing.TotalCount.ShouldBe(0);
        JsonElement json = await client.GetFromJsonAsync<JsonElement>($"{filter}&pageSize=1");
        json.GetProperty("items")[0].TryGetProperty("taskText", out _).ShouldBeFalse();
    }

    private static async Task<PagedResult<WritingPromptSummary>> PageAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<PagedResult<WritingPromptSummary>>(path, JsonSerialization.Options))!;

    private static async Task<WritingPromptDetails> DetailsAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<WritingPromptDetails>(path, JsonSerialization.Options))!;

    private static async Task<string> CreateAsync(HttpClient client, object input, string path = "/api/admin/writing-prompts")
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, input);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }

    private static async Task ChangeAsync(HttpClient client, HttpMethod method, string path, object body,
        HttpStatusCode status = HttpStatusCode.NoContent)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(body) };
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> ClientAsync(BandUpApiFactory factory, Role role)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"{Guid.NewGuid():N}@example.test", "Editor", "test-hash", now);
        user.ChangeRole(role);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(user.Id, role.ToString(), now).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }
}
