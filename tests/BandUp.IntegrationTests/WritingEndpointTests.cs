using System.Net;
using System.Text;
using System.Text.Json;
using BandUp.Domain.Billing;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed partial class WritingEndpointTests(BandUpTestEnvironment environment)
{
    private const string Prompts = "/api/writing/prompts";
    private const string Submissions = "/api/writing/submissions";
    private const string Origin = "http://localhost:3000";
    private static readonly string Essay = string.Join(' ', Enumerable.Repeat("cafe\u0301", 100));

    private BandUpApiFactory EnabledFactory() => new(environment, overrides: new()
    {
        ["Features:WritingSubmissionEnabled"] = "true",
    });

    [Fact]
    public async Task PromptPickerRequiresAuthenticationAndReturnsOnlyApprovedPromptsInIdOrder()
    {
        await using BandUpApiFactory factory = new(environment);
        using var client = ContentTestData.CreateClient(factory);
        using var anonymous = await client.GetAsync(Prompts);
        await AssertProblem(anonymous, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-picker");
        var first = await SeedPrompt(factory, user.Id);
        var second = await SeedPrompt(factory, user.Id);
        var draft = await SeedPrompt(factory, user.Id, WritingPromptStatus.Draft);
        var archived = await SeedPrompt(factory, user.Id, WritingPromptStatus.Archived);
        var legacy = await SeedPrompt(factory, user.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            await db.WritingPrompts.Where(prompt => prompt.Id == legacy)
                .ExecuteUpdateAsync(properties => properties.SetProperty(prompt => prompt.MinWords, 99));
        }
        using var request = ContentTestData.Get(Prompts, user);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ReadJson(response);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        var ids = items.Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(ids.OrderBy(id => id).ToArray(), ids);
        Assert.DoesNotContain(draft, ids);
        Assert.DoesNotContain(archived, ids);
        Assert.DoesNotContain(legacy, ids);
        foreach (var expected in new[] { first, second })
        {
            var item = Assert.Single(items, item => item.GetProperty("id").GetGuid() == expected);
            Assert.Equal("Cities", item.GetProperty("title").GetString());
            Assert.Equal("Discuss urban growth.", item.GetProperty("promptText").GetString());
            Assert.Equal(3, item.EnumerateObject().Count());
        }
    }

    [Fact]
    public async Task AcceptanceIsOffByDefaultAndRequiresBothSessionAndCsrfWhenEnabled()
    {
        await using BandUpApiFactory disabled = new(environment);
        await using var enabled = EnabledFactory();
        using var defaultClient = ContentTestData.CreateClient(disabled);
        using var enabledClient = ContentTestData.CreateClient(enabled);
        var user = await ContentTestData.RegisterAndLoginAsync(defaultClient, "writing-security");
        var prompt = await SeedPrompt(disabled, user.Id);
        var json = Payload(prompt);
        using var gated = await Post(defaultClient, user, json, Guid.NewGuid().ToString());
        await AssertProblem(gated, HttpStatusCode.NotFound, "NOT_FOUND");

        using var unauthenticated = await Post(enabledClient, null, json, Guid.NewGuid().ToString());
        await AssertProblem(unauthenticated, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        using var withoutCsrf = await Post(enabledClient, user, json, Guid.NewGuid().ToString(), csrf: false);
        await AssertProblem(withoutCsrf, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertAccount(disabled, user.Id, charges: 0);
    }

    [Fact]
    public async Task AcceptanceReturnsStableReceiptAndChargesOnlyNewSubmissions()
    {
        await using var factory = EnabledFactory();
        using var client = ContentTestData.CreateClient(factory);
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-receipt");
        var prompt = await SeedPrompt(factory, user.Id);
        var key = Guid.NewGuid().ToString();
        using var accepted = await Post(client, user, Payload(prompt), key);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(accepted.Headers.CacheControl?.NoStore);
        var receipt = await ReadJson(accepted);
        var id = receipt.GetProperty("submissionId").GetGuid();
        Assert.Equal("Queued", receipt.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.Zero, receipt.GetProperty("submittedAt").GetDateTimeOffset().Offset);
        Assert.Equal(3, receipt.EnumerateObject().Count());
        Assert.Equal($"{Submissions}/{id}", accepted.Headers.Location?.OriginalString);
        Assert.Contains("Location", accepted.Headers.GetValues("Access-Control-Expose-Headers"), StringComparer.OrdinalIgnoreCase);

        using var conflict = await Post(client, user, Payload(prompt, Essay.Normalize()), key);
        await AssertProblem(conflict, HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        using var unavailable = await Post(client, user, Payload(Guid.NewGuid()), Guid.NewGuid().ToString());
        await AssertProblem(unavailable, HttpStatusCode.NotFound, "NOT_FOUND");

        for (int remaining = 2; remaining > 0; remaining--)
        {
            using var another = await Post(client, user, Payload(prompt), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Accepted, another.StatusCode);
        }
        using var replay = await Post(client, user, Payload(prompt), key);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(accepted.Headers.Location, replay.Headers.Location);
        Assert.Equal(receipt.GetRawText(), (await ReadJson(replay)).GetRawText());
        using var insufficient = await Post(client, user, Payload(prompt), Guid.NewGuid().ToString());
        await AssertProblem(insufficient, HttpStatusCode.PaymentRequired, "INSUFFICIENT_CREDIT");
        await AssertAccount(factory, user.Id, charges: 3);
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<BandUpDbContext>().WritingSubmissions.SingleAsync(s => s.Id == id);
        Assert.Equal(Essay, row.EssayText);
    }

    [Fact]
    public async Task StrictJsonAndUuidValidationLeaveTheKeyAvailable()
    {
        await using var factory = EnabledFactory();
        using var client = ContentTestData.CreateClient(factory);
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-input");
        var prompt = await SeedPrompt(factory, user.Id);
        var key = Guid.NewGuid().ToString();
        var valid = Payload(prompt);
        foreach (string malformed in new[]
        {
            "null", "[]", "{}", valid + "{}",
            valid.Insert(1, "\"essayText\":\"duplicate\","),
            valid.Insert(1, "\"EssayText\":\"wrong case\","),
            valid.Insert(1, "\"userId\":\"untrusted\","),
            $"{{\"promptId\":\"{prompt}\",\"essayText\":123}}",
            $"{{\"promptId\":\"{prompt}\",\"essayText\":null}}",
            $"{{\"promptId\":\"{prompt}\",\"essayText\":\"\\ud800\"}}",
            Payload(prompt, Essay + "\0"), Payload(prompt, "too short"), Payload(prompt, Essay + new string('a', 8000)),
        })
        {
            using var rejected = await Post(client, user, malformed, key);
            await AssertProblem(rejected, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        foreach (string? invalidKey in new[] { null, "invalid", Guid.Empty.ToString(), $"{key},{key}" })
        {
            using var rejected = await Post(client, user, valid, invalidKey);
            await AssertProblem(rejected, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        await AssertAccount(factory, user.Id, charges: 0);
        using var accepted = await Post(client, user, valid, key);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyLimitMeasuresBytesWithAndWithoutContentLength(bool chunked)
    {
        await using var factory = EnabledFactory();
        using var client = ContentTestData.CreateClient(factory);
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-body");
        var prompt = await SeedPrompt(factory, user.Id);
        var key = Guid.NewGuid().ToString();
        var json = Payload(prompt);
        var exact = json + new string(' ', 65_536 - Encoding.UTF8.GetByteCount(json));
        using var tooLarge = await Post(client, user, exact + " ", key, chunked: chunked);
        await AssertProblem(tooLarge, HttpStatusCode.RequestEntityTooLarge, "VALIDATION_ERROR");
        await AssertUnchangedSignupState(factory, user.Id);
        using var accepted = await Post(client, user, exact, key, chunked: chunked);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await AssertAccount(factory, user.Id, charges: 1);
    }

    [Fact]
    public async Task CorsAllowsTheSubmissionHeadersForTheConfiguredOrigin()
    {
        await using var factory = EnabledFactory();
        using var client = ContentTestData.CreateClient(factory);
        using HttpRequestMessage request = new(HttpMethod.Options, Submissions);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-requested-with,idempotency-key");
        using var response = await client.SendAsync(request);
        Assert.Equal(Origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains("idempotency-key", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"))
            .Split(',', StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        using HttpRequestMessage denied = new(HttpMethod.Options, Submissions);
        denied.Headers.Add("Origin", "https://unapproved.example.test");
        denied.Headers.Add("Access-Control-Request-Method", "POST");
        denied.Headers.Add("Access-Control-Request-Headers", "content-type,x-requested-with,idempotency-key");
        using var deniedResponse = await client.SendAsync(denied);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, ContentTestData.TestUser? user,
        string json, string? key, bool csrf = true, bool chunked = false)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Submissions);
        request.Headers.Add("Origin", Origin);
        if (user is not null)
        {
            request.Headers.Add("Cookie", user.AccessCookie);
        }
        if (csrf)
        {
            request.Headers.Add("X-Requested-With", "bandup");
        }
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }
        request.Content = chunked ? new UnknownLengthContent(Encoding.UTF8.GetBytes(json)) : new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new("application/json");
        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }
        return await client.SendAsync(request);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes.AsMemory()).AsTask();
    }

    private static string Payload(Guid prompt, string? essay = null) =>
        JsonSerializer.Serialize(new { promptId = prompt, essayText = essay ?? Essay });

    private static async Task<Guid> SeedPrompt(BandUpApiFactory factory, Guid owner,
        WritingPromptStatus status = WritingPromptStatus.Published)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var now = DateTimeOffset.UnixEpoch;
        var prompt = WritingPrompt.CreateDraft("Cities", "Discuss urban growth.", "Opinion", 100, 350, owner, now);
        if (status != WritingPromptStatus.Draft)
        {
            prompt.Publish(now);
        }
        if (status == WritingPromptStatus.Archived)
        {
            prompt.Archive(now);
        }
        db.WritingPrompts.Add(prompt);
        await db.SaveChangesAsync();
        return prompt.Id;
    }

    private static async Task AssertAccount(BandUpApiFactory factory, Guid owner, int charges)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner);
        Assert.Equal(3 - charges, account.Balance);
        Assert.Equal(charges, account.ReservedRefundCapacity);
        Assert.Equal(charges, await db.WritingSubmissions.CountAsync(s => s.UserId == owner));
        Assert.Equal(charges, await db.CreditLedger.CountAsync(e =>
            e.UserId == owner && e.Reason == CreditLedgerEntry.WritingChargeReason));
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var problem = await ReadJson(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal($"https://bandup.app/errors/{code.Replace('_', '-').ToLowerInvariant()}",
            problem.GetProperty("type").GetString());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
