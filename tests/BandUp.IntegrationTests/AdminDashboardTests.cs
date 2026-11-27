using System.Net.Http.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Dashboard.Admin;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminDashboardTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task Counters_Respect_Passage_Status_And_Include_All_Lessons_And_Accounts_For_Both_Admin_Roles()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User admin = CreateUser(Role.Admin, now);
        User superAdmin = CreateUser(Role.SuperAdmin, now);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.AddRange(admin, superAdmin);
        await db.SaveChangesAsync();
        await using BandUpApiFactory factory = new(environment);
        using HttpClient adminClient = CreateClient(factory, admin, now);
        using HttpClient superClient = CreateClient(factory, superAdmin, now);
        AdminDashboardDto baseline = (await adminClient.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard"))!;

        User learner = CreateUser(Role.User, now);
        User inactive = CreateUser(Role.User, now);
        inactive.Deactivate();
        Passage draft = Passage.CreateDraft("Draft", "Body", "Source", "Original", admin.Id);
        Passage published = PublishedPassage(admin.Id, now);
        Passage archived = PublishedPassage(admin.Id, now);
        archived.Archive();
        Lesson draftLesson = Lesson.CreateDraft("Draft lesson", "Body", QuestionType.SentenceCompletion, 1);
        Lesson publishedLesson = Lesson.CreateDraft("Published lesson", "Body", QuestionType.SentenceCompletion, 2);
        publishedLesson.AddExample("Evidence", "Paraphrase", 1);
        publishedLesson.Publish();
        db.Users.AddRange(learner, inactive);
        db.Passages.AddRange(draft, published, archived);
        db.Lessons.AddRange(draftLesson, publishedLesson);
        await db.SaveChangesAsync();

        AdminDashboardDto expected = new(
            baseline.PublishedPassages + 1, baseline.DraftPassages + 1,
            baseline.TotalLessons + 2, baseline.TotalUsers + 2);
        (await adminClient.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard")).ShouldBe(expected);
        (await superClient.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard")).ShouldBe(expected);

        published.Archive();
        await db.SaveChangesAsync();
        (await adminClient.GetFromJsonAsync<AdminDashboardDto>("/api/admin/dashboard"))
            .ShouldBe(expected with { PublishedPassages = expected.PublishedPassages - 1 });
    }

    private static User CreateUser(Role role, DateTimeOffset now)
    {
        User user = User.Create($"{Guid.NewGuid():N}@example.test", "Dashboard user", "test-hash", now);
        user.ChangeRole(role);
        return user;
    }

    private static HttpClient CreateClient(BandUpApiFactory factory, User user, DateTimeOffset now)
    {
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(user.Id, user.Role.ToString(), now).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        return client;
    }

    private static Passage PublishedPassage(Guid actorId, DateTimeOffset now)
    {
        Passage passage = Passage.CreateDraft("Published", "Body", "Source", "Original", actorId);
        Question question = passage.AddQuestion(QuestionType.SentenceCompletion, "Question", 1);
        question.SetAnswerKey("Answer");
        question.SetExplanation("Explanation", "Body");
        question.VerifyAnswerKey(actorId, now);
        passage.Publish(now);
        return passage;
    }
}
