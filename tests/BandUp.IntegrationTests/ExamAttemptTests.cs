using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// EX-01 to EX-11. Starting an attempt, the server-owned clock, refresh recovery and autosave.
/// The clock is replaced by a hand-moved one so deadlines are tested without waiting.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class ExamAttemptTests(BandUpTestEnvironment environment)
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
    public async Task EX_01_Starting_An_Attempt_Without_A_Session_Returns_401()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync(AttemptsUrl, new { passageId = Guid.NewGuid() });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EX_02_Starting_An_Attempt_Fixes_A_Twenty_Minute_Deadline_On_The_Server()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex02");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-02 passage");

        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId = passage.PassageId });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        JsonElement body = await ReadJsonAsync(response);
        Guid attemptId = body.GetProperty("id").GetGuid();
        response.Headers.Location?.ToString().ShouldBe($"/api/attempts/{attemptId}");
        body.GetProperty("passageId").GetGuid().ShouldBe(passage.PassageId);
        body.GetProperty("status").GetString().ShouldBe("InProgress");
        body.GetProperty("remainingSeconds").GetInt32().ShouldBe(20 * 60);
        body.GetProperty("isExpired").GetBoolean().ShouldBeFalse();
        body.GetProperty("answers").GetArrayLength().ShouldBe(0);

        DateTimeOffset startedAt = body.GetProperty("startedAt").GetDateTimeOffset();
        DateTimeOffset expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        (expiresAt - startedAt).ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task EX_03_Starting_The_Same_Passage_Again_Returns_The_Running_Attempt()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex03");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-03 passage");

        JsonElement first = await StartAsync(client, user, passage.PassageId, HttpStatusCode.Created);
        JsonElement second = await StartAsync(client, user, passage.PassageId, HttpStatusCode.OK);

        second.GetProperty("id").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());

        // The first response is built in memory (100 ns ticks), the second is read back from the
        // database (microseconds), so the deadline is compared with a tolerance.
        second.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(
            first.GetProperty("expiresAt").GetDateTimeOffset(),
            TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task EX_04_Starting_A_Draft_Or_Unknown_Passage_Returns_404()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex04");
        Guid draftId = await ContentTestData.SeedDraftPassageAsync(factory.Services, user.Id, "EX-04 draft");

        using HttpRequestMessage draft = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId = draftId });
        using HttpRequestMessage unknown = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId = Guid.NewGuid() });

        (await client.SendAsync(draft)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.SendAsync(unknown)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EX_05_An_Attempt_Is_Visible_Only_To_Its_Owner()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser owner = await ContentTestData.RegisterAndLoginAsync(client, "ex05a");
        ContentTestData.TestUser other = await ContentTestData.RegisterAndLoginAsync(client, "ex05b");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, owner.Id, "EX-05 passage");
        Guid attemptId = (await StartAsync(client, owner, passage.PassageId, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        HttpResponseMessage anonymous = await client.GetAsync($"{AttemptsUrl}/{attemptId}");
        using HttpRequestMessage foreign = ContentTestData.Get($"{AttemptsUrl}/{attemptId}", other);
        HttpResponseMessage foreignResponse = await client.SendAsync(foreign);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreignResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadJsonAsync(foreignResponse)).GetProperty("code").GetString().ShouldBe("ATTEMPT_NOT_FOUND");
    }

    [Fact]
    public async Task EX_06_Remaining_Time_Comes_From_The_Server_Clock_And_Stops_At_Zero()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex06");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-06 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        clock.Advance(TimeSpan.FromMinutes(5));
        JsonElement afterFive = await GetStateAsync(client, user, attemptId);

        afterFive.GetProperty("remainingSeconds").GetInt32().ShouldBe(15 * 60);
        afterFive.GetProperty("serverNow").GetDateTimeOffset().ShouldBe(clock.GetUtcNow());
        afterFive.GetProperty("isExpired").GetBoolean().ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(30));
        JsonElement afterDeadline = await GetStateAsync(client, user, attemptId);

        afterDeadline.GetProperty("remainingSeconds").GetInt32().ShouldBe(0);
        afterDeadline.GetProperty("isExpired").GetBoolean().ShouldBeTrue();
        afterDeadline.GetProperty("status").GetString().ShouldBe("Expired");
    }

    [Fact]
    public async Task EX_07_Saved_Answers_Survive_A_Refresh()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex07");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-07 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True", HttpStatusCode.OK);
        await SaveAsync(client, user, attemptId, passage.CompletionQuestionId, "nests", HttpStatusCode.OK);

        // A refresh is a fresh start call plus a state read; both must show the saved answers.
        JsonElement resumed = await StartAsync(client, user, passage.PassageId, HttpStatusCode.OK);
        JsonElement state = await GetStateAsync(client, user, attemptId);

        foreach (JsonElement view in new[] { resumed, state })
        {
            Dictionary<Guid, string?> answers = view.GetProperty("answers").EnumerateArray()
                .ToDictionary(
                    answer => answer.GetProperty("questionId").GetGuid(),
                    answer => answer.GetProperty("selectedAnswer").GetString());

            answers[passage.TrueFalseQuestionId].ShouldBe("True");
            answers[passage.CompletionQuestionId].ShouldBe("nests");
        }
    }

    [Fact]
    public async Task EX_08_Saving_Again_Replaces_The_Answer_And_A_Blank_Clears_It()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex08");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-08 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "False", HttpStatusCode.OK);
        JsonElement replaced =
            await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True", HttpStatusCode.OK);

        replaced.GetProperty("answers").GetArrayLength().ShouldBe(1);
        replaced.GetProperty("answers")[0].GetProperty("selectedAnswer").GetString().ShouldBe("True");

        JsonElement cleared =
            await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "  ", HttpStatusCode.OK);

        cleared.GetProperty("answers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task EX_09_Saving_An_Answer_For_A_Question_Of_Another_Passage_Returns_400()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex09");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-09 passage");
        ContentTestData.SeededPassage otherPassage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-09 other passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Put,
            $"{AttemptsUrl}/{attemptId}/answers/{otherPassage.TrueFalseQuestionId}",
            user,
            new { selectedAnswer = "True" });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("questionId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task EX_10_Answers_Are_Accepted_Within_The_Grace_Period_And_Refused_After_It()
    {
        TestClock clock = new();
        await using BandUpApiFactory factory = CreateFactory(clock);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex10");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-10 passage");
        Guid attemptId = (await StartAsync(client, user, passage.PassageId, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        clock.Advance(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(5));
        await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True", HttpStatusCode.OK);

        clock.Advance(TimeSpan.FromSeconds(10));
        JsonElement refused =
            await SaveAsync(client, user, attemptId, passage.CompletionQuestionId, "nests", HttpStatusCode.Conflict);

        refused.GetProperty("code").GetString().ShouldBe("ATTEMPT_EXPIRED");
    }

    [Fact]
    public async Task EX_11_Simultaneous_Starts_Create_Exactly_One_Attempt()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex11");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "EX-11 passage");

        Task<HttpResponseMessage>[] calls =
        [
            .. Enumerable.Range(0, 10).Select(_ =>
            {
                HttpRequestMessage request = ContentTestData.Send(
                    HttpMethod.Post, AttemptsUrl, user, new { passageId = passage.PassageId });
                return client.SendAsync(request);
            }),
        ];

        HttpResponseMessage[] responses = await Task.WhenAll(calls);

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(9);

        Guid[] ids = new Guid[responses.Length];
        for (int index = 0; index < responses.Length; index++)
        {
            ids[index] = (await ReadJsonAsync(responses[index])).GetProperty("id").GetGuid();
        }

        ids.Distinct().Count().ShouldBe(1);
    }

    private static async Task<JsonElement> StartAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid passageId,
        HttpStatusCode expected)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(expected);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> GetStateAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid attemptId)
    {
        using HttpRequestMessage request = ContentTestData.Get($"{AttemptsUrl}/{attemptId}", user);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> SaveAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid attemptId,
        Guid questionId,
        string answer,
        HttpStatusCode expected)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Put,
            $"{AttemptsUrl}/{attemptId}/answers/{questionId}",
            user,
            new { selectedAnswer = answer });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(expected);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
