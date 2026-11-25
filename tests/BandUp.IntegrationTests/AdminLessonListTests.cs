using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Application.Features.Learning.Admin;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminLessonListTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Lesson_Cards_Combine_Title_Search_Status_And_Pagination(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, role);
        string marker = Guid.NewGuid().ToString("N");
        Lesson draft = Lesson.CreateDraft($"{marker} A draft", "Body", QuestionType.TrueFalseNotGiven, 10);
        Lesson published = PublishedLesson($"{marker} B published", 10);
        Lesson archived = PublishedLesson($"{marker} C archived", 20);
        archived.Archive();
        Lesson bodyOnly = Lesson.CreateDraft("Other title", marker, QuestionType.SentenceCompletion, 1);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            BandUpDbContext db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            db.Lessons.AddRange(draft, published, archived, bodyOnly);
            await db.SaveChangesAsync();
        }

        string path = $"/api/admin/lessons?search=%20{marker.ToUpperInvariant()}%20";
        PagedResult<LessonSummary> first = await ReadPageAsync(client, $"{path}&pageSize=2");
        first.TotalCount.ShouldBe(3);
        first.Page.ShouldBe(1);
        first.PageSize.ShouldBe(2);
        first.Items.Select(item => item.Id).ShouldBe(new[] { draft.Id, published.Id });
        first.Items[0].QuestionType.ShouldBe(QuestionType.TrueFalseNotGiven);
        first.Items[0].HasPractice.ShouldBeFalse();
        first.Items[0].ExampleCount.ShouldBe(0);
        first.Items[1].HasPractice.ShouldBeTrue();
        first.Items[1].ExampleCount.ShouldBe(1);

        PagedResult<LessonSummary> second = await ReadPageAsync(client, $"{path}&page=2&pageSize=2");
        second.TotalCount.ShouldBe(3);
        second.Items.Single().Id.ShouldBe(archived.Id);
        foreach (LessonStatus status in Enum.GetValues<LessonStatus>())
        {
            PagedResult<LessonSummary> filtered = await ReadPageAsync(client, $"{path}&status={status}");
            filtered.TotalCount.ShouldBe(1);
            filtered.Items.Single().Status.ShouldBe(status);
        }

        PagedResult<LessonSummary> pastEnd = await ReadPageAsync(client, $"{path}&page=2147483647&pageSize=100");
        pastEnd.TotalCount.ShouldBe(3);
        pastEnd.Items.ShouldBeEmpty();
        PagedResult<LessonSummary> missing = await ReadPageAsync(client,
            $"/api/admin/lessons?search=missing-{marker}&status=Draft");
        missing.TotalCount.ShouldBe(0);
        missing.Items.ShouldBeEmpty();
        PagedResult<LessonSummary> byType = await ReadPageAsync(client, $"{path}&questionType=TrueFalseNotGiven");
        byType.TotalCount.ShouldBe(1);
        byType.Items.Single().Id.ShouldBe(draft.Id);
        PagedResult<LessonSummary> combined = await ReadPageAsync(client,
            $"{path}&questionType=SentenceCompletion&status=Archived&pageSize=1");
        combined.TotalCount.ShouldBe(1);
        combined.Items.Single().Id.ShouldBe(archived.Id);
        PagedResult<LessonSummary> clamped = await ReadPageAsync(client, $"{path}&page=0&pageSize=1000");
        clamped.Page.ShouldBe(1);
        clamped.PageSize.ShouldBe(100);
        clamped.Items.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Archiving_Hides_The_Lesson_From_Learners_And_Recommendations_While_Keeping_History(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient admin = await CreateClientAsync(factory, role);
        using HttpClient learner = await CreateClientAsync(factory, Role.User);
        Guid learnerId = (await learner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();
        Lesson lesson = PublishedLesson($"Archive lesson {Guid.NewGuid():N}", 1);
        LessonMap mapping = LessonMap.Create(lesson);
        LessonProgress progress = LessonProgress.Complete(learnerId, lesson.Id, TimeProvider.System.GetUtcNow());
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            BandUpDbContext db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            await db.LessonMaps.Where(item => item.QuestionType == lesson.QuestionType && item.ErrorPattern == null)
                .ExecuteDeleteAsync();
            db.Lessons.Add(lesson);
            db.LessonMaps.Add(mapping);
            db.LessonProgressEntries.Add(progress);
            await db.SaveChangesAsync();
            var recommended = await scope.ServiceProvider.GetRequiredService<ILessonQueries>()
                .GetDefaultLessonIdsAsync([lesson.QuestionType], CancellationToken.None);
            recommended[lesson.QuestionType].ShouldBe(lesson.Id);
        }

        string path = $"/api/admin/lessons/{lesson.Id}";
        using (HttpResponseMessage beforeArchive = await learner.GetAsync($"/api/lessons/{lesson.Id}"))
        {
            beforeArchive.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage archived = await admin.PostAsJsonAsync($"{path}/archive", new { });
            archived.StatusCode.ShouldBe(HttpStatusCode.NoContent, await archived.Content.ReadAsStringAsync());
        }

        LessonDetails details = (await admin.GetFromJsonAsync<LessonDetails>(path, JsonSerialization.Options))!;
        details.Status.ShouldBe(LessonStatus.Archived);
        details.Body.ShouldBe(lesson.Body);
        details.Examples.Single().Id.ShouldBe(lesson.Examples.Single().Id);
        details.HasPractice.ShouldBeTrue();
        using (HttpResponseMessage hidden = await learner.GetAsync($"/api/lessons/{lesson.Id}"))
        {
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        JsonElement learnerList = await learner.GetFromJsonAsync<JsonElement>("/api/lessons?pageSize=100");
        learnerList.GetProperty("items").EnumerateArray()
            .ShouldNotContain(item => item.GetProperty("id").GetGuid() == lesson.Id);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            BandUpDbContext db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            (await db.LessonProgressEntries.AnyAsync(item => item.Id == progress.Id)).ShouldBeTrue();
            (await db.LessonMaps.AnyAsync(item => item.Id == mapping.Id)).ShouldBeTrue();
            var recommended = await scope.ServiceProvider.GetRequiredService<ILessonQueries>()
                .GetDefaultLessonIdsAsync([lesson.QuestionType], CancellationToken.None);
            recommended.ContainsKey(lesson.QuestionType).ShouldBeFalse();
        }

        using (HttpResponseMessage republish = await admin.PostAsJsonAsync($"{path}/publish", new { }))
        {
            republish.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        using (HttpResponseMessage unknown = await admin.PostAsJsonAsync($"/api/admin/lessons/{Guid.NewGuid()}/archive", new { }))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using HttpResponseMessage created = await admin.PostAsJsonAsync("/api/admin/lessons", new
        {
            title = "A draft", body = "Draft body", questionType = "TrueFalseNotGiven", sortOrder = 1,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using HttpResponseMessage draftArchive = await admin.PostAsJsonAsync($"{created.Headers.Location}/archive", new { });
        draftArchive.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static Lesson PublishedLesson(string title, int sortOrder)
    {
        Lesson lesson = Lesson.CreateDraft(title, "Lesson body", QuestionType.SentenceCompletion, sortOrder);
        lesson.AddExample("The birds build nests.", "Nest building", 1);
        lesson.Publish();
        return lesson;
    }

    private async Task<HttpClient> CreateClientAsync(BandUpApiFactory factory, Role role)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User actor = User.Create($"{Guid.NewGuid():N}@example.test", "Lesson editor", "test-hash", now);
        actor.ChangeRole(role);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actor.Id, role.ToString(), now).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static async Task<PagedResult<LessonSummary>> ReadPageAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<PagedResult<LessonSummary>>(path, JsonSerialization.Options))!;
}
