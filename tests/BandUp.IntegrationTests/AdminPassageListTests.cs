using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Content.Admin;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminPassageListTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Passage_Cards_Are_Filtered_Before_Counting_And_Paging_And_Include_Question_Counts(Role role)
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = await CreateClientAsync(factory, role);
        string marker = Guid.NewGuid().ToString("N");
        Guid actorId = await ActorIdAsync(client);
        Passage draft = Passage.CreateDraft($"{marker} A draft", "Body", "Source", "Original", actorId);
        for (int index = 0; index < 3; index++)
        {
            draft.AddQuestion(QuestionType.SentenceCompletion, $"Question {index}", index);
        }

        Passage published = PublishedPassage($"{marker} B published", actorId, clock.GetUtcNow());
        Passage archived = PublishedPassage($"{marker} C archived", actorId, clock.GetUtcNow());
        archived.Archive();
        Passage bodyOnly = Passage.CreateDraft("Other title", marker, "Source", "Original", actorId);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            BandUpDbContext db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            db.Passages.AddRange(draft, published, archived, bodyOnly);
            await db.SaveChangesAsync();
            // An older record can have an unknown update time after applying the migration.
            await db.Passages.Where(passage => passage.Id == archived.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(passage => passage.UpdatedAt, (DateTimeOffset?)null));
        }

        string path = $"/api/admin/passages?search=%20{marker.ToUpperInvariant()}%20";
        PagedResult<PassageSummary> first = await ReadPageAsync(client, $"{path}&pageSize=2");
        first.TotalCount.ShouldBe(3);
        first.Page.ShouldBe(1);
        first.PageSize.ShouldBe(2);
        first.Items.Select(item => item.Id).ShouldBe(new[] { draft.Id, published.Id });
        first.Items[0].QuestionCount.ShouldBe(3);
        first.Items[1].QuestionCount.ShouldBe(1);
        first.Items[0].UpdatedAt.ShouldBe(DatabaseTime(clock));

        PagedResult<PassageSummary> second = await ReadPageAsync(client, $"{path}&page=2&pageSize=2");
        second.TotalCount.ShouldBe(3);
        second.Items.Single().Id.ShouldBe(archived.Id);
        second.Items[0].UpdatedAt.ShouldBeNull();

        foreach (PassageStatus status in Enum.GetValues<PassageStatus>())
        {
            PagedResult<PassageSummary> filtered = await ReadPageAsync(client, $"{path}&status={status}");
            filtered.TotalCount.ShouldBe(1);
            filtered.Items.Single().Status.ShouldBe(status);
        }

        PagedResult<PassageSummary> pastEnd = await ReadPageAsync(client, $"{path}&page=2147483647&pageSize=100");
        pastEnd.TotalCount.ShouldBe(3);
        pastEnd.Items.ShouldBeEmpty();
        PagedResult<PassageSummary> missing = await ReadPageAsync(client,
            $"/api/admin/passages?search=missing-{marker}&status=Draft");
        missing.TotalCount.ShouldBe(0);
        missing.Items.ShouldBeEmpty();

        JsonElement details = await client.GetFromJsonAsync<JsonElement>($"/api/admin/passages/{draft.Id}");
        details.GetProperty("updatedAt").GetDateTimeOffset().ShouldBe(first.Items[0].UpdatedAt!.Value);
    }

    [Fact]
    public async Task Passage_Update_Time_Tracks_Content_Changes_And_Preserves_No_Ops_And_Failed_Commands()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        object input = new { title = "Original title", body = "Original body", sourceNote = "Source", usageBasis = "Original" };
        string passage = await CreateAsync(client, "/api/admin/passages", input);
        (await UpdatedAtAsync(client, passage)).ShouldBe(DatabaseTime(clock));

        clock.Advance(TimeSpan.FromMinutes(1));
        await ChangeAsync(client, HttpMethod.Put, passage,
            new { title = "Updated title", body = "Original body", sourceNote = "Source", usageBasis = "Original" });
        DateTimeOffset editedAt = DatabaseTime(clock);
        (await UpdatedAtAsync(client, passage)).ShouldBe(editedAt);

        clock.Advance(TimeSpan.FromMinutes(1));
        string question = await CreateAsync(client, $"{passage}/questions",
            new { type = "SentenceCompletion", prompt = "A question", sortOrder = 1 });
        (await UpdatedAtAsync(client, passage)).ShouldBe(DatabaseTime(clock));

        (HttpMethod Method, string Path, object Body)[] changes =
        [
            (HttpMethod.Put, question, new { prompt = "Updated question", sortOrder = 2 }),
            (HttpMethod.Put, $"{question}/answer-key", new { correctAnswer = "Answer" }),
            (HttpMethod.Put, $"{question}/explanation", new { explanation = "Explanation", evidenceSentence = "Original body" }),
            (HttpMethod.Post, $"{question}/answer-key/verify", new { }),
        ];
        foreach (var change in changes)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await ChangeAsync(client, change.Method, change.Path, change.Body);
            (await UpdatedAtAsync(client, passage)).ShouldBe(DatabaseTime(clock));
        }

        DateTimeOffset verifiedAt = (await UpdatedAtAsync(client, passage))!.Value;
        clock.Advance(TimeSpan.FromMinutes(1));
        await ChangeAsync(client, HttpMethod.Post, $"{question}/answer-key/verify", new { });
        (await UpdatedAtAsync(client, passage)).ShouldBe(verifiedAt);

        clock.Advance(TimeSpan.FromMinutes(1));
        await ChangeAsync(client, HttpMethod.Delete, question, new { });
        DateTimeOffset deletedAt = DatabaseTime(clock);
        (await UpdatedAtAsync(client, passage)).ShouldBe(deletedAt);
        clock.Advance(TimeSpan.FromMinutes(1));
        using (HttpResponseMessage failed = await client.PostAsJsonAsync($"{passage}/publish", new { }))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        (await UpdatedAtAsync(client, passage)).ShouldBe(deletedAt);

        // With no content change, saving the same header must not manufacture a new update time.
        await ChangeAsync(client, HttpMethod.Put, passage,
            new { title = "Updated title", body = "Original body", sourceNote = "Source", usageBasis = "Original" });
        (await UpdatedAtAsync(client, passage)).ShouldBe(deletedAt);
    }

    private BandUpApiFactory CreateFactory(TestClock clock) =>
        new(environment, configureServices: services => services.AddSingleton<TimeProvider>(clock));

    private async Task<HttpClient> CreateClientAsync(BandUpApiFactory factory, Role role)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"{Guid.NewGuid():N}@example.test", "Passage editor", "test-hash", now);
        user.ChangeRole(role);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(user.Id, role.ToString(), now).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static async Task<Guid> ActorIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();

    private static async Task<PagedResult<PassageSummary>> ReadPageAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<PagedResult<PassageSummary>>(path, JsonSerialization.Options))!;

    private static async Task<DateTimeOffset?> UpdatedAtAsync(HttpClient client, string path)
    {
        JsonElement details = await client.GetFromJsonAsync<JsonElement>(path);
        return details.GetProperty("updatedAt").ValueKind == JsonValueKind.Null
            ? null
            : details.GetProperty("updatedAt").GetDateTimeOffset();
    }

    private static DateTimeOffset DatabaseTime(TestClock clock)
    {
        DateTimeOffset now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static async Task<string> CreateAsync(HttpClient client, string path, object input)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, input);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }

    private static async Task ChangeAsync(HttpClient client, HttpMethod method, string path, object input)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(input) };
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static Passage PublishedPassage(string title, Guid actorId, DateTimeOffset now)
    {
        Passage passage = Passage.CreateDraft(title, "Body", "Source", "Original", actorId);
        Question question = passage.AddQuestion(QuestionType.SentenceCompletion, "Prompt", 1);
        question.SetExplanation("Explanation", "Body");
        question.SetAnswerKey("Answer");
        question.VerifyAnswerKey(actorId, now);
        passage.Publish(now);
        return passage;
    }
}
