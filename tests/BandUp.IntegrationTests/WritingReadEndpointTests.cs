using System.Net;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.IntegrationTests;

public sealed partial class WritingEndpointTests
{
    [Fact]
    public async Task ReadRoutesRequireAuthenticationAndKeepLearnersAndAdminsOwnerScoped()
    {
        await using var factory = EnabledFactory();
        using var client = ContentTestData.CreateClient(factory);
        var owner = await ContentTestData.RegisterAndLoginAsync(client, "writing-owner");
        var stranger = await ContentTestData.RegisterAndLoginAsync(client, "writing-stranger");
        var admin = await ContentTestData.RegisterAndLoginAsync(client, "writing-admin");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            var actor = await db.Users.SingleAsync(u => u.Id == admin.Id);
            actor.ChangeRole(Role.Admin);
            await db.SaveChangesAsync();
        }
        var token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(admin.Id, Role.Admin.ToString(), DateTimeOffset.UtcNow);
        admin = admin with { AccessCookie = $"bu_at={token.Value}" };
        var prompt = await SeedPrompt(factory, owner.Id);
        var id = await AcceptForRead(client, owner, prompt);
        using (var settle = factory.Services.CreateScope())
        {
            var dispatch = settle.ServiceProvider.GetRequiredService<IDispatcher>();
            Assert.True((await dispatch.SendAsync<ClaimWriting, bool>(new(id), CancellationToken.None)).Value);
            await CompleteForRead(dispatch, id);
        }

        using (var anonymousHistory = await Fetch(client, Submissions))
        {
            await AssertProblem(anonymousHistory, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        }
        foreach (var caller in new[] { stranger, admin })
        {
            var empty = await ReadOk(client, Submissions, caller);
            Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
            Assert.Empty(empty.GetProperty("items").EnumerateArray());
        }

        var absent = Guid.NewGuid();
        foreach (var suffix in new[] { "", "/detail", "/result" })
        {
            using var anonymous = await Fetch(client, $"{Submissions}/{id}{suffix}");
            await AssertProblem(anonymous, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
            foreach (var caller in new[] { stranger, admin })
            {
                using var foreign = await Fetch(client, $"{Submissions}/{id}{suffix}", caller);
                await AssertProblem(foreign, HttpStatusCode.NotFound, "NOT_FOUND");
            }
            using var missing = await Fetch(client, $"{Submissions}/{absent}{suffix}", owner);
            await AssertProblem(missing, HttpStatusCode.NotFound, "NOT_FOUND");
        }
    }

    [Fact]
    public async Task ReadsWithAcceptanceOffPreserveHistorySnapshotsResultsAndFinancialState()
    {
        var clock = new TestClock();
        await using var setup = new BandUpApiFactory(environment, overrides: new() { ["Features:WritingSubmissionEnabled"] = "true" },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));
        using var setupClient = ContentTestData.CreateClient(setup);
        var owner = await ContentTestData.RegisterAndLoginAsync(setupClient, "writing-read");
        var prompt = await SeedPrompt(setup, owner.Id);
        var failed = await AcceptForRead(setupClient, owner, prompt);
        var processing = await AcceptForRead(setupClient, owner, prompt);
        var succeeded = await AcceptForRead(setupClient, owner, prompt);
        Guid queued;

        using (var settle = setup.Services.CreateScope())
        {
            var dispatch = settle.ServiceProvider.GetRequiredService<IDispatcher>();
            Assert.True((await dispatch.SendAsync<ClaimWriting, bool>(new(failed), CancellationToken.None)).Value);
            Assert.True((await dispatch.SendAsync<FailWriting, bool>(new(failed), CancellationToken.None)).Value);
            Assert.True((await dispatch.SendAsync<ClaimWriting, bool>(new(processing), CancellationToken.None)).Value);
            Assert.True((await dispatch.SendAsync<ClaimWriting, bool>(new(succeeded), CancellationToken.None)).Value);
            await CompleteForRead(dispatch, succeeded);

            // The frozen clock gives every submission the same acceptance timestamp.
            queued = await AcceptForRead(setupClient, owner, prompt);
            var db = settle.ServiceProvider.GetRequiredService<BandUpDbContext>();
            (await db.WritingPrompts.SingleAsync(p => p.Id == prompt)).Archive(clock.GetUtcNow());
            await db.SaveChangesAsync();
        }

        await using BandUpApiFactory disabled = new(environment);
        using var client = ContentTestData.CreateClient(disabled);
        var before = await ReadState(disabled, owner.Id);
        var history = await ReadOk(client, Submissions, owner);
        Fields(history, "items", "page", "pageSize", "totalCount");
        Assert.Equal(1, history.GetProperty("page").GetInt32());
        Assert.Equal(20, history.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, history.GetProperty("totalCount").GetInt32());
        var items = history.GetProperty("items").EnumerateArray().ToArray();
        var expected = new[] { failed, processing, succeeded, queued }.OrderByDescending(id => id).ToArray();
        Assert.Equal(expected, items.Select(item => item.GetProperty("submissionId").GetGuid()).ToArray());
        Assert.Single(items.Select(item => item.GetProperty("submittedAt").GetDateTimeOffset()).Distinct());
        foreach (var item in items)
        {
            var keys = new List<string> { "submissionId", "promptId", "title", "status", "submittedAt" };
            var status = item.GetProperty("status").GetString();
            if (status is "Failed" or "Succeeded")
            {
                keys.Add("completedAt");
            }
            if (status == "Succeeded")
            {
                keys.AddRange(["estimatedOverallBand", "scoreLabel"]);
                Assert.Equal(6.5m, item.GetProperty("estimatedOverallBand").GetDecimal());
                Assert.Equal("estimated", item.GetProperty("scoreLabel").GetString());
            }
            Fields(item, keys.ToArray());
            Assert.Equal("Cities", item.GetProperty("title").GetString());
        }
        var second = await ReadOk(client, $"{Submissions}?page=2&pageSize=2", owner);
        Assert.Equal(expected[2..], second.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("submissionId").GetGuid()).ToArray());
        var clamped = await ReadOk(client, $"{Submissions}?page=0&pageSize=101", owner);
        Assert.Equal(1, clamped.GetProperty("page").GetInt32());
        Assert.Equal(100, clamped.GetProperty("pageSize").GetInt32());
        var distant = await ReadOk(client, $"{Submissions}?page={int.MaxValue}&pageSize=100", owner);
        Assert.Empty(distant.GetProperty("items").EnumerateArray());
        foreach (var invalid in new[] { "page=1.5", "pageSize=2147483648", "page=1&page=2" })
        {
            using var bad = await Fetch(client, $"{Submissions}?{invalid}", owner);
            await AssertProblem(bad, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }

        foreach (var (id, status) in new[] { (failed, "Failed"), (processing, "Processing"), (succeeded, "Succeeded"), (queued, "Queued") })
        {
            var data = await ReadOk(client, $"{Submissions}/{id}", owner);
            Assert.Equal(id, data.GetProperty("submissionId").GetGuid());
            Assert.Equal(status, data.GetProperty("status").GetString());
            if (status == "Succeeded")
            {
                Fields(data, "submissionId", "submittedAt", "status", "completedAt", "resultUrl");
                Assert.Equal($"{Submissions}/{id}/result", data.GetProperty("resultUrl").GetString());
            }
            else if (status == "Failed")
            {
                Fields(data, "submissionId", "submittedAt", "status", "completedAt", "failure");
                var failure = data.GetProperty("failure");
                Fields(failure, "code", "creditRefunded");
                Assert.Equal("EVALUATION_FAILED", failure.GetProperty("code").GetString());
                Assert.True(failure.GetProperty("creditRefunded").GetBoolean());
            }
            else
            {
                Fields(data, "submissionId", "submittedAt", "status");
            }
            if (status != "Succeeded")
            {
                using var unavailable = await Fetch(client, $"{Submissions}/{id}/result", owner);
                await AssertProblem(unavailable, HttpStatusCode.Conflict,
                    status == "Failed" ? "EVALUATION_FAILED" : "EVALUATION_NOT_READY");
            }
        }

        var detail = await ReadOk(client, $"{Submissions}/{succeeded}/detail", owner);
        Fields(detail, "submissionId", "essayText", "promptSnapshot");
        Assert.Equal(Essay, detail.GetProperty("essayText").GetString());
        var snapshot = detail.GetProperty("promptSnapshot");
        Fields(snapshot, "id", "title", "promptText");
        Assert.Equal(prompt, snapshot.GetProperty("id").GetGuid());
        Assert.Equal("Discuss urban growth.", snapshot.GetProperty("promptText").GetString());
        var result = await ReadOk(client, $"{Submissions}/{succeeded}/result", owner);
        Fields(result, "submissionId", "completedAt", "feedback");
        var publicFeedback = result.GetProperty("feedback");
        Fields(publicFeedback, "schemaVersion", "scoreLabel", "estimatedOverallBand", "summary", "criteria");
        Assert.Equal("1.0", publicFeedback.GetProperty("schemaVersion").GetString());
        Assert.Equal("estimated", publicFeedback.GetProperty("scoreLabel").GetString());
        Assert.Equal(6.5m, publicFeedback.GetProperty("estimatedOverallBand").GetDecimal());
        var criteria = publicFeedback.GetProperty("criteria");
        Fields(criteria, "tr", "cc", "lr", "gra");
        Fields(criteria.GetProperty("tr"), "score", "explanation", "strengths", "areasForImprovement", "recommendations");
        Assert.Equal(before, await ReadState(disabled, owner.Id));
    }

