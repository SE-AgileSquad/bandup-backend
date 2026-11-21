using System.Net;
using System.Text.Json;
using BandUp.Domain.Vocabulary;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// EX-28 to EX-32. What the result adds beyond the score (passage title, approximate band, time
/// spent, approved paraphrase pairs) and the two lists a learner reads: the test history of
/// submitted attempts and the standing on each passage for the Reading Tests list.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class ExamResultAndHistoryTests(BandUpTestEnvironment environment)
{
    private const string AttemptsUrl = "/api/attempts";

    private BandUpApiFactory CreateFactory(TestClock clock) =>
        new(environment, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

    [Fact]
    public async Task EX_28_The_Result_Carries_The_Title_The_Band_And_The_Time_Spent()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex28");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-28 passage");
        Guid attemptId = await StartAsync(client, user, passage.PassageId);

        clock.Advance(TimeSpan.FromMinutes(7));
        HttpResponseMessage response = await SubmitAsync(
            client,
            user,
            attemptId,
            new[]
            {
                new { questionId = passage.TrueFalseQuestionId, selectedAnswer = "True" },
                new { questionId = passage.CompletionQuestionId, selectedAnswer = "wrong" },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("passageTitle").GetString().ShouldBe("EX-28 passage");
        body.GetProperty("rawScore").GetInt32().ShouldBe(1);

        // One of two scales to 20 of 40, which the published table puts at band 5.5.
        body.GetProperty("approxBand").GetDecimal().ShouldBe(5.5m);
        body.GetProperty("timeSpentSeconds").GetInt32().ShouldBe(7 * 60);
        body.GetProperty("durationSeconds").GetInt32().ShouldBe(20 * 60);

        // A refresh reads the stored values back unchanged.
        JsonElement again = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}/result");
        again.GetProperty("approxBand").GetDecimal().ShouldBe(5.5m);
        again.GetProperty("timeSpentSeconds").GetInt32().ShouldBe(7 * 60);
        again.GetProperty("passageTitle").GetString().ShouldBe("EX-28 passage");
    }

    [Fact]
    public async Task EX_29_Time_Spent_Never_Exceeds_The_Duration_When_Submitted_In_The_Grace_Period()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex29");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-29 passage");
        Guid attemptId = await StartAsync(client, user, passage.PassageId);

        clock.Advance(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(5));
        HttpResponseMessage response = await SubmitAsync(client, user, attemptId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("timeSpentSeconds").GetInt32().ShouldBe(20 * 60);
        body.GetProperty("durationSeconds").GetInt32().ShouldBe(20 * 60);
    }

    [Fact]
    public async Task EX_30_Only_Approved_Paraphrase_Pairs_Appear_In_The_Result()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex30");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-30 passage");
        await SeedParaphrasePairsAsync(factory.Services, user.Id, passage.TrueFalseQuestionId);
        Guid attemptId = await StartAsync(client, user, passage.PassageId);

        HttpResponseMessage response = await SubmitAsync(client, user, attemptId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement[] questions =
            (await ReadJsonAsync(response)).GetProperty("questions").EnumerateArray().ToArray();

        JsonElement[] pairs = questions[0].GetProperty("paraphrasePairs").EnumerateArray().ToArray();
        pairs.Length.ShouldBe(1);
        pairs[0].GetProperty("sourceSentence").GetString().ShouldBe("Birds build nests in spring.");
        pairs[0].GetProperty("equivalentSentence").GetString().ShouldBe("Nest-building happens in the spring.");

        // A question without approved pairs still carries the field, as an empty list.
        questions[1].GetProperty("paraphrasePairs").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task EX_31_The_Test_History_Lists_Only_Submitted_Attempts_Newest_First()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex31");
        ContentTestData.TestUser other = await ContentTestData.RegisterAndLoginAsync(client, "ex31other");
        ContentTestData.SeededPassage first =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-31 first");
        ContentTestData.SeededPassage second =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-31 second");
        ContentTestData.SeededPassage third =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-31 third");

        (await SubmitAsync(client, user, await StartAsync(client, user, first.PassageId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        clock.Advance(TimeSpan.FromMinutes(1));
        (await SubmitAsync(client, user, await StartAsync(client, user, second.PassageId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A sitting that is never submitted and one still running do not belong in the history.
        await StartAsync(client, user, third.PassageId);
        clock.Advance(TimeSpan.FromMinutes(25));
        await StartAsync(client, user, first.PassageId);

        JsonElement history = await GetJsonAsync(client, user, AttemptsUrl);

        history.GetProperty("totalCount").GetInt32().ShouldBe(2);
        JsonElement[] items = history.GetProperty("items").EnumerateArray().ToArray();
        items.Length.ShouldBe(2);
        items[0].GetProperty("passageTitle").GetString().ShouldBe("EX-31 second");
        items[1].GetProperty("passageTitle").GetString().ShouldBe("EX-31 first");
        items[0].GetProperty("rawScore").GetInt32().ShouldBe(0);
        items[0].GetProperty("totalQuestions").GetInt32().ShouldBe(2);
        items[0].GetProperty("approxBand").GetDecimal().ShouldBe(0.0m);
        items[0].GetProperty("attemptId").GetGuid().ShouldNotBe(Guid.Empty);

        JsonElement secondPage = await GetJsonAsync(client, user, $"{AttemptsUrl}?page=2&pageSize=1");
        secondPage.GetProperty("totalCount").GetInt32().ShouldBe(2);
        JsonElement[] secondItems = secondPage.GetProperty("items").EnumerateArray().ToArray();
        secondItems.Length.ShouldBe(1);
        secondItems[0].GetProperty("passageTitle").GetString().ShouldBe("EX-31 first");

        // Someone else's history never shows these attempts.
        JsonElement othersHistory = await GetJsonAsync(client, other, AttemptsUrl);
        othersHistory.GetProperty("totalCount").GetInt32().ShouldBe(0);
        othersHistory.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task EX_32_The_Passage_Summaries_Show_What_Is_Running_And_What_Is_Completed()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex32");
        ContentTestData.SeededPassage untouched =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-32 untouched");
        ContentTestData.SeededPassage ranOut =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-32 ran out");
        ContentTestData.SeededPassage finished =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-32 finished");
        ContentTestData.SeededPassage running =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-32 running");

        await StartAsync(client, user, ranOut.PassageId);
        clock.Advance(TimeSpan.FromMinutes(25));

        Guid finishedAttemptId = await StartAsync(client, user, finished.PassageId);
        await SaveAsync(client, user, finishedAttemptId, finished.TrueFalseQuestionId, "True");
        (await SubmitAsync(client, user, finishedAttemptId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        Guid runningAttemptId = await StartAsync(client, user, running.PassageId);

        JsonElement[] summaries = (await GetJsonAsync(client, user, $"{AttemptsUrl}/summary"))
            .EnumerateArray()
            .ToArray();

        // A passage never started and one whose only sitting ran out of time have no entry.
        summaries.Length.ShouldBe(2);
        summaries.ShouldNotContain(summary => summary.GetProperty("passageId").GetGuid() == untouched.PassageId);
        summaries.ShouldNotContain(summary => summary.GetProperty("passageId").GetGuid() == ranOut.PassageId);

        JsonElement completed = summaries.Single(
            summary => summary.GetProperty("passageId").GetGuid() == finished.PassageId);
        completed.GetProperty("state").GetString().ShouldBe("Completed");
        completed.GetProperty("attemptId").GetGuid().ShouldBe(finishedAttemptId);
        completed.GetProperty("rawScore").GetInt32().ShouldBe(1);
        completed.GetProperty("totalQuestions").GetInt32().ShouldBe(2);
        completed.GetProperty("remainingSeconds").ValueKind.ShouldBe(JsonValueKind.Null);

        JsonElement inProgress = summaries.Single(
            summary => summary.GetProperty("passageId").GetGuid() == running.PassageId);
        inProgress.GetProperty("state").GetString().ShouldBe("InProgress");
        inProgress.GetProperty("attemptId").GetGuid().ShouldBe(runningAttemptId);
        inProgress.GetProperty("remainingSeconds").GetInt32().ShouldBe(20 * 60);
        inProgress.GetProperty("rawScore").ValueKind.ShouldBe(JsonValueKind.Null);

        // Starting the finished passage again makes a running sitting, which then wins over the
        // earlier result.
        Guid secondAttemptId = await StartAsync(client, user, finished.PassageId);
        JsonElement[] after = (await GetJsonAsync(client, user, $"{AttemptsUrl}/summary"))
            .EnumerateArray()
            .ToArray();

        JsonElement again = after.Single(
            summary => summary.GetProperty("passageId").GetGuid() == finished.PassageId);
        again.GetProperty("state").GetString().ShouldBe("InProgress");
        again.GetProperty("attemptId").GetGuid().ShouldBe(secondAttemptId);
    }

    private static async Task SeedParaphrasePairsAsync(IServiceProvider services, Guid authorId, Guid questionId)
    {
        ParaphrasePair approved = ParaphrasePair.CreateDraft(
            "Birds build nests in spring.",
            "Nest-building happens in the spring.",
            authorId,
            questionId);
        approved.Approve();

        // Drafts are still being written by the admin and must never reach a learner.
        ParaphrasePair draft = ParaphrasePair.CreateDraft("in spring", "during the season", authorId, questionId);

        using IServiceScope scope = services.CreateScope();
        BandUpDbContext context = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        context.ParaphrasePairs.AddRange(approved, draft);
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> StartAsync(HttpClient client, ContentTestData.TestUser user, Guid passageId)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task SaveAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid attemptId,
        Guid questionId,
        string answer)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Put,
            $"{AttemptsUrl}/{attemptId}/answers/{questionId}",
            user,
            new { selectedAnswer = answer });

        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid attemptId,
        object? answers = null)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post,
            $"{AttemptsUrl}/{attemptId}/submit",
            user,
            new { answers = answers ?? Array.Empty<object>() });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> GetJsonAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        string url)
    {
        using HttpRequestMessage request = ContentTestData.Get(url, user);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
