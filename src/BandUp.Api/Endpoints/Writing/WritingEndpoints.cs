using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using Microsoft.Net.Http.Headers;

namespace BandUp.Api.Endpoints.Writing;

internal sealed class PrivateWritingResponse;

public sealed class WritingEndpoints(IConfiguration configuration) : IEndpointGroup
{
    private const string SubmissionEnabled = "Features:WritingSubmissionEnabled";
    private const string KeyHeader = "Idempotency-Key";
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 4,
    };

    public string Prefix => "writing";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/prompts", ListPromptsAsync)
            .WithName("Writing_ListPrompts")
            .WithMetadata(new PrivateWritingResponse())
            .RequireAuthorization()
            .Produces<WritingPromptListDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/submissions", SubmitAsync)
            .WithName("Writing_Submit")
            .WithMetadata(new PrivateWritingResponse())
            .RequireAuthorization()
            .Produces<WritingSubmissionAcceptedDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        var reads = endpoints.MapGroup("/submissions")
            .WithMetadata(new PrivateWritingResponse())
            .RequireAuthorization();

        reads.MapGet("", ListSubmissionsAsync).WithName("Writing_ListSubmissions")
            .Produces<PagedResult<WritingSubmissionHistoryItemDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        reads.MapGet("/{id:guid}", (Guid id, IDispatcher dispatcher, HttpContext context, CancellationToken ct) =>
            ReadAsync<GetWritingStatusQuery, WritingEvaluationStatusDto>(new(id), dispatcher, context, ct))
            .WithName("Writing_GetStatus")
            .Produces<WritingEvaluationStatusDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        reads.MapGet("/{id:guid}/detail", (Guid id, IDispatcher dispatcher, HttpContext context, CancellationToken ct) =>
            ReadAsync<GetWritingDetailQuery, WritingSubmissionDetailDto>(new(id), dispatcher, context, ct))
            .WithName("Writing_GetDetail")
            .Produces<WritingSubmissionDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        reads.MapGet("/{id:guid}/result", (Guid id, IDispatcher dispatcher, HttpContext context, CancellationToken ct) =>
            ReadAsync<GetWritingResultQuery, WritingEvaluationResultDto>(new(id), dispatcher, context, ct))
            .WithName("Writing_GetResult")
            .Produces<WritingEvaluationResultDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private static Task<IResult> ListSubmissionsAsync(IDispatcher dispatcher, HttpContext context, CancellationToken ct)
    {
        var query = context.Request.Query;
        if (!TryReadPageValue(query, "page", 1, out var page))
        {
            return Task.FromResult(Invalid(context, "page", "Send one integer page value."));
        }
        if (!TryReadPageValue(query, "pageSize", PageRequest.DefaultPageSize, out var pageSize))
        {
            return Task.FromResult(Invalid(context, "pageSize", "Send one integer page size."));
        }
        return ReadAsync<ListWritingSubmissionsQuery, PagedResult<WritingSubmissionHistoryItemDto>>(
            new(new PageRequest(page, pageSize)), dispatcher, context, ct);
    }

    private static bool TryReadPageValue(IQueryCollection query, string name, int fallback, out int value)
    {
        value = fallback;
        return !query.TryGetValue(name, out var values) || (values.Count == 1 &&
            int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out value));
    }

    private static async Task<IResult> ReadAsync<TQuery, TResponse>(TQuery query, IDispatcher dispatcher,
        HttpContext context, CancellationToken ct) where TQuery : IQuery<TResponse>
    {
        var result = await dispatcher.QueryAsync<TQuery, TResponse>(query, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> ListPromptsAsync(IDispatcher dispatcher, HttpContext context, CancellationToken ct)
    {
        var result = await dispatcher.QueryAsync<ListWritingPromptsQuery, WritingPromptListDto>(new(), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private async Task<IResult> SubmitAsync(HttpContext context, IDispatcher dispatcher, ICurrentUser currentUser,
        IServiceScopeFactory scopes, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>(SubmissionEnabled))
        {
            return Error.NotFound("Writing submission is unavailable.").ToProblem(context);
        }

        var request = context.Request;
        if (request.ContentLength > MaxBodyBytes)
        {
            return BodyTooLarge(context);
        }
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(context, "body", "Send an application/json request body.");
        }

        // Read one byte beyond the limit to distinguish an exact-limit body from an oversized stream.
        var body = new byte[MaxBodyBytes + 1];
        int length = await request.Body.ReadAtLeastAsync(body, body.Length, throwOnEndOfStream: false, cancellationToken: ct);
        if (length > MaxBodyBytes)
        {
            return BodyTooLarge(context);
        }
        var keys = request.Headers[KeyHeader];
        if (keys.Count != 1 || !Guid.TryParseExact(keys[0], "D", out var key) || key == Guid.Empty)
        {
            return Invalid(context, KeyHeader, "Send one non-empty UUID idempotency key.");
        }
        if (!Utf8.IsValid(body.AsSpan(0, length)))
        {
            return Invalid(context, "body", "Send a valid UTF-8 JSON body.");
        }

        SubmitWritingEssayRequest? submission;
        try
        {
            submission = JsonSerializer.Deserialize<SubmitWritingEssayRequest>(body.AsSpan(0, length), StrictJson);
        }
        catch (JsonException)
        {
            // Parser details may contain client-controlled property names.
            return Invalid(context, "body", "Send a JSON object containing promptId and essayText.");
        }
        if (submission is null)
        {
            return Invalid(context, "body", "Send a JSON object containing promptId and essayText.");
        }

        var command = new SubmitWritingEssayCommand(key, submission.PromptId, submission.EssayText);
        var owner = currentUser.IsAuthenticated ? currentUser.UserId : null;
        Result<WritingSubmissionAcceptedDto> result;
        try
        {
            result = await dispatcher.SendAsync<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>(command, ct);
        }
        catch (WritingSubmissionKeyConflictException conflict) when (owner is { } ownerId && ownerId != Guid.Empty &&
            !conflict.Data.Contains("TransactionCleanup"))
        {
            ct.ThrowIfCancellationRequested();
            // The failed command's context has been retired. Only read the committed winner in a fresh scope.
            await using var recovery = scopes.CreateAsyncScope();
            var store = recovery.ServiceProvider.GetRequiredService<IWritingSubmissionStore>();
            var existing = await store.FindByKeyAsync(ownerId, key, ct);
            if (existing is null)
            {
                throw;
            }
            result = WritingSubmissionReplay.Compare(existing, command);
        }
        return result.IsSuccess
            ? TypedResults.Accepted($"/api/writing/submissions/{result.Value.SubmissionId}", result.Value)
            : result.ToProblem(context);
    }

    private static IResult Invalid(HttpContext context, string field, string message) =>
        Error.Validation(new Dictionary<string, string[]> { [field] = [message] }).ToProblem(context);

    private static IResult BodyTooLarge(HttpContext context)
    {
        var problem = ProblemDetailsFactory.ForBadHttpRequest(context, StatusCodes.Status413PayloadTooLarge);
        problem.Detail = "The request body exceeds 64 KiB.";
        problem.Extensions["errors"] = new Dictionary<string, string[]> { ["body"] = [problem.Detail] };
        return new ProblemResult(problem);
    }
}
