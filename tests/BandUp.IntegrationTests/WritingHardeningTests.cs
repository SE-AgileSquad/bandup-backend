using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.IntegrationTests;

public sealed partial class WritingEndpointTests
{
    [Fact]
    public async Task InvalidUtf8MediaTypesAndRepeatedHeadersDoNotSpendCredits()
    {
        await using var factory = EnabledFactory();
        using var client = ContentTestData.CreateClient(factory);
        var owner = await ContentTestData.RegisterAndLoginAsync(client, "writing-wire");
        var prompt = await SeedPrompt(factory, owner.Id);
        var valid = Encoding.UTF8.GetBytes(Payload(prompt));
        var key = Guid.NewGuid().ToString();
        var invalidUtf8 = Encoding.UTF8.GetBytes($"{{\"promptId\":\"{prompt}\",\"essayText\":\"")
            .Concat(new byte[] { 0xc3, 0x28 }).Concat("\"}"u8.ToArray()).ToArray();

        using (var malformed = await PostBytes(client, owner, invalidUtf8, [key], "application/json"))
        {
            await AssertProblem(malformed, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            var problem = await ReadJson(malformed);
            Assert.Equal("Send a valid UTF-8 JSON body.", problem.GetProperty("errors").GetProperty("body")[0].GetString());
        }
        foreach (var mediaType in new[] { null, "text/plain", "application/jsonp" })
        {
            using var wrongType = await PostBytes(client, owner, valid, [key], mediaType);
            await AssertProblem(wrongType, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        using (var multiple = await PostBytes(client, owner, valid, [key, Guid.NewGuid().ToString()], "application/json"))
        {
            await AssertProblem(multiple, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            var problem = await ReadJson(multiple);
            Assert.True(problem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _));
        }
        await AssertUnchangedSignupState(factory, owner.Id);
        using var accepted = await PostBytes(client, owner, valid, [key], "application/json; charset=utf-8");
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await AssertAccount(factory, owner.Id, charges: 1);
    }

    [Fact]
    public async Task WritingLogsKeepOperationalFailuresWithoutParserOrPayloadDetails()
    {
        var logs = new TestLogSink();
        await using var factory = new BandUpApiFactory(environment,
            overrides: new() { ["Features:WritingSubmissionEnabled"] = "true" }, logSink: logs,
            configureServices: services =>
                services.AddScoped<IPipelineBehavior<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>, PrivateFailureProbe>());
        using var client = ContentTestData.CreateClient(factory);
        var owner = await ContentTestData.RegisterAndLoginAsync(client, "writing-private");
        var prompt = await SeedPrompt(factory, owner.Id);
        var key = Guid.NewGuid().ToString();
        var essay = Essay + " privateEssayMarker";
        logs.Clear();
        var malformed = Payload(prompt, essay).Insert(1, "\"privateParserMarker\":true,");
        using var rejected = await Post(client, owner, malformed, key);
        await AssertProblem(rejected, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using var failed = await Post(client, owner, Payload(prompt, essay), key);
        await AssertProblem(failed, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        await AssertUnchangedSignupState(factory, owner.Id);

        Assert.Contains(logs.Events, entry => entry.RenderMessage().Contains("Writing request failed", StringComparison.Ordinal));
        foreach (var entry in logs.Events)
        {
            var rendered = entry.RenderMessage() + entry.Exception + string.Join(" ", entry.Properties.Values);
            Assert.DoesNotContain("privateEssayMarker", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("privateParserMarker", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("privateFeedbackMarker", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(owner.AccessCookie, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(key, rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SwaggerDescribesAllWritingRoutesAndTheManuallyParsedInputs()
    {
        await using BandUpApiFactory factory = new(environment);
        using var client = ContentTestData.CreateClient(factory);
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJson(response);
        var session = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("WritingSession");
        Assert.Equal("cookie", session.GetProperty("in").GetString());
        Assert.Equal("bu_at", session.GetProperty("name").GetString());
        var paths = document.GetProperty("paths");
        AssertResponses(paths.GetProperty(Prompts).GetProperty("get"), "200", "401", "500");
        var list = paths.GetProperty(Submissions).GetProperty("get");
        AssertResponses(list, "200", "400", "401", "500");
        Assert.Equal(1, FindParameter(list, "page").GetProperty("schema").GetProperty("default").GetInt32());
        Assert.Equal(20, FindParameter(list, "pageSize").GetProperty("schema").GetProperty("default").GetInt32());
        AssertResponses(paths.GetProperty(Submissions + "/{id}").GetProperty("get"), "200", "401", "404", "500");
        AssertResponses(paths.GetProperty(Submissions + "/{id}/detail").GetProperty("get"), "200", "401", "404", "500");
        AssertResponses(paths.GetProperty(Submissions + "/{id}/result").GetProperty("get"), "200", "401", "404", "409", "500");

        var submit = paths.GetProperty(Submissions).GetProperty("post");
        AssertResponses(submit, "202", "400", "401", "402", "404", "409", "413", "500");
        var key = FindParameter(submit, "Idempotency-Key");
        Assert.Equal("header", key.GetProperty("in").GetString());
        Assert.True(key.GetProperty("required").GetBoolean());
        Assert.Equal("uuid", key.GetProperty("schema").GetProperty("format").GetString());
        Assert.True(FindParameter(submit, "X-Requested-With").GetProperty("required").GetBoolean());
        var body = submit.GetProperty("requestBody");
        Assert.True(body.GetProperty("required").GetBoolean());
        var schema = body.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "essayText", "promptId" },
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Order(StringComparer.Ordinal).ToArray());
        Assert.True(submit.GetProperty("responses").GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _));
    }

    private static JsonElement FindParameter(JsonElement operation, string name) =>
        Assert.Single(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == name);

    private static void AssertResponses(JsonElement operation, params string[] codes)
    {
        var responses = operation.GetProperty("responses");
        Assert.True(operation.GetProperty("security")[0].TryGetProperty("WritingSession", out _));
        Fields(responses, codes);
        var success = responses.GetProperty(codes[0]).GetProperty("content").GetProperty("application/json");
        Assert.True(success.TryGetProperty("schema", out _));
        foreach (var code in codes.Skip(1))
        {
            Assert.True(responses.GetProperty(code).GetProperty("content").TryGetProperty("application/problem+json", out _));
        }
    }

    private static async Task<HttpResponseMessage> PostBytes(HttpClient client, ContentTestData.TestUser owner,
        byte[] bytes, string[] keys, string? contentType)
    {
        using var request = ContentTestData.Send(HttpMethod.Post, Submissions, owner);
        request.Headers.Add("Idempotency-Key", keys);
        request.Content = new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        return await client.SendAsync(request);
    }

    private sealed class PrivateFailureProbe : IPipelineBehavior<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>
    {
        public async Task<Result<WritingSubmissionAcceptedDto>> HandleAsync(SubmitWritingEssayCommand request,
            CancellationToken ct, RequestHandlerDelegate<WritingSubmissionAcceptedDto> next)
        {
            await next();
            throw new InvalidOperationException($"privateFeedbackMarker {request.EssayText}");
        }
    }
}
