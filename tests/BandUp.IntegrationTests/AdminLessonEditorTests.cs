using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Learning.Admin;
using BandUp.Domain.Content;
using BandUp.Domain.Identity;
using BandUp.Domain.Learning;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminLessonEditorTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Editors_Can_Manage_A_Lesson_And_Its_Examples_Through_Publication_And_Archive(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, role);
        string lesson = await CreateAsync(client, "/api/admin/lessons", new
        {
            title = "False vs Not Given", body = "Compare claims with the source.", questionType = "TrueFalseNotGiven", sortOrder = 10,
        });
        LessonDetails draft = await DetailsAsync(client, lesson);
        draft.IsReadOnly.ShouldBeFalse();
        draft.CanChangeQuestionType.ShouldBeTrue();
        draft.ExampleCount.ShouldBe(0);
        draft.PublishReadiness.IsReady.ShouldBeFalse();
        draft.PublishReadiness.Errors.Keys.ShouldContain("examples");
        JsonElement emptyPublication = await ProblemAsync(client, HttpMethod.Post, $"{lesson}/publish", new { }, HttpStatusCode.Conflict);
        emptyPublication.GetProperty("errors").TryGetProperty("examples", out _).ShouldBeTrue();

        await ChangeAsync(client, HttpMethod.Put, lesson, new
        {
            title = "Completion", body = "Find the missing phrase.", sortOrder = 20, questionType = "SentenceCompletion",
        });
        (await DetailsAsync(client, lesson)).QuestionType.ShouldBe(QuestionType.SentenceCompletion);
        string first = await CreateAsync(client, $"{lesson}/examples",
            new { evidenceSentence = "Birds build nests.", paraphrase = "Nest building", sortOrder = 10 });
        string second = await CreateAsync(client, $"{lesson}/examples",
            new { evidenceSentence = "Birds return in spring.", paraphrase = "Spring return", sortOrder = 20 });
        Guid firstId = Guid.Parse(first.Split('/')[^1]);
        Guid secondId = Guid.Parse(second.Split('/')[^1]);
        await ChangeAsync(client, HttpMethod.Put, first,
            new { evidenceSentence = "Birds build nests on roofs.", paraphrase = "Rooftop nests", sortOrder = 10 });
        LessonExampleDetails edited = (await client.GetFromJsonAsync<LessonExampleDetails>(first))!;
        edited.Paraphrase.ShouldBe("Rooftop nests");
        await ChangeAsync(client, HttpMethod.Put, $"{lesson}/examples/order", new { exampleIds = new[] { secondId, firstId } });
        LessonExampleDetails[] examples = (await client.GetFromJsonAsync<LessonExampleDetails[]>($"{lesson}/examples"))!;
        examples.Select(example => example.Id).ShouldBe(new[] { secondId, firstId });
        examples.Select(example => example.SortOrder).ShouldBe(new[] { 1, 2 });
        LessonDetails ready = await DetailsAsync(client, lesson);
        ready.PublishReadiness.IsReady.ShouldBeTrue();
        ready.HasPractice.ShouldBeTrue();
        ready.ExampleCount.ShouldBe(2);
        await ChangeAsync(client, HttpMethod.Post, $"{lesson}/publish", new { });
        await ChangeAsync(client, HttpMethod.Post, $"{lesson}/publish", new { });
        LessonDetails published = await DetailsAsync(client, lesson);
        published.Status.ShouldBe(LessonStatus.Published);
        published.IsReadOnly.ShouldBeFalse();
        published.CanChangeQuestionType.ShouldBeFalse();

        await ProblemAsync(client, HttpMethod.Put, lesson,
            new { title = "Rejected", body = "Rejected", sortOrder = 99, questionType = "TrueFalseNotGiven" }, HttpStatusCode.Conflict);
        (await DetailsAsync(client, lesson)).Title.ShouldBe("Completion");
        // Old clients can omit questionType: the saved type is preserved.
        await ChangeAsync(client, HttpMethod.Put, lesson, new { title = "Updated completion", body = "Updated lesson.", sortOrder = 3 });
        (await DetailsAsync(client, lesson)).QuestionType.ShouldBe(QuestionType.SentenceCompletion);
        await ChangeAsync(client, HttpMethod.Put, second,
            new { evidenceSentence = "Updated evidence", paraphrase = "Updated paraphrase", sortOrder = 1 });
        await ChangeAsync(client, HttpMethod.Delete, first, new { });
        await ProblemAsync(client, HttpMethod.Delete, second, new { }, HttpStatusCode.Conflict);
        await ChangeAsync(client, HttpMethod.Post, $"{lesson}/archive", new { });
        await ChangeAsync(client, HttpMethod.Post, $"{lesson}/archive", new { });
        LessonDetails archived = await DetailsAsync(client, lesson);
        archived.IsReadOnly.ShouldBeTrue();
        archived.Status.ShouldBe(LessonStatus.Archived);
        archived.ExampleCount.ShouldBe(1);

        (HttpMethod Method, string Path, object Body)[] blocked =
        [
            (HttpMethod.Put, lesson, new { title = "Changed", body = "Changed", sortOrder = 100 }),
            (HttpMethod.Post, $"{lesson}/examples", new { evidenceSentence = "New", paraphrase = "New", sortOrder = 2 }),
            (HttpMethod.Put, second, new { evidenceSentence = "Changed", paraphrase = "Changed", sortOrder = 10 }),
            (HttpMethod.Delete, second, new { }),
            (HttpMethod.Put, $"{lesson}/examples/order", new { exampleIds = new[] { secondId } }),
            (HttpMethod.Post, $"{lesson}/publish", new { }),
        ];
        foreach (var change in blocked)
        {
            await ProblemAsync(client, change.Method, change.Path, change.Body, HttpStatusCode.Conflict);
        }

        LessonDetails unchanged = await DetailsAsync(client, lesson);
        unchanged.Title.ShouldBe(archived.Title);
        unchanged.Examples.Single().ShouldBe(archived.Examples.Single());
        await ChangeAsync(client, HttpMethod.Delete, lesson, new { });
        using HttpResponseMessage deleted = await client.GetAsync(lesson);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Example_Membership_And_Invalid_Reorders_Are_Checked_Without_Partial_Changes()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        string lesson = await CreateLessonAsync(client);
        string other = await CreateLessonAsync(client);
        string first = await CreateAsync(client, $"{lesson}/examples",
            new { evidenceSentence = "First", paraphrase = "First paraphrase", sortOrder = 10 });
        string second = await CreateAsync(client, $"{lesson}/examples",
            new { evidenceSentence = "Second", paraphrase = "Second paraphrase", sortOrder = 20 });
        string foreign = await CreateAsync(client, $"{other}/examples",
            new { evidenceSentence = "Other", paraphrase = "Other paraphrase", sortOrder = 1 });
        Guid firstId = Guid.Parse(first.Split('/')[^1]);
        Guid secondId = Guid.Parse(second.Split('/')[^1]);
        Guid foreignId = Guid.Parse(foreign.Split('/')[^1]);
        object[] invalidOrders =
        [
            new { exampleIds = new[] { firstId, firstId } },
            new { exampleIds = new[] { firstId, foreignId } },
            new { exampleIds = new[] { secondId } },
            new { exampleIds = new[] { Guid.Empty, firstId } },
            new { exampleIds = (Guid[]?)null },
        ];
        foreach (object input in invalidOrders)
        {
            JsonElement problem = await ProblemAsync(client, HttpMethod.Put, $"{lesson}/examples/order", input, HttpStatusCode.BadRequest);
            problem.GetProperty("errors").TryGetProperty("exampleIds", out _).ShouldBeTrue();
            LessonDetails unchanged = await DetailsAsync(client, lesson);
            unchanged.Examples.Select(example => example.SortOrder).ShouldBe(new[] { 10, 20 });
        }

        await ProblemAsync(client, HttpMethod.Put, $"{other}/examples/{firstId}",
            new { evidenceSentence = "Changed", paraphrase = "Changed", sortOrder = 2 }, HttpStatusCode.NotFound);
        await ProblemAsync(client, HttpMethod.Delete, $"{other}/examples/{firstId}", new { }, HttpStatusCode.NotFound);
        using (HttpResponseMessage hidden = await client.GetAsync($"{other}/examples/{firstId}"))
        {
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (HttpResponseMessage missingCollection = await client.GetAsync($"/api/admin/lessons/{Guid.NewGuid()}/examples"))
        {
            missingCollection.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await ChangeAsync(client, HttpMethod.Delete, first, new { });
        await ChangeAsync(client, HttpMethod.Delete, second, new { });
        (await client.GetFromJsonAsync<LessonExampleDetails[]>($"{lesson}/examples"))!.ShouldBeEmpty();
        (await DetailsAsync(client, lesson)).PublishReadiness.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task A_Mapped_Lessons_Type_And_Referenced_Record_Are_Protected()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await CreateClientAsync(factory, Role.Admin);
        string path = await CreateLessonAsync(client);
        Guid id = Guid.Parse(path.Split('/')[^1]);
        string pattern = $"editor-test-{Guid.NewGuid():N}";
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            BandUpDbContext db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            Lesson lesson = await db.Lessons.SingleAsync(item => item.Id == id);
            db.LessonMaps.Add(LessonMap.Create(lesson, pattern));
            await db.SaveChangesAsync();
        }

        (await DetailsAsync(client, path)).CanChangeQuestionType.ShouldBeFalse();
        await ProblemAsync(client, HttpMethod.Put, path,
            new { title = "Changed", body = "Changed", sortOrder = 50, questionType = "SentenceCompletion" }, HttpStatusCode.Conflict);
        LessonDetails original = await DetailsAsync(client, path);
        original.QuestionType.ShouldBe(QuestionType.TrueFalseNotGiven);
        original.Title.ShouldBe("TFNG");
        await ChangeAsync(client, HttpMethod.Put, path,
            new { title = "Updated title", body = "Updated body", sortOrder = 2, questionType = "TrueFalseNotGiven" });
        await ProblemAsync(client, HttpMethod.Delete, path, new { }, HttpStatusCode.Conflict);
        (await DetailsAsync(client, path)).Title.ShouldBe("Updated title");
    }

    private static Task<string> CreateLessonAsync(HttpClient client) => CreateAsync(client, "/api/admin/lessons", new
    {
        title = "TFNG", body = "Lesson body", questionType = "TrueFalseNotGiven", sortOrder = 1,
    });

    private async Task<HttpClient> CreateClientAsync(BandUpApiFactory factory, Role role)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User actor = User.Create($"{Guid.NewGuid():N}@example.test", "Lesson editor", "test-hash", now);
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

    private static async Task<LessonDetails> DetailsAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<LessonDetails>(path, JsonSerialization.Options))!;

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

    private static async Task<JsonElement> ProblemAsync(HttpClient client, HttpMethod method, string path, object input, HttpStatusCode expected)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(input) };
        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
