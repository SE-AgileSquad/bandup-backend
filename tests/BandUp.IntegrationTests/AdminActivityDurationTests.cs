using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Domain.Identity;
using BandUp.Domain.Content;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminActivityDurationTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task Migration_Backfills_Existing_Content()
    {
        string database = "duration_upgrade_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection admin = new(environment.MigratedConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = new($"CREATE DATABASE {database}", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            string connection = new NpgsqlConnectionStringBuilder(environment.MigratedConnectionString)
                { Database = database }.ConnectionString;
            await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(connection);
            string[] migrations = db.Database.GetMigrations().ToArray();
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            Guid userId = Guid.NewGuid();
            Guid passageId = Guid.NewGuid();
            Guid writingId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.users (id, email, display_name, password_hash, role, is_active, created_at)
                VALUES ({userId}, 'duration-upgrade@example.test', 'Admin', 'test-hash', 'Admin', true, {now});
                INSERT INTO content.passages (id, title, body, source_note, usage_basis, status, version, created_by)
                VALUES ({passageId}, 'Old passage', 'Text', 'Team', 'Original', 'Draft', 1, {userId});
                INSERT INTO writing.writing_prompts
                (id, title, task_text, essay_type, min_words, max_words, status, created_by, created_at, updated_at)
                VALUES ({writingId}, 'Old writing', 'Text', 'Opinion', 100, 350, 'Draft', {userId}, {now}, {now});
                """);
            await db.Database.MigrateAsync();
            (await db.Passages.SingleAsync()).DurationMinutes.ShouldBe(20);
            (await db.WritingPrompts.SingleAsync()).DurationMinutes.ShouldBe(40);
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
        finally
        {
            await using NpgsqlCommand drop = new($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("passages", 20)]
    [InlineData("writing-prompts", 40)]
    public async Task Duration_Is_Saved_Listed_And_Preserved_When_Omitted_Or_Null(string resource, int defaultMinutes)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory);
        string marker = "duration-" + Guid.NewGuid();
        string path = await CreateAsync(client, resource, Input(marker));
        (await DetailAsync(client, path)).GetProperty("durationMinutes").GetInt32().ShouldBe(defaultMinutes);
        string custom = await CreateAsync(client, resource, Input(marker, 27));
        (await DetailAsync(client, custom)).GetProperty("durationMinutes").GetInt32().ShouldBe(27);
        await PutAsync(client, custom, Input(marker, 35));
        JsonElement before = await DetailAsync(client, custom);
        before.GetProperty("durationMinutes").GetInt32().ShouldBe(35);
        await PutAsync(client, custom, Input(marker, 35));
        (await DetailAsync(client, custom)).GetProperty("updatedAt").ToString()
            .ShouldBe(before.GetProperty("updatedAt").ToString());
        await PutAsync(client, custom, Input(marker));
        (await DetailAsync(client, custom)).GetProperty("durationMinutes").GetInt32().ShouldBe(35);
        await PutAsync(client, custom, Input(marker, null, includeDuration: true));
        (await DetailAsync(client, custom)).GetProperty("durationMinutes").GetInt32().ShouldBe(35);
        JsonElement list = await client.GetFromJsonAsync<JsonElement>($"/api/admin/{resource}?search={marker}");
        var row = list.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == custom.Split('/')[^1]);
        row.GetProperty("durationMinutes").GetInt32().ShouldBe(35);

        foreach (object invalid in new object[] { 0, -1, 1.5, "invalid" })
        {
            using HttpResponseMessage create = await client.PostAsJsonAsync($"/api/admin/{resource}", Input(marker, invalid));
            create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using HttpResponseMessage edit = await client.PutAsJsonAsync(custom, Input(marker, invalid));
            edit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await DetailAsync(client, custom)).GetProperty("durationMinutes").GetInt32().ShouldBe(35);
        }
    }

    [Theory]
    [InlineData("passages")]
    [InlineData("writing-prompts")]
    public async Task Published_Content_Is_Read_Only_And_Copies_Preserve_The_Duration(string resource)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory);
        Guid actor = Guid.NewGuid();
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            User creator = User.Create($"creator-{actor}@example.test", "Creator", "test-hash", DateTimeOffset.UtcNow);
            db.Users.Add(creator);
            actor = creator.Id;
            await db.SaveChangesAsync();
        }

        Guid id;
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            if (resource == "passages")
            {
                Passage passage = Passage.CreateDraft("Duration passage", "Birds build nests.", "Team", "Original", actor, 31);
                Question question = passage.AddQuestion(QuestionType.TrueFalseNotGiven, "Birds build nests.", 1);
                question.SetAnswerKey("True");
                question.SetExplanation("The passage states this.", "Birds build nests.");
                question.VerifyAnswerKey(actor, DateTimeOffset.UtcNow);
                passage.Publish(DateTimeOffset.UtcNow);
                db.Passages.Add(passage);
                id = passage.Id;
            }
            else
            {
                WritingPrompt prompt = WritingPrompt.CreateDraft("Duration writing", "Discuss the topic.", "Opinion essay",
                    100, 350, actor, DateTimeOffset.UtcNow, 31);
                prompt.Publish(DateTimeOffset.UtcNow);
                db.WritingPrompts.Add(prompt);
                id = prompt.Id;
            }
            await db.SaveChangesAsync();
        }

        string path = $"/api/admin/{resource}/{id}";
        using HttpResponseMessage blocked = await client.PutAsJsonAsync(path, Input("Changed", 60));
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await DetailAsync(client, path)).GetProperty("durationMinutes").GetInt32().ShouldBe(31);
        string action = resource == "passages" ? "revisions" : "duplicate";
        using HttpResponseMessage copy = await client.PostAsJsonAsync($"{path}/{action}", new { });
        copy.StatusCode.ShouldBe(HttpStatusCode.Created, await copy.Content.ReadAsStringAsync());
        JsonElement draft = await DetailAsync(client, copy.Headers.Location!.OriginalString);
        draft.GetProperty("durationMinutes").GetInt32().ShouldBe(31);
        await PutAsync(client, copy.Headers.Location.OriginalString, Input("Changed duration", 45));
        (await DetailAsync(client, path)).GetProperty("durationMinutes").GetInt32().ShouldBe(31);
    }

    private static Dictionary<string, object?> Input(string title, object? duration = null, bool includeDuration = false)
    {
        Dictionary<string, object?> input = new()
        {
            ["title"] = title, ["body"] = "Original passage body.", ["sourceNote"] = "Team", ["usageBasis"] = "Original",
            ["taskText"] = "Discuss the topic.", ["essayType"] = "Opinion essay", ["minWords"] = 100, ["maxWords"] = 350,
        };
        if (includeDuration || duration is not null)
        {
            input["durationMinutes"] = duration;
        }
        return input;
    }

    private static async Task<string> CreateAsync(HttpClient client, string resource, object input)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/admin/{resource}", input);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString;
    }
    private static async Task PutAsync(HttpClient client, string path, object input)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(path, input);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
    private static Task<JsonElement> DetailAsync(HttpClient client, string path) => client.GetFromJsonAsync<JsonElement>(path);
    private async Task<HttpClient> ClientAsync(BandUpApiFactory factory)
    {
        User actor = User.Create($"duration-{Guid.NewGuid()}@example.test", "Editor", "test-hash", DateTimeOffset.UtcNow);
        actor.ChangeRole(Role.Admin);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actor.Id, "Admin", DateTimeOffset.UtcNow).Value;
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }
}
