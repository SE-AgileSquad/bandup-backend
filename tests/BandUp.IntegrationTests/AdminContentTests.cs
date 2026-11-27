using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminContentTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task A_Passage_Requires_Verified_Keys_And_Preserves_Published_Versions()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateAdminClientAsync(factory);
        const string body = "The birds build their nests on rooftops.";
        object input = new { title = "Birds", body, sourceNote = "BandUp", usageBasis = "Original" };
        string passage = await CreateAsync(client, "/api/admin/passages", input);
        string question = await CreateAsync(client, $"{passage}/questions",
            new { type = "SentenceCompletion", prompt = "The birds build ___ on rooftops.", sortOrder = 1 });

        object reviewed = new { prompt = "The birds build ___ on rooftops.", sortOrder = 1,
            correctAnswer = "their nests", acceptedVariants = new[] { "nests" },
            explanation = "The text names the place.", evidenceSentence = body, isVerified = true };
        await ExpectAsync(client, HttpMethod.Put, question, HttpStatusCode.NoContent,
            new { prompt = "The birds build ___ on rooftops.", sortOrder = 1,
                correctAnswer = "their nests", acceptedVariants = new[] { "nests" },
                explanation = "The text names the place.", evidenceSentence = body });
        await ExpectAsync(client, HttpMethod.Post, $"{passage}/publish", HttpStatusCode.Conflict);
        await ExpectStatusAsync(client, passage, "Draft");

        await ExpectAsync(client, HttpMethod.Put, question, HttpStatusCode.NoContent, reviewed);
        await ExpectAsync(client, HttpMethod.Put, passage, HttpStatusCode.NoContent,
            new { title = "Birds", body = $"{body} They live in cities.", sourceNote = "BandUp", usageBasis = "Original" });
        await ExpectAsync(client, HttpMethod.Post, $"{passage}/publish", HttpStatusCode.Conflict);
        await ExpectAsync(client, HttpMethod.Put, question, HttpStatusCode.NoContent, reviewed);
        await ExpectAsync(client, HttpMethod.Post, $"{passage}/publish", HttpStatusCode.NoContent,
            new { reviewConfirmed = true });
        await ExpectStatusAsync(client, passage, "Published");
        await ExpectAsync(client, HttpMethod.Put, passage, HttpStatusCode.Conflict, input);

        string revision = await CreateAsync(client, $"{passage}/revisions", new { });
        await ExpectAsync(client, HttpMethod.Post, $"{passage}/revisions", HttpStatusCode.Conflict);
        JsonElement revisionDetails = await client.GetFromJsonAsync<JsonElement>(revision);
        JsonElement copiedQuestion = revisionDetails.GetProperty("questions")[0];
        Guid copiedQuestionId = copiedQuestion.GetProperty("id").GetGuid();
        JsonElement copiedKey = copiedQuestion.GetProperty("answerKey");
        copiedKey.GetProperty("acceptedVariants")[0].GetString().ShouldBe("nests");
        copiedKey.GetProperty("verifiedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        await ExpectStatusAsync(client, passage, "Published");

        await ExpectAsync(client, HttpMethod.Put,
            $"{revision}/questions/{copiedQuestionId}", HttpStatusCode.NoContent, reviewed);
        await ExpectAsync(client, HttpMethod.Post, $"{revision}/publish", HttpStatusCode.NoContent,
            new { reviewConfirmed = true });
        await ExpectStatusAsync(client, passage, "Archived");
        await ExpectStatusAsync(client, revision, "Published");
    }

    [Fact]
    public async Task Draft_Questions_And_Passages_Can_Be_Deleted()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateAdminClientAsync(factory);
        object input = new { title = "Draft", body = "Draft body", sourceNote = "BandUp", usageBasis = "Original" };
        string passage = await CreateAsync(client, "/api/admin/passages", input);
        string otherPassage = await CreateAsync(client, "/api/admin/passages", input);
        string question = await CreateAsync(client, $"{passage}/questions",
            new { type = "TrueFalseNotGiven", prompt = "A claim.", sortOrder = 1 });

        await ExpectAsync(client, HttpMethod.Put, question, HttpStatusCode.NoContent,
            new { prompt = "An updated claim.", sortOrder = 2, correctAnswer = "Not Given" });
        string questionId = question.Split('/')[^1];
        await ExpectAsync(client, HttpMethod.Delete, $"{otherPassage}/questions/{questionId}", HttpStatusCode.NotFound);
        await ExpectAsync(client, HttpMethod.Delete, question, HttpStatusCode.NoContent);
        await ExpectAsync(client, HttpMethod.Get, question, HttpStatusCode.NotFound);
        await ExpectAsync(client, HttpMethod.Delete, passage, HttpStatusCode.NoContent);
        await ExpectAsync(client, HttpMethod.Get, passage, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lesson_Examples_Are_Editable_And_The_Last_Published_Example_Is_Protected()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateAdminClientAsync(factory);
        string lesson = await CreateAsync(client, "/api/admin/lessons",
            new { title = "TFNG", body = "Lesson body", questionType = "TrueFalseNotGiven", sortOrder = 1 });

        await ExpectAsync(client, HttpMethod.Post, $"{lesson}/publish", HttpStatusCode.Conflict);
        string example = await CreateAsync(client, $"{lesson}/examples",
            new { evidenceSentence = "Evidence", paraphrase = "Paraphrase", sortOrder = 1 });
        await ExpectAsync(client, HttpMethod.Put, lesson, HttpStatusCode.NoContent,
            new { title = "Updated TFNG", body = "Updated body", sortOrder = 2 });
        await ExpectAsync(client, HttpMethod.Put, example, HttpStatusCode.NoContent,
            new { evidenceSentence = "New evidence", paraphrase = "New paraphrase", sortOrder = 2 });

        await ExpectAsync(client, HttpMethod.Post, $"{lesson}/publish", HttpStatusCode.NoContent);
        JsonElement details = await client.GetFromJsonAsync<JsonElement>(lesson);
        details.GetProperty("status").GetString().ShouldBe("Published");
        details.GetProperty("examples")[0].GetProperty("paraphrase").GetString().ShouldBe("New paraphrase");
        await ExpectAsync(client, HttpMethod.Delete, example, HttpStatusCode.Conflict);
        await ExpectAsync(client, HttpMethod.Delete, lesson, HttpStatusCode.NoContent);
        await ExpectAsync(client, HttpMethod.Get, lesson, HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> CreateAdminClientAsync(BandUpApiFactory factory)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User admin = User.Create($"{Guid.NewGuid():N}@example.test", "Admin", "test-hash", now);
        admin.ChangeRole(Role.Admin);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        HttpClient client = factory.CreateClient();
        AccessToken token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(admin.Id, nameof(Role.Admin), now);
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token.Value}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static async Task<string> CreateAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }

    private static async Task ExpectAsync(
        HttpClient client, HttpMethod method, string path, HttpStatusCode status, object? body = null)
    {
        using HttpRequestMessage request = new(method, path);
        if (method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(body ?? new { });
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
    }

    private static async Task ExpectStatusAsync(HttpClient client, string path, string status)
    {
        JsonElement resource = await client.GetFromJsonAsync<JsonElement>(path);
        resource.GetProperty("status").GetString().ShouldBe(status);
    }
}
