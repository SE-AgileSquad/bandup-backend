using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// Shared helpers for tests that need a signed-in learner and reading content. Content is
/// created through the domain model so the tests never bypass its invariants.
/// </summary>
public static class ContentTestData
{
    private const string Password = "aaaaaaaaa1";

    public sealed record TestUser(Guid Id, string AccessCookie);

    public sealed record SeededPassage(
        Guid PassageId,
        Guid TrueFalseQuestionId,
        Guid CompletionQuestionId);

    public static async Task<TestUser> RegisterAndLoginAsync(HttpClient client, string prefix)
    {
        string email = $"{prefix}-{Guid.NewGuid():N}@example.com";

        HttpResponseMessage register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = Password,
            displayName = "Test Learner",
            captchaToken = "fake-captcha-token",
        });
        register.EnsureSuccessStatusCode();

        Guid userId = JsonDocument
            .Parse(await register.Content.ReadAsStringAsync())
            .RootElement
            .GetProperty("id")
            .GetGuid();

        HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = Password,
            captchaToken = "fake-captcha-token",
        });
        login.EnsureSuccessStatusCode();

        string cookie = login.Headers
            .GetValues("Set-Cookie")
            .Select(header => header.Split(';')[0].Trim())
            .First(pair => pair.StartsWith("bu_at=", StringComparison.Ordinal));

        return new TestUser(userId, cookie);
    }

    public static HttpRequestMessage Get(string url, TestUser? user = null)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);

        if (user is not null)
        {
            request.Headers.Add("Cookie", user.AccessCookie);
        }

        return request;
    }

    /// <summary>
    /// A published passage with one True/False/Not Given question and one sentence-completion
    /// question, both with verified answer keys.
    /// </summary>
    public static async Task<SeededPassage> SeedPublishedPassageAsync(
        IServiceProvider services,
        Guid authorId,
        string title)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Passage passage = Passage.CreateDraft(
            title,
            "Birds build nests in spring.",
            "Original text written for the test.",
            "Written in-house.",
            authorId);

        Question trueFalse = passage.AddQuestion(QuestionType.TrueFalseNotGiven, "Birds build nests in spring.", 1);
        trueFalse.SetAnswerKey("True");
        trueFalse.SetExplanation("The first sentence says so.", "Birds build nests in spring.");
        trueFalse.VerifyAnswerKey(authorId, now);

        Question completion = passage.AddQuestion(QuestionType.SentenceCompletion, "Birds build ___ in spring.", 2);
        completion.SetAnswerKey("nests", ["a nest"]);
        completion.SetExplanation("The passage names what birds build.", "Birds build nests in spring.");
        completion.VerifyAnswerKey(authorId, now);

        passage.Publish(now);

        using IServiceScope scope = services.CreateScope();
        BandUpDbContext context = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        context.Passages.Add(passage);
        await context.SaveChangesAsync();

        return new SeededPassage(passage.Id, trueFalse.Id, completion.Id);
    }

    public static async Task<Guid> SeedDraftPassageAsync(IServiceProvider services, Guid authorId, string title)
    {
        Passage passage = Passage.CreateDraft(title, "Draft body.", "Test source.", "Test basis.", authorId);
        passage.AddQuestion(QuestionType.TrueFalseNotGiven, "A draft question.", 1);

        using IServiceScope scope = services.CreateScope();
        BandUpDbContext context = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        context.Passages.Add(passage);
        await context.SaveChangesAsync();

        return passage.Id;
    }

    public static async Task<Guid> SeedLessonAsync(
        IServiceProvider services,
        string title,
        QuestionType type,
        int sortOrder,
        bool publish,
        bool mapAsDefault)
    {
        Lesson lesson = Lesson.CreateDraft(title, $"How to approach {type} questions.", type, sortOrder);
        lesson.AddExample("Birds build nests in spring.", "Nest-building happens in the spring.", 2);
        lesson.AddExample("Spring is when nests appear.", "Birds make nests in spring.", 1);

        if (publish)
        {
            lesson.Publish();
        }

        using IServiceScope scope = services.CreateScope();
        BandUpDbContext context = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        context.Lessons.Add(lesson);

        if (mapAsDefault)
        {
            // The default mapping is unique per question type, so a test replaces any earlier one.
            await context.LessonMaps
                .Where(map => map.QuestionType == type && map.ErrorPattern == null)
                .ExecuteDeleteAsync();

            context.LessonMaps.Add(LessonMap.Create(lesson));
        }

        await context.SaveChangesAsync();

        return lesson.Id;
    }
}
