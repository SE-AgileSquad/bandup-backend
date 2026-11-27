using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Content.Admin;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminPassageEditorTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Editor_Reports_All_Blockers_And_Requires_Final_Review_Before_Publication(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, role);
        string passage = await CreateAsync(client, "/api/admin/passages", PassageInput());
        string question = await CreateAsync(client, $"{passage}/questions",
            new { type = "TrueFalseNotGiven", prompt = "Every garden received funding.", sortOrder = 1 });
        Guid questionId = Guid.Parse(question.Split('/')[^1]);
        string fieldPrefix = $"questions.{questionId}.";
        await ChangeAsync(client, HttpMethod.Put, $"{question}/answer-key", new { correctAnswer = "False" });
        await ChangeAsync(client, HttpMethod.Put, $"{question}/explanation", new { explanation = "" });

        PassageDetails draft = await DetailsAsync(client, passage);
        draft.IsReadOnly.ShouldBeFalse();
        draft.CanCreateRevision.ShouldBeFalse();
        draft.WordCount.ShouldBe(4);
        draft.PublishReadiness.IsReady.ShouldBeFalse();
        draft.PublishReadiness.Errors.Keys.ShouldContain($"{fieldPrefix}explanation");
        draft.PublishReadiness.Errors.Keys.ShouldContain($"{fieldPrefix}evidenceSentence");
        draft.PublishReadiness.Errors.Keys.ShouldContain($"{fieldPrefix}answerKey.isVerified");
        DateTimeOffset? beforeFailure = draft.UpdatedAt;

        JsonElement blocked = await ConflictAsync(client, HttpMethod.Post, $"{passage}/publish", new { });
        JsonElement errors = blocked.GetProperty("errors");
        errors.TryGetProperty("reviewConfirmed", out _).ShouldBeTrue();
        errors.TryGetProperty($"{fieldPrefix}explanation", out _).ShouldBeTrue();
        errors.TryGetProperty($"{fieldPrefix}evidenceSentence", out _).ShouldBeTrue();
        errors.TryGetProperty($"{fieldPrefix}answerKey.isVerified", out _).ShouldBeTrue();
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(beforeFailure);

        JsonElement unverified = await ConflictAsync(client, HttpMethod.Post, $"{question}/answer-key/verify", new { });
        unverified.GetProperty("errors").TryGetProperty($"{fieldPrefix}evidenceSentence", out _).ShouldBeTrue();
        await ChangeAsync(client, HttpMethod.Put, $"{question}/explanation",
            new { explanation = "Funding is not universal.", evidenceSentence = "Some gardens received funding." });
        await ChangeAsync(client, HttpMethod.Post, $"{question}/answer-key/verify", new { });
        PassageDetails ready = await DetailsAsync(client, passage);
        ready.PublishReadiness.IsReady.ShouldBeTrue();
        ready.Questions.Single().AnswerKey!.IsVerified.ShouldBeTrue();

        JsonElement reviewRequired = await ConflictAsync(client, HttpMethod.Post, $"{passage}/publish", new { reviewConfirmed = false });
        reviewRequired.GetProperty("errors").EnumerateObject().Single().Name.ShouldBe("reviewConfirmed");
        await ChangeAsync(client, HttpMethod.Post, $"{passage}/publish", new { reviewConfirmed = true });
        PassageDetails published = await DetailsAsync(client, passage);
        published.Status.ShouldBe(PassageStatus.Published);
        published.IsReadOnly.ShouldBeTrue();
        published.CanCreateRevision.ShouldBeTrue();

        // Every editing entry point must respect the published aggregate's read-only state.
        (HttpMethod Method, string Path, object Input)[] edits =
        [
            (HttpMethod.Put, passage, PassageInput()),
            (HttpMethod.Post, $"{passage}/questions", new { type = "SentenceCompletion", prompt = "A ___", sortOrder = 2 }),
            (HttpMethod.Put, question, new { prompt = "Another claim.", sortOrder = 2 }),
            (HttpMethod.Delete, question, new { }),
            (HttpMethod.Put, $"{question}/explanation", new { explanation = "Changed" }),
            (HttpMethod.Put, $"{question}/answer-key", new { correctAnswer = "True" }),
            (HttpMethod.Delete, $"{question}/answer-key", new { }),
            (HttpMethod.Post, $"{question}/answer-key/verify", new { }),
            (HttpMethod.Delete, $"{question}/answer-key/verify", new { }),
        ];
        foreach (var edit in edits)
        {
            await ConflictAsync(client, edit.Method, edit.Path, edit.Input);
        }

        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(published.UpdatedAt);
        // Duplicate as draft retains text and explanations, but the new version must be checked again.
        string revision = await CreateAsync(client, $"{passage}/revisions", new { });
        PassageDetails copy = await DetailsAsync(client, revision);
        copy.Status.ShouldBe(PassageStatus.Draft);
        copy.IsReadOnly.ShouldBeFalse();
        copy.PreviousVersionId.ShouldBe(published.Id);
        copy.Questions.Single().Id.ShouldNotBe(questionId);
        copy.Questions.Single().Explanation.ShouldBe(ready.Questions.Single().Explanation);
        copy.Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
        (await DetailsAsync(client, passage)).CanCreateRevision.ShouldBeFalse();
        await ChangeAsync(client, HttpMethod.Post, $"{passage}/publish", new { });
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(published.UpdatedAt);
    }

    [Fact]
    public async Task Draft_Can_Clear_Content_And_Toggle_Verification_Without_Losing_Other_Fields()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        string passage = await CreateAsync(client, "/api/admin/passages", PassageInput());
        string question = await CreateAsync(client, $"{passage}/questions",
            new { type = "TrueFalseNotGiven", prompt = "All gardens received funding.", sortOrder = 1 });
        Guid questionId = Guid.Parse(question.Split('/')[^1]);
        await ChangeAsync(client, HttpMethod.Put, $"{question}/answer-key", new { correctAnswer = "Not Given" });
        await ChangeAsync(client, HttpMethod.Put, $"{question}/explanation", new
        {
            explanation = "The source does not address funding.",
            misleadingSentence = "Some gardens received funding.",
            unspecifiedPart = "Whether all gardens received funding.",
        });
        await ChangeAsync(client, HttpMethod.Post, $"{question}/answer-key/verify", new { });
        PassageDetails verified = await DetailsAsync(client, passage);
        verified.PublishReadiness.IsReady.ShouldBeTrue();
        await ChangeAsync(client, HttpMethod.Put, $"{question}/answer-key", new { correctAnswer = " not given " });
        await ChangeAsync(client, HttpMethod.Put, $"{question}/explanation", new
        {
            explanation = "The source does not address funding.",
            misleadingSentence = "Some gardens received funding.",
            unspecifiedPart = "Whether all gardens received funding.",
        });
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(verified.UpdatedAt);

        await ChangeAsync(client, HttpMethod.Delete, $"{question}/answer-key/verify", new { });
        PassageDetails uncheckedDetails = await DetailsAsync(client, passage);
        uncheckedDetails.Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
        uncheckedDetails.PublishReadiness.Errors.Count.ShouldBe(1);
        await ChangeAsync(client, HttpMethod.Post, $"{question}/answer-key/verify", new { });
        await ChangeAsync(client, HttpMethod.Put, $"{question}/explanation", new { explanation = (string?)null });
        PassageDetails clearedExplanation = await DetailsAsync(client, passage);
        clearedExplanation.Questions.Single().Explanation.ShouldBeNull();
        clearedExplanation.Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
        clearedExplanation.PublishReadiness.Errors.Keys.ShouldContain($"questions.{questionId}.unspecifiedPart");

        await ChangeAsync(client, HttpMethod.Delete, $"{question}/answer-key", new { });
        PassageDetails noKey = await DetailsAsync(client, passage);
        noKey.Questions.Single().AnswerKey.ShouldBeNull();
        noKey.Questions.Single().Prompt.ShouldBe("All gardens received funding.");
        noKey.PublishReadiness.Errors.Keys.ShouldContain($"questions.{questionId}.answerKey.correctAnswer");
        await ChangeAsync(client, HttpMethod.Delete, $"{question}/answer-key", new { });
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(noKey.UpdatedAt);
        await ChangeAsync(client, HttpMethod.Put, $"{question}/answer-key", new { correctAnswer = "False" });
        await ChangeAsync(client, HttpMethod.Put, $"{question}/explanation",
            new { explanation = "Not every garden was funded.", evidenceSentence = "Some gardens received funding." });
        await ChangeAsync(client, HttpMethod.Post, $"{question}/answer-key/verify", new { });
        (await DetailsAsync(client, passage)).PublishReadiness.IsReady.ShouldBeTrue();

        await ChangeAsync(client, HttpMethod.Put, passage, new
        {
            title = "Gardens", body = "Updated original text.", sourceNote = "BandUp", usageBasis = "Original content",
        });
        (await DetailsAsync(client, passage)).Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
    }

    private static object PassageInput() =>
        new { title = "Gardens", body = "Some gardens received funding.", sourceNote = "BandUp", usageBasis = "Original content" };

    private async Task<HttpClient> CreateClientAsync(BandUpApiFactory factory, Role role)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User actor = User.Create($"{Guid.NewGuid():N}@example.test", "Passage editor", "test-hash", now);
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

    private static async Task<PassageDetails> DetailsAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<PassageDetails>(path, JsonSerialization.Options))!;

    private static async Task<string> CreateAsync(HttpClient client, string path, object input)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, input);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }

    private static async Task ChangeAsync(HttpClient client, HttpMethod method, string path, object input)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(input) };
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ConflictAsync(HttpClient client, HttpMethod method, string path, object input)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(input) };
        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, body);
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
