using System.Net;
using System.Text.Json;
using BandUp.Domain.Content;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// EX-12 to EX-24. Submitting an attempt exactly once: grading, idempotent retries, concurrent
/// submits, the result with its answer key, and what happens when time runs out.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class ExamSubmissionTests(BandUpTestEnvironment environment)
{
    private const string AttemptsUrl = "/api/attempts";

    private BandUpApiFactory CreateFactory(TestClock? clock = null) =>
        new(environment, configureServices: services =>
        {
            if (clock is null)
            {
                return;
            }

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

    [Fact]
    public async Task EX_12_Submitting_Without_A_Session_Returns_401()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);

        using HttpRequestMessage request = new(HttpMethod.Post, $"{AttemptsUrl}/{Guid.NewGuid()}/submit");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EX_13_Submit_Grades_Saved_And_Final_Answers_And_Reveals_The_Key()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex13");

        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "true");

        HttpResponseMessage response = await SubmitAsync(
            client,
            user,
            attemptId,
            Guid.NewGuid().ToString(),
            new[] { new { questionId = passage.CompletionQuestionId, selectedAnswer = "A  Nest" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("attemptId").GetGuid().ShouldBe(attemptId);
        body.GetProperty("rawScore").GetInt32().ShouldBe(2);
        body.GetProperty("totalQuestions").GetInt32().ShouldBe(2);

        // Two of two scales to 40 of 40, which is the top of the published table.
        body.GetProperty("approxBand").GetDecimal().ShouldBe(9.0m);

        JsonElement[] questions = body.GetProperty("questions").EnumerateArray().ToArray();
        questions[0].GetProperty("questionId").GetGuid().ShouldBe(passage.TrueFalseQuestionId);
        questions[0].GetProperty("selectedAnswer").GetString().ShouldBe("true");
        questions[0].GetProperty("isCorrect").GetBoolean().ShouldBeTrue();
        questions[0].GetProperty("correctAnswer").GetString().ShouldBe("True");
        questions[0].GetProperty("explanation").GetString().ShouldNotBeNullOrWhiteSpace();
        questions[1].GetProperty("isCorrect").GetBoolean().ShouldBeTrue();
        questions[1].GetProperty("correctAnswer").GetString().ShouldBe("nests");

        JsonElement state = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}");
        state.GetProperty("status").GetString().ShouldBe("Submitted");
        state.GetProperty("remainingSeconds").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task EX_14_Wrong_Or_Missing_Answers_Score_Zero_And_Point_At_The_Mapped_Lessons()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex14");
        Guid tfngLesson = await ContentTestData.SeedLessonAsync(
            factory.Services, "EX-14 TFNG lesson", QuestionType.TrueFalseNotGiven, 1, true, true);
        Guid completionLesson = await ContentTestData.SeedLessonAsync(
            factory.Services, "EX-14 completion lesson", QuestionType.SentenceCompletion, 1, true, true);

        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "False");

        HttpResponseMessage response = await SubmitAsync(client, user, attemptId, Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("rawScore").GetInt32().ShouldBe(0);

        JsonElement[] questions = body.GetProperty("questions").EnumerateArray().ToArray();
        questions[0].GetProperty("isCorrect").GetBoolean().ShouldBeFalse();
        questions[0].GetProperty("relatedLessonId").GetGuid().ShouldBe(tfngLesson);
        questions[1].GetProperty("selectedAnswer").ValueKind.ShouldBe(JsonValueKind.Null);
        questions[1].GetProperty("isCorrect").GetBoolean().ShouldBeFalse();
        questions[1].GetProperty("relatedLessonId").GetGuid().ShouldBe(completionLesson);
    }

    [Fact]
    public async Task EX_15_Submitting_Without_An_Idempotency_Key_Returns_400()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, _, Guid attemptId) = await StartNewAsync(client, factory, "ex15");

        HttpResponseMessage response = await SubmitAsync(client, user, attemptId, key: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
    }

    [Fact]
    public async Task EX_16_Retrying_With_The_Same_Key_Returns_The_Same_Result()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex16");
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");
        string key = Guid.NewGuid().ToString();

        HttpResponseMessage first = await SubmitAsync(client, user, attemptId, key);
        HttpResponseMessage second = await SubmitAsync(client, user, attemptId, key);
        HttpResponseMessage third = await SubmitAsync(client, user, attemptId, key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.OK);

        string firstBody = await first.Content.ReadAsStringAsync();
        (await second.Content.ReadAsStringAsync()).ShouldBe(firstBody);
        (await third.Content.ReadAsStringAsync()).ShouldBe(firstBody);
    }

    [Fact]
    public async Task EX_17_Submitting_Again_With_A_Different_Key_Returns_409_And_Keeps_The_Score()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex17");
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");
        await SubmitAsync(client, user, attemptId, Guid.NewGuid().ToString());

        // The learner tries to improve the score by submitting different answers under a new key.
        HttpResponseMessage second = await SubmitAsync(
            client,
            user,
            attemptId,
            Guid.NewGuid().ToString(),
            new[] { new { questionId = passage.CompletionQuestionId, selectedAnswer = "nests" } });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(second)).GetProperty("code").GetString().ShouldBe("ATTEMPT_ALREADY_SUBMITTED");

        JsonElement result = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}/result");
        result.GetProperty("rawScore").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task EX_18_A_Key_Already_Used_For_Another_Attempt_Is_Refused()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex18");
        ContentTestData.SeededPassage other =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-18 other passage");
        Guid otherAttemptId = (await StartAsync(client, user, other.PassageId)).GetProperty("id").GetGuid();
        string key = Guid.NewGuid().ToString();
        await SubmitAsync(client, user, attemptId, key);

        HttpResponseMessage response = await SubmitAsync(client, user, otherAttemptId, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("IDEMPOTENCY_KEY_REUSED");
        passage.PassageId.ShouldNotBe(other.PassageId);
    }

    [Fact]
    public async Task EX_19_Ten_Simultaneous_Submits_With_One_Key_All_Return_The_Same_Single_Result()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex19");
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");
        string key = Guid.NewGuid().ToString();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => SubmitAsync(client, user, attemptId, key)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);

        string[] bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        bodies.Distinct().Count().ShouldBe(1);
        JsonDocument.Parse(bodies[0]).RootElement.GetProperty("rawScore").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task EX_20_Ten_Simultaneous_Submits_With_Different_Keys_Let_Exactly_One_Win()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex20");
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => SubmitAsync(client, user, attemptId, Guid.NewGuid().ToString())));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);

        foreach (HttpResponseMessage loser in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            (await ReadJsonAsync(loser)).GetProperty("code").GetString().ShouldBe("ATTEMPT_ALREADY_SUBMITTED");
        }
    }

    [Fact]
    public async Task EX_21_The_Result_Is_Refused_Before_Submit_And_Hidden_From_Others()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, _, Guid attemptId) = await StartNewAsync(client, factory, "ex21a");
        ContentTestData.TestUser other = await ContentTestData.RegisterAndLoginAsync(client, "ex21b");

        using HttpRequestMessage early = ContentTestData.Get($"{AttemptsUrl}/{attemptId}/result", user);
        HttpResponseMessage earlyResponse = await client.SendAsync(early);
        await SubmitAsync(client, user, attemptId, Guid.NewGuid().ToString());
        using HttpRequestMessage foreign = ContentTestData.Get($"{AttemptsUrl}/{attemptId}/result", other);
        HttpResponseMessage foreignResponse = await client.SendAsync(foreign);
        HttpResponseMessage anonymous = await client.GetAsync($"{AttemptsUrl}/{attemptId}/result");

        earlyResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(earlyResponse)).GetProperty("code").GetString().ShouldBe("ATTEMPT_NOT_SUBMITTED");
        foreignResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EX_22_Submitting_After_The_Grace_Period_Is_Refused_And_Records_No_Result()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid attemptId) =
            await StartNewAsync(client, factory, "ex22");
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        clock.Advance(TimeSpan.FromMinutes(21));
        HttpResponseMessage response = await SubmitAsync(
            client,
            user,
            attemptId,
            Guid.NewGuid().ToString(),
            new[] { new { questionId = passage.CompletionQuestionId, selectedAnswer = "nests" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("ATTEMPT_EXPIRED");

        // The sitting reads as expired and has no result, not even one built from saved answers.
        JsonElement state = await GetJsonAsync(client, user, $"{AttemptsUrl}/{attemptId}");
        state.GetProperty("status").GetString().ShouldBe("Expired");
        state.GetProperty("remainingSeconds").GetInt32().ShouldBe(0);

        using HttpRequestMessage resultRequest = ContentTestData.Get($"{AttemptsUrl}/{attemptId}/result", user);
        HttpResponseMessage result = await client.SendAsync(resultRequest);

        result.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(result)).GetProperty("code").GetString().ShouldBe("ATTEMPT_EXPIRED");
    }

    [Fact]
    public async Task EX_23_Starting_After_An_Expired_Attempt_Expires_It_Without_A_Result_And_Begins_A_New_One()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, ContentTestData.SeededPassage passage, Guid oldAttemptId) =
            await StartNewAsync(client, factory, "ex23");
        await SaveAsync(client, user, oldAttemptId, passage.TrueFalseQuestionId, "True");

        clock.Advance(TimeSpan.FromMinutes(25));
        using HttpRequestMessage start = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId = passage.PassageId });
        HttpResponseMessage response = await client.SendAsync(start);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadJsonAsync(response)).GetProperty("id").GetGuid().ShouldNotBe(oldAttemptId);

        // The old sitting ran out of time, so it was closed as expired and recorded no score.
        JsonElement oldState = await GetJsonAsync(client, user, $"{AttemptsUrl}/{oldAttemptId}");
        oldState.GetProperty("status").GetString().ShouldBe("Expired");

        using HttpRequestMessage oldResultRequest = ContentTestData.Get($"{AttemptsUrl}/{oldAttemptId}/result", user);
        HttpResponseMessage oldResult = await client.SendAsync(oldResultRequest);

        oldResult.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(oldResult)).GetProperty("code").GetString().ShouldBe("ATTEMPT_EXPIRED");
    }

    [Fact]
    public async Task EX_24_Submitting_An_Answer_For_Another_Passages_Question_Returns_400()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        (ContentTestData.TestUser user, _, Guid attemptId) = await StartNewAsync(client, factory, "ex24");
        ContentTestData.SeededPassage other =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-24 other passage");

        HttpResponseMessage response = await SubmitAsync(
            client,
            user,
            attemptId,
            Guid.NewGuid().ToString(),
            new[] { new { questionId = other.TrueFalseQuestionId, selectedAnswer = "True" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The refused request changed nothing, so the attempt can still be submitted.
        HttpResponseMessage retry = await SubmitAsync(client, user, attemptId, Guid.NewGuid().ToString());
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<(ContentTestData.TestUser User, ContentTestData.SeededPassage Passage, Guid AttemptId)>
        StartNewAsync(HttpClient client, BandUpApiFactory factory, string prefix)
    {
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, prefix);
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, $"{prefix} passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId)).GetProperty("id").GetGuid();

        return (user, passage, attemptId);
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
        string? key,
        object? answers = null)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post,
            $"{AttemptsUrl}/{attemptId}/submit",
            user,
            new { answers = answers ?? Array.Empty<object>() });

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

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
