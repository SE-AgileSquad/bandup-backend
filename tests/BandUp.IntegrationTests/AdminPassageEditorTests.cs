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
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminPassageEditorTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task One_Request_Saves_Question_Content_And_Review_And_Publishing_Still_Requires_Final_Confirmation(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, role);
        string passage = await CreateAsync(client, "/api/admin/passages", PassageInput());
        string question = await CreateAsync(client, $"{passage}/questions", new
        {
            type = "TrueFalseNotGiven", prompt = "All gardens received funding.", sortOrder = 1,
            correctAnswer = "False", explanation = "Funding was not universal.",
            evidenceSentence = "Some gardens received funding.", isVerified = false,
        });
        PassageDetails draft = await DetailsAsync(client, passage);
        var saved = draft.Questions.Single();
        saved.AnswerKey!.CorrectAnswer.ShouldBe("False");
        saved.Explanation.ShouldBe("Funding was not universal.");
        saved.AnswerKey.IsVerified.ShouldBeFalse();
        draft.PublishReadiness.IsReady.ShouldBeFalse();
        JsonElement blocked = await ConflictAsync(client, HttpMethod.Post, $"{passage}/publish", new { reviewConfirmed = true });
        blocked.GetProperty("errors").EnumerateObject().ShouldContain(item => item.Name.EndsWith("answerKey.isVerified", StringComparison.Ordinal));
        await ChangeAsync(client, HttpMethod.Put, question, FullQuestion() with { IsVerified = true });
        PassageDetails ready = await DetailsAsync(client, passage);
        ready.PublishReadiness.IsReady.ShouldBeTrue();
        ready.Questions.Single().AnswerKey!.VerifiedBy.ShouldNotBeNull();
        JsonElement review = await ConflictAsync(client, HttpMethod.Post, $"{passage}/publish", new { reviewConfirmed = false });
        review.GetProperty("errors").EnumerateObject().Single().Name.ShouldBe("reviewConfirmed");
        await ChangeAsync(client, HttpMethod.Post, $"{passage}/publish", new { reviewConfirmed = true });
        PassageDetails published = await DetailsAsync(client, passage);
        published.IsReadOnly.ShouldBeTrue();
        await ConflictAsync(client, HttpMethod.Put, question, FullQuestion());
        await ConflictAsync(client, HttpMethod.Delete, question, new { });
        await ConflictAsync(client, HttpMethod.Post, $"{passage}/questions", new { type = "SentenceCompletion", prompt = "A ___", sortOrder = 2 });
        await ConflictAsync(client, HttpMethod.Put, passage, PassageInput());
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(published.UpdatedAt);
        string revision = await CreateAsync(client, $"{passage}/revisions", new { });
        PassageDetails copy = await DetailsAsync(client, revision);
        copy.PreviousVersionId.ShouldBe(published.Id);
        copy.Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
        copy.Questions.Single().Explanation.ShouldBe(ready.Questions.Single().Explanation);
        copy.Questions.Single().Id.ShouldNotBe(saved.Id);
    }

    [Fact]
    public async Task Full_Replacement_Can_Clear_Answers_And_Explanations_And_Toggle_Verification()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        string passage = await CreateAsync(client, "/api/admin/passages", PassageInput());
        string question = await CreateAsync(client, $"{passage}/questions", new
        {
            type = "TrueFalseNotGiven", prompt = "All gardens received funding.", sortOrder = 1,
            correctAnswer = "Not Given", explanation = "The source does not state this.",
            unspecifiedPart = "Whether every garden was funded.", isVerified = true,
        });
        var input = FullQuestion() with { CorrectAnswer = "Not Given", Explanation = "The source does not state this.",
            EvidenceSentence = null, UnspecifiedPart = "Whether every garden was funded.", IsVerified = true };
        PassageDetails before = await DetailsAsync(client, passage);
        await ChangeAsync(client, HttpMethod.Put, question, input with { CorrectAnswer = " not given " });
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(before.UpdatedAt);
        await ChangeAsync(client, HttpMethod.Put, question, input with { IsVerified = false });
        (await DetailsAsync(client, passage)).Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
        await ChangeAsync(client, HttpMethod.Put, question, input);
        await ChangeAsync(client, HttpMethod.Put, question, input with { Explanation = null, UnspecifiedPart = null, IsVerified = false });
        var cleared = (await DetailsAsync(client, passage)).Questions.Single();
        cleared.Explanation.ShouldBeNull();
        cleared.UnspecifiedPart.ShouldBeNull();
        cleared.AnswerKey!.IsVerified.ShouldBeFalse();
        // Omitted content fields in PUT clear all answer/explanation data.
        await ChangeAsync(client, HttpMethod.Put, question, new { prompt = input.Prompt, sortOrder = 1 });
        PassageDetails noKey = await DetailsAsync(client, passage);
        noKey.Questions.Single().AnswerKey.ShouldBeNull();
        await ChangeAsync(client, HttpMethod.Put, question, new { prompt = input.Prompt, sortOrder = 1 });
        (await DetailsAsync(client, passage)).UpdatedAt.ShouldBe(noKey.UpdatedAt);
        await ChangeAsync(client, HttpMethod.Put, question, FullQuestion() with { IsVerified = true });
        await ChangeAsync(client, HttpMethod.Put, passage, new
        {
            title = "Gardens", body = "Updated source text.", sourceNote = "BandUp", usageBasis = "Original content",
        });
        (await DetailsAsync(client, passage)).Questions.Single().AnswerKey!.IsVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_Answers_Or_Incomplete_Review_Roll_Back_The_Whole_Create_And_Edit()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        string passage = await CreateAsync(client, "/api/admin/passages", PassageInput());
        PassageDetails original = await DetailsAsync(client, passage);
        using (HttpResponseMessage failed = await client.PostAsJsonAsync($"{passage}/questions", new
        {
            type = "TrueFalseNotGiven", prompt = "Invalid new question", sortOrder = 1, correctAnswer = "Maybe",
        }))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        await ConflictAsync(client, HttpMethod.Post, $"{passage}/questions", new
        {
            type = "TrueFalseNotGiven", prompt = "Incomplete new question", sortOrder = 1,
            correctAnswer = "True", isVerified = true,
        });
        PassageDetails unchanged = await DetailsAsync(client, passage);
        unchanged.Questions.ShouldBeEmpty();
        unchanged.UpdatedAt.ShouldBe(original.UpdatedAt);
        string question = await CreateAsync(client, $"{passage}/questions", new
        {
            type = "SentenceCompletion", prompt = "Gardens received ___", sortOrder = 1, correctAnswer = "funding",
            acceptedVariants = new[] { "financial support" }, explanation = "Funding is mentioned.",
            evidenceSentence = "Some gardens received funding.", isVerified = true,
        });
        PassageDetails saved = await DetailsAsync(client, passage);
        var prior = saved.Questions.Single();
        await ConflictAsync(client, HttpMethod.Put, question, FullQuestion() with
        {
            Prompt = "Changed prompt", CorrectAnswer = "new answer", Explanation = null, IsVerified = true,
        });
        PassageDetails after = await DetailsAsync(client, passage);
        after.UpdatedAt.ShouldBe(saved.UpdatedAt);
        after.Questions.Single().Prompt.ShouldBe(prior.Prompt);
        after.Questions.Single().AnswerKey!.CorrectAnswer.ShouldBe("funding");
        after.Questions.Single().AnswerKey!.AcceptedVariants.ShouldBe(new[] { "financial support" });
        after.Questions.Single().AnswerKey!.VerifiedAt.ShouldBe(prior.AnswerKey!.VerifiedAt);
        string other = await CreateAsync(client, "/api/admin/passages", PassageInput());
        using HttpResponseMessage wrongParent = await client.PutAsJsonAsync($"{other}/questions/{prior.Id}", FullQuestion());
        wrongParent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Old_Answer_And_Explanation_Routes_Are_Not_Exposed()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        JsonElement swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        swagger.GetProperty("paths").EnumerateObject().ShouldNotContain(path => path.Name.Contains("/answer-key", StringComparison.Ordinal)
            || path.Name.EndsWith("/explanation", StringComparison.Ordinal));
        foreach (string suffix in new[] { "answer-key", "answer-key/verify", "explanation" })
        {
            using HttpResponseMessage response = await client.PutAsJsonAsync(
                $"/api/admin/passages/{Guid.NewGuid()}/questions/{Guid.NewGuid()}/{suffix}", new { });
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Invalid_Variants_Do_Not_Change_Content_And_Question_Deletion_Removes_Its_Answer()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        string passage = await CreateAsync(client, "/api/admin/passages", PassageInput());
        string question = await CreateAsync(client, $"{passage}/questions", new
        {
            type = "TrueFalseNotGiven", prompt = "All gardens received funding.", sortOrder = 1,
            correctAnswer = "False", explanation = "Funding was not universal.",
            evidenceSentence = "Some gardens received funding.", isVerified = true,
        });
        PassageDetails before = await DetailsAsync(client, passage);
        var original = before.Questions.Single();
        var invalid = new[]
        {
            FullQuestion() with { Prompt = "Changed", AcceptedVariants = ["False"] },
            FullQuestion() with { Prompt = "Changed", CorrectAnswer = null, AcceptedVariants = ["Variant"] },
            FullQuestion() with { Prompt = "Changed", AcceptedVariants = [null!] },
        };
        foreach (var input in invalid)
        {
            using HttpResponseMessage response = await client.PutAsJsonAsync(question, input);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            PassageDetails after = await DetailsAsync(client, passage);
            after.UpdatedAt.ShouldBe(before.UpdatedAt);
            after.Questions.Single().Prompt.ShouldBe(original.Prompt);
            after.Questions.Single().AnswerKey!.IsVerified.ShouldBeTrue();
            after.Questions.Single().AnswerKey!.VerifiedAt.ShouldBe(original.AnswerKey!.VerifiedAt);
        }

        await ChangeAsync(client, HttpMethod.Delete, question, new { });
        using HttpResponseMessage missing = await client.GetAsync(question);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        (await db.Questions.AnyAsync(item => item.Id == original.Id)).ShouldBeFalse();
        (await db.AnswerKeys.AnyAsync(key => key.QuestionId == original.Id)).ShouldBeFalse();
    }

    private static BandUp.Application.Features.Content.Admin.EditQuestion.QuestionEditInput FullQuestion() =>
        new("All gardens received funding.", 1, "False", [], "Funding was not universal.", "Some gardens received funding.");

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
