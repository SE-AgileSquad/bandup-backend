using System.Net;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// EX-33 to EX-37. A sitting that ran out of time is graded from the answers the learner had
/// already saved, as of the deadline, whatever notices it first: reading the result, the state,
/// the lists, or a submit that arrives too late. Nothing that arrives after the deadline and
/// grace is ever part of the result.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class ExamDeadlineClosingTests(BandUpTestEnvironment environment)
{
    private const string AttemptsUrl = "/api/attempts";

    private BandUpApiFactory CreateFactory(TestClock clock) =>
        new(environment, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

    [Fact]
    public async Task EX_33_Reading_The_Result_Of_An_Abandoned_Attempt_Grades_Its_Saved_Answers()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex33");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-33 passage");
        JsonElement started = await StartAsync(client, user, passage.PassageId);
        Guid attemptId = started.GetProperty("id").GetGuid();
        DateTimeOffset deadline = started.GetProperty("expiresAt").GetDateTimeOffset();

        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        // The browser never submitted. No submit request is made at all.
        clock.Advance(TimeSpan.FromMinutes(25));
        JsonElement result = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}/result");

        result.GetProperty("rawScore").GetInt32().ShouldBe(1);
        result.GetProperty("totalQuestions").GetInt32().ShouldBe(2);
        result.GetProperty("approxBand").GetDecimal().ShouldBe(5.5m);
        result.GetProperty("submittedAt").GetDateTimeOffset().ShouldBe(deadline);
        result.GetProperty("timeSpentSeconds").GetInt32().ShouldBe(20 * 60);

        JsonElement[] questions = result.GetProperty("questions").EnumerateArray().ToArray();
        questions[0].GetProperty("isCorrect").GetBoolean().ShouldBeTrue();
        questions[1].GetProperty("selectedAnswer").ValueKind.ShouldBe(JsonValueKind.Null);

        // Reading it again returns the same stored result.
        JsonElement again = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}/result");
        again.GetProperty("rawScore").GetInt32().ShouldBe(1);
        again.GetProperty("submittedAt").GetDateTimeOffset().ShouldBe(deadline);
    }

    [Fact]
    public async Task EX_34_The_History_And_Summary_Show_An_Abandoned_Attempt_With_Saved_Answers_As_Completed()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex34");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-34 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId)).GetProperty("id").GetGuid();
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        clock.Advance(TimeSpan.FromMinutes(25));

        JsonElement history = await GetJsonAsync(client, user, AttemptsUrl);

        history.GetProperty("totalCount").GetInt32().ShouldBe(1);
        JsonElement item = history.GetProperty("items")[0];
        item.GetProperty("attemptId").GetGuid().ShouldBe(attemptId);
        item.GetProperty("rawScore").GetInt32().ShouldBe(1);
        item.GetProperty("approxBand").GetDecimal().ShouldBe(5.5m);

        JsonElement[] summaries = (await GetJsonAsync(client, user, $"{AttemptsUrl}/summary"))
            .EnumerateArray()
            .ToArray();

        summaries.Length.ShouldBe(1);
        summaries[0].GetProperty("state").GetString().ShouldBe("Completed");
        summaries[0].GetProperty("rawScore").GetInt32().ShouldBe(1);

        JsonElement state = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}");
        state.GetProperty("status").GetString().ShouldBe("Submitted");
        state.GetProperty("remainingSeconds").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task EX_35_A_Submit_That_Arrives_After_The_Server_Closed_The_Attempt_Gets_The_Same_Result()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex35");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-35 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId)).GetProperty("id").GetGuid();
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        clock.Advance(TimeSpan.FromMinutes(21));

        // A refresh reads the state first, and the server closes the sitting on its own.
        (await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}"))
            .GetProperty("status").GetString().ShouldBe("Submitted");

        // Then the slow submit arrives, with a key the server has not seen and an answer that is
        // too late to count. Neither of them changes the result.
        HttpResponseMessage first = await SubmitAsync(
            client,
            user,
            attemptId,
            new[] { new { questionId = passage.CompletionQuestionId, selectedAnswer = "nests" } });
        HttpResponseMessage second = await SubmitAsync(client, user, attemptId);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(first)).GetProperty("rawScore").GetInt32().ShouldBe(1);
        (await ReadJsonAsync(second)).GetProperty("rawScore").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task EX_36_An_Answer_Saved_After_The_Grace_Period_Is_Refused_And_Is_Not_Graded()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex36");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-36 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId)).GetProperty("id").GetGuid();
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        clock.Advance(TimeSpan.FromMinutes(21));
        using HttpRequestMessage late = ContentTestData.Send(
            HttpMethod.Put,
            $"{AttemptsUrl}/{attemptId}/answers/{passage.CompletionQuestionId}",
            user,
            new { selectedAnswer = "nests" });
        HttpResponseMessage refused = await client.SendAsync(late);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(refused)).GetProperty("code").GetString().ShouldBe("ATTEMPT_EXPIRED");

        JsonElement result = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}/result");

        result.GetProperty("rawScore").GetInt32().ShouldBe(1);
        result.GetProperty("questions")[1].GetProperty("selectedAnswer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task EX_37_Someone_Elses_Due_Attempt_Is_Not_Visible_To_Another_Learner()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser owner = await ContentTestData.RegisterAndLoginAsync(client, "ex37owner");
        ContentTestData.TestUser other = await ContentTestData.RegisterAndLoginAsync(client, "ex37other");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, owner.Id, "EX-37 passage");
        Guid attemptId = (await StartAsync(client, owner, passage.PassageId)).GetProperty("id").GetGuid();
        await SaveAsync(client, owner, attemptId, passage.TrueFalseQuestionId, "True");

        clock.Advance(TimeSpan.FromMinutes(25));

        foreach (string url in new[] { $"{AttemptsUrl}/{attemptId}", $"{AttemptsUrl}/{attemptId}/result" })
        {
            using HttpRequestMessage request = ContentTestData.Get(url, other);
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // The other learner's lists stay empty, and the owner still gets the graded result.
        (await GetJsonAsync(client, other, AttemptsUrl)).GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await GetJsonAsync(client, owner, $"{AttemptsUrl}/{attemptId}/result"))
            .GetProperty("rawScore").GetInt32().ShouldBe(1);
    }

    private static async Task<JsonElement> StartAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid passageId)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
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
