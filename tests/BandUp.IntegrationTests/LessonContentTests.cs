using System.Net;
using System.Text.Json;
using BandUp.Domain.Content;
using BandUp.IntegrationTests.Infrastructure;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// LES-01 to LES-05. Lessons are for signed-in learners and only published lessons are visible.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class LessonContentTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task LES_01_Listing_Lessons_Without_A_Session_Returns_401()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/lessons");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LES_02_Listing_Lessons_Can_Be_Narrowed_To_One_Question_Type()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "les02");
        Guid tfng = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-02 TFNG", QuestionType.TrueFalseNotGiven, 1, true, false);
        Guid completion = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-02 completion", QuestionType.SentenceCompletion, 1, true, false);
        Guid draft = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-02 draft", QuestionType.TrueFalseNotGiven, 1, false, false);

        using HttpRequestMessage request =
            ContentTestData.Get("/api/lessons?questionType=TrueFalseNotGiven&pageSize=100", user);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement[] items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToArray();
        Guid[] ids = [.. items.Select(item => item.GetProperty("id").GetGuid())];

        ids.ShouldContain(tfng);
        ids.ShouldNotContain(completion);
        ids.ShouldNotContain(draft);
        items.ShouldAllBe(item => item.GetProperty("questionType").GetString() == "TrueFalseNotGiven");
    }

    [Fact]
    public async Task LES_03_Listing_Lessons_Without_A_Filter_Returns_Every_Type()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "les03");
        Guid tfng = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-03 TFNG", QuestionType.TrueFalseNotGiven, 1, true, false);
        Guid completion = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-03 completion", QuestionType.SentenceCompletion, 1, true, false);

        using HttpRequestMessage request = ContentTestData.Get("/api/lessons?pageSize=100", user);
        HttpResponseMessage response = await client.SendAsync(request);

        JsonElement[] items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToArray();
        Guid[] ids = [.. items.Select(item => item.GetProperty("id").GetGuid())];

        ids.ShouldContain(tfng);
        ids.ShouldContain(completion);
    }

    [Fact]
    public async Task LES_04_Getting_A_Lesson_Returns_Its_Examples_In_Order()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "les04");
        Guid lessonId = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-04 lesson", QuestionType.TrueFalseNotGiven, 1, true, false);

        using HttpRequestMessage request = ContentTestData.Get($"/api/lessons/{lessonId}", user);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("title").GetString().ShouldBe("LES-04 lesson");
        body.GetProperty("questionType").GetString().ShouldBe("TrueFalseNotGiven");

        JsonElement[] examples = body.GetProperty("examples").EnumerateArray().ToArray();
        examples.Length.ShouldBe(2);
        examples[0].GetProperty("sortOrder").GetInt32().ShouldBe(1);
        examples[1].GetProperty("sortOrder").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task LES_05_Getting_A_Draft_Or_Unknown_Lesson_Returns_404()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "les05");
        Guid draftId = await ContentTestData.SeedLessonAsync(
            factory.Services, "LES-05 draft", QuestionType.SentenceCompletion, 1, false, false);

        using HttpRequestMessage draftRequest = ContentTestData.Get($"/api/lessons/{draftId}", user);
        HttpResponseMessage draft = await client.SendAsync(draftRequest);

        using HttpRequestMessage unknownRequest = ContentTestData.Get($"/api/lessons/{Guid.NewGuid()}", user);
        HttpResponseMessage unknown = await client.SendAsync(unknownRequest);

        draft.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LES_06_Getting_A_Lesson_Without_A_Session_Returns_401()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/lessons/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
