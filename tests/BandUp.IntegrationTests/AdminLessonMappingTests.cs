using System.Net;
using System.Net.Http.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Learning;
using BandUp.Application.Features.Learning.Admin.Mappings;
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
public sealed class AdminLessonMappingTests(BandUpTestEnvironment environment)
{
    private const string Path = "/api/admin/lesson-mappings";

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Defaults_Are_Saved_Together_And_Only_Published_Matching_Lessons_Are_Selectable(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, role);
        Lesson tfng = MakeLesson(QuestionType.TrueFalseNotGiven);
        Lesson sc = MakeLesson(QuestionType.SentenceCompletion);
        Lesson draft = MakeLesson(QuestionType.TrueFalseNotGiven, publish: false);
        Lesson archived = MakeLesson(QuestionType.TrueFalseNotGiven);
        archived.Archive();
        LessonMap specific = LessonMap.Create(tfng, "mapping-test-" + Guid.NewGuid());
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            db.Lessons.AddRange(tfng, sc, draft, archived);
            db.LessonMaps.Add(specific);
            await db.SaveChangesAsync();
        }

        await SaveAsync(client, tfng.Id, sc.Id);
        await SaveAsync(client, tfng.Id, sc.Id);
        LessonMappingRow[] rows = await GetAsync(client);
        rows.Length.ShouldBe(2);
        LessonMappingRow row = rows.Single(item => item.QuestionType == tfng.QuestionType);
        row.SelectedLesson!.Id.ShouldBe(tfng.Id);
        row.IsEnabled.ShouldBeTrue();
        row.Options.ShouldContain(option => option.Id == tfng.Id);
        row.Options.ShouldNotContain(option => option.Id == sc.Id || option.Id == draft.Id || option.Id == archived.Id);

        foreach (Guid invalid in new[] { Guid.NewGuid(), draft.Id, archived.Id, sc.Id })
        {
            await SaveAsync(client, invalid, sc.Id, HttpStatusCode.BadRequest);
            (await GetAsync(client)).Single(item => item.QuestionType == tfng.QuestionType)
                .SelectedLesson!.Id.ShouldBe(tfng.Id);
        }

        // A failure in the second selection must not replace the valid first one either.
        await SaveAsync(client, null, tfng.Id, HttpStatusCode.BadRequest);
        (await GetAsync(client)).Single(item => item.QuestionType == tfng.QuestionType)
            .SelectedLesson!.Id.ShouldBe(tfng.Id);

        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            (await db.LessonMaps.CountAsync(map => map.ErrorPattern == null)).ShouldBe(2);
            (await db.LessonMaps.SingleAsync(map => map.Id == specific.Id)).LessonId.ShouldBe(tfng.Id);
        }

        // Use the existing lesson endpoint to prove that archive and the admin status agree.
        using HttpResponseMessage delete = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/lessons/{tfng.Id}") { Content = JsonContent.Create(new { }) });
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using HttpResponseMessage archive = await client.PostAsJsonAsync($"/api/admin/lessons/{tfng.Id}/archive", new { });
        archive.StatusCode.ShouldBe(HttpStatusCode.NoContent, await archive.Content.ReadAsStringAsync());
        row = (await GetAsync(client)).Single(item => item.QuestionType == tfng.QuestionType);
        row.IsEnabled.ShouldBeFalse();
        row.SelectedLesson!.Status.ShouldBe(LessonStatus.Archived);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            var defaults = await scope.ServiceProvider.GetRequiredService<ILessonQueries>()
                .GetDefaultLessonIdsAsync(Enum.GetValues<QuestionType>(), CancellationToken.None);
            defaults.ShouldNotContainKey(tfng.QuestionType);
            defaults[sc.QuestionType].ShouldBe(sc.Id);
        }

        await SaveAsync(client, null, null);
        (await GetAsync(client)).ShouldAllBe(item => item.SelectedLesson == null && !item.IsEnabled);
        await using BandUpDbContext verify = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        (await verify.LessonMaps.AnyAsync(map => map.Id == specific.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_Full_Saves_Do_Not_Mix_Selections_Or_Create_Duplicate_Defaults()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.Admin);
        Lesson firstTfng = MakeLesson(QuestionType.TrueFalseNotGiven);
        Lesson firstSc = MakeLesson(QuestionType.SentenceCompletion);
        Lesson secondTfng = MakeLesson(QuestionType.TrueFalseNotGiven);
        Lesson secondSc = MakeLesson(QuestionType.SentenceCompletion);
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            db.Lessons.AddRange(firstTfng, firstSc, secondTfng, secondSc);
            await db.SaveChangesAsync();
        }
        await SaveAsync(client, null, null);
        await Task.WhenAll(SaveAsync(client, firstTfng.Id, firstSc.Id), SaveAsync(client, secondTfng.Id, secondSc.Id));
        LessonMappingRow[] rows = await GetAsync(client);
        Guid tfng = rows.Single(row => row.QuestionType == QuestionType.TrueFalseNotGiven).SelectedLesson!.Id;
        Guid sc = rows.Single(row => row.QuestionType == QuestionType.SentenceCompletion).SelectedLesson!.Id;
        (tfng == firstTfng.Id && sc == firstSc.Id || tfng == secondTfng.Id && sc == secondSc.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_Rejects_Users_And_Requires_The_Csrf_Header()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();
        using (HttpResponseMessage anonymous = await client.PutAsJsonAsync(Path, Input(null, null)))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using HttpClient user = await ClientAsync(factory, Role.User);
        using (HttpResponseMessage denied = await user.PutAsJsonAsync(Path, Input(null, null)))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        using HttpClient admin = await ClientAsync(factory, Role.Admin);
        admin.DefaultRequestHeaders.Remove("X-Requested-With");
        using HttpResponseMessage csrf = await admin.PutAsJsonAsync(Path, Input(null, null));
        csrf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Malformed_Selections_Return_Field_Errors_Without_Changing_Defaults()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.Admin);
        await SaveAsync(client, null, null);
        object[] invalid =
        [
            new { }, new { mappings = (object?)null }, new { mappings = Array.Empty<object>() },
            new { mappings = new[] { new { questionType = "TrueFalseNotGiven", lessonId = (Guid?)null } } },
            new { mappings = new[] { new { questionType = "TrueFalseNotGiven", lessonId = (Guid?)null },
                new { questionType = "TrueFalseNotGiven", lessonId = (Guid?)null } } },
            new { mappings = new[] { new { questionType = "Unknown", lessonId = (Guid?)null },
                new { questionType = "SentenceCompletion", lessonId = (Guid?)null } } },
            Input(Guid.Empty, null),
        ];
        foreach (object input in invalid)
        {
            using HttpResponseMessage response = await client.PutAsJsonAsync(Path, input);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }
        (await GetAsync(client)).ShouldAllBe(item => !item.IsEnabled && item.SelectedLesson == null);
    }

    private static Lesson MakeLesson(QuestionType type, bool publish = true)
    {
        Lesson lesson = Lesson.CreateDraft($"Mapping {Guid.NewGuid()}", "Lesson body", type, 0);
        lesson.AddExample("Evidence", "Paraphrase", 1);
        if (publish)
        {
            lesson.Publish();
        }
        return lesson;
    }

    private static SaveLessonMappingsInput Input(Guid? tfng, Guid? sc) => new([
        new(QuestionType.TrueFalseNotGiven, tfng), new(QuestionType.SentenceCompletion, sc)]);

    private static async Task SaveAsync(HttpClient client, Guid? tfng, Guid? sc,
        HttpStatusCode expected = HttpStatusCode.NoContent)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(Path, Input(tfng, sc));
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
    }

    private static async Task<LessonMappingRow[]> GetAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<LessonMappingRow[]>(Path, JsonSerialization.Options))!;

    private async Task<HttpClient> ClientAsync(BandUpApiFactory factory, Role role)
    {
        User actor = User.Create($"mapping-{Guid.NewGuid()}@example.test", "Mapper", "test-hash", DateTimeOffset.UtcNow);
        actor.ChangeRole(role);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actor.Id, role.ToString(), DateTimeOffset.UtcNow).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }
}
