using System.Net;
using System.Text.Json;
using BandUp.Domain.Content;
using BandUp.IntegrationTests.Infrastructure;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// READ-01 to READ-06. Reading content is for signed-in learners, only published content is
/// visible, and no response ever carries an answer key.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class ReadingContentTests(BandUpTestEnvironment environment)
{
    private static readonly string[] ForbiddenFields =
    [
        "correctAnswer",
        "acceptedVariants",
        "answerKey",
        "explanation",
        "evidenceSentence",
        "misleadingSentence",
        "unspecifiedPart",
    ];

    [Fact]
    public async Task READ_01_Listing_Passages_Without_A_Session_Returns_401()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/reading/passages");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task READ_02_Listing_Passages_Shows_Published_Passages_And_Hides_Drafts()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "read02");
        ContentTestData.SeededPassage published =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "READ-02 published");
        Guid draftId = await ContentTestData.SeedDraftPassageAsync(factory.Services, user.Id, "READ-02 draft");

        using HttpRequestMessage request = ContentTestData.Get("/api/reading/passages?pageSize=100", user);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        JsonElement[] items = body.GetProperty("items").EnumerateArray().ToArray();

        JsonElement row = items.Single(item => item.GetProperty("id").GetGuid() == published.PassageId);
        row.GetProperty("title").GetString().ShouldBe("READ-02 published");
        row.GetProperty("questionCount").GetInt32().ShouldBe(2);
        items.ShouldNotContain(item => item.GetProperty("id").GetGuid() == draftId);
        body.GetProperty("page").GetInt32().ShouldBe(1);
        body.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task READ_03_Listing_Passages_Is_Paged()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "read03");
        await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "READ-03 one");
        await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "READ-03 two");

        using HttpRequestMessage request = ContentTestData.Get("/api/reading/passages?page=1&pageSize=1", user);
        HttpResponseMessage response = await client.SendAsync(request);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("items").GetArrayLength().ShouldBe(1);
        body.GetProperty("pageSize").GetInt32().ShouldBe(1);
        body.GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task READ_04_Getting_A_Passage_Returns_Its_Questions_Without_Any_Answer_Key()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "read04");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "READ-04 passage");

        using HttpRequestMessage request = ContentTestData.Get($"/api/reading/passages/{passage.PassageId}", user);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        string raw = await response.Content.ReadAsStringAsync();
        foreach (string field in ForbiddenFields)
        {
            raw.ShouldNotContain(field, Case.Insensitive);
        }

        JsonElement body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("title").GetString().ShouldBe("READ-04 passage");
        body.GetProperty("body").GetString().ShouldBe("Birds build nests in spring.");

        JsonElement[] questions = body.GetProperty("questions").EnumerateArray().ToArray();
        questions.Length.ShouldBe(2);
        questions[0].GetProperty("id").GetGuid().ShouldBe(passage.TrueFalseQuestionId);
        questions[0].GetProperty("type").GetString().ShouldBe("TrueFalseNotGiven");
        questions[1].GetProperty("id").GetGuid().ShouldBe(passage.CompletionQuestionId);
        questions[1].GetProperty("type").GetString().ShouldBe("SentenceCompletion");
    }

    [Fact]
    public async Task READ_05_Each_Question_Points_At_The_Lesson_Mapped_To_Its_Type()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "read05");
        ContentTestData.SeededPassage passage =
            await ContentTestData.SeedPublishedPassageAsync(factory.Services, user.Id, "READ-05 passage");
        Guid tfngLesson = await ContentTestData.SeedLessonAsync(
            factory.Services, "READ-05 TFNG lesson", QuestionType.TrueFalseNotGiven, 1, true, true);
        await ContentTestData.SeedLessonAsync(
            factory.Services,
            "READ-05 unpublished completion lesson",
            QuestionType.SentenceCompletion,
            1,
            false,
            true);

        using HttpRequestMessage request = ContentTestData.Get($"/api/reading/passages/{passage.PassageId}", user);
        HttpResponseMessage response = await client.SendAsync(request);

        JsonElement[] questions = (await ReadJsonAsync(response)).GetProperty("questions").EnumerateArray().ToArray();

        questions[0].GetProperty("relatedLessonId").GetGuid().ShouldBe(tfngLesson);

        // The only lesson mapped to sentence completion is unpublished, so learners get no link.
        questions[1].GetProperty("relatedLessonId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task READ_06_Getting_A_Draft_Or_Unknown_Passage_Returns_404()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        ContentTestData.TestUser user = await ContentTestData.RegisterAndLoginAsync(client, "read06");
        Guid draftId = await ContentTestData.SeedDraftPassageAsync(factory.Services, user.Id, "READ-06 draft");

        using HttpRequestMessage draftRequest = ContentTestData.Get($"/api/reading/passages/{draftId}", user);
        HttpResponseMessage draft = await client.SendAsync(draftRequest);

        using HttpRequestMessage unknownRequest = ContentTestData.Get($"/api/reading/passages/{Guid.NewGuid()}", user);
        HttpResponseMessage unknown = await client.SendAsync(unknownRequest);

        draft.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadJsonAsync(unknown)).GetProperty("code").GetString().ShouldBe("NOT_FOUND");
    }

    [Fact]
    public async Task READ_07_Getting_A_Passage_Without_A_Session_Returns_401()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/reading/passages/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
