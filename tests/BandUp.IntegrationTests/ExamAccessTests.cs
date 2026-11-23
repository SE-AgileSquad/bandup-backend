using System.Net;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// EX-25 to EX-27. Who may change an attempt: an unknown id and someone else's attempt are both
/// "not found" for saving and submitting, and a submitted attempt no longer accepts answers.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class ExamAccessTests(BandUpTestEnvironment environment)
{
    private const string AttemptsUrl = "/api/attempts";

    [Fact]
    public async Task EX_25_Saving_Or_Submitting_An_Unknown_Attempt_Returns_404()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex25");

        HttpResponseMessage save = await SaveAsync(client, user, Guid.NewGuid(), Guid.NewGuid(), "True");
        HttpResponseMessage submit = await SubmitAsync(client, user, Guid.NewGuid());

        save.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadJsonAsync(save)).GetProperty("code").GetString().ShouldBe("ATTEMPT_NOT_FOUND");
        submit.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadJsonAsync(submit)).GetProperty("code").GetString().ShouldBe("ATTEMPT_NOT_FOUND");
    }

    [Fact]
    public async Task EX_26_Another_User_Cannot_Save_Into_Or_Submit_My_Attempt()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser owner = await ContentTestData.RegisterAndLoginAsync(client, "ex26a");
        ContentTestData.TestUser intruder = await ContentTestData.RegisterAndLoginAsync(client, "ex26b");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, owner.Id, "ex26 passage");
        Guid attemptId = await StartAsync(client, owner, passage.PassageId);

        HttpResponseMessage save = await SaveAsync(client, intruder, attemptId, passage.TrueFalseQuestionId, "False");
        HttpResponseMessage submit = await SubmitAsync(client, intruder, attemptId);

        save.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        submit.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nothing changed for the owner: no answer was stored and the attempt is still open.
        using HttpRequestMessage read = ContentTestData.Get($"{AttemptsUrl}/{attemptId}", owner);
        JsonElement state = await ReadJsonAsync(await client.SendAsync(read));
        state.GetProperty("status").GetString().ShouldBe("InProgress");
        state.GetProperty("answers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task EX_27_A_Submitted_Attempt_Refuses_New_Answers_With_409()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = ContentTestData.CreateClient(factory);
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "ex27");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "ex27 passage");
        Guid attemptId = await StartAsync(client, user, passage.PassageId);

        (await SubmitAsync(client, user, attemptId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage late = await SaveAsync(client, user, attemptId, passage.TrueFalseQuestionId, "True");

        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(late)).GetProperty("code").GetString().ShouldBe("ATTEMPT_ALREADY_SUBMITTED");
    }

    private static async Task<Guid> StartAsync(HttpClient client, ContentTestData.TestUser user, Guid passageId)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post, AttemptsUrl, user, new { passageId });
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SaveAsync(
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

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client,
        ContentTestData.TestUser user,
        Guid attemptId)
    {
        using HttpRequestMessage request = ContentTestData.Send(
            HttpMethod.Post,
            $"{AttemptsUrl}/{attemptId}/submit",
            user,
            new { answers = Array.Empty<object>() });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