    private static async Task<Guid> AcceptForRead(HttpClient client, ContentTestData.TestUser owner, Guid prompt)
    {
        using var response = await Post(client, owner, Payload(prompt), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await ReadJson(response)).GetProperty("submissionId").GetGuid();
    }

    private static async Task CompleteForRead(IDispatcher dispatch, Guid id)
    {
        var criterion = new CriterionFeedback(6.5m, "Explanation", [], [], ["Add an example."]);
        var feedback = new WritingFeedback(id, "Summary", criterion, criterion, criterion, criterion);
        Assert.True((await dispatch.SendAsync<CompleteWriting, bool>(new(id, feedback), CancellationToken.None)).Value);
    }

    private static async Task<HttpResponseMessage> Fetch(HttpClient client, string path, ContentTestData.TestUser? owner = null)
    {
        using var request = ContentTestData.Get(path, owner);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadOk(HttpClient client, string path, ContentTestData.TestUser owner)
    {
        using var response = await Fetch(client, path, owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await ReadJson(response);
    }

    private static void Fields(JsonElement value, params string[] names) =>
        Assert.Equal(names.Order(StringComparer.Ordinal).ToArray(),
            value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

    private static async Task<string> ReadState(BandUpApiFactory factory, Guid owner)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var account = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        var ledger = await db.CreditLedger.AsNoTracking().Where(e => e.UserId == owner).OrderBy(e => e.Id).ToListAsync();
        var submissions = await db.WritingSubmissions.AsNoTracking().Where(s => s.UserId == owner)
            .OrderBy(s => s.Id).ToListAsync();
        return JsonSerializer.Serialize(new { account, ledger, submissions });
    }
}
