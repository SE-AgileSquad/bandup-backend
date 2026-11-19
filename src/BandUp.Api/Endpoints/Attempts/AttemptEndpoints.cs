using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Exams;
using BandUp.Application.Features.Exams.GetAttempt;
using BandUp.Application.Features.Exams.GetAttemptResult;
using BandUp.Application.Features.Exams.GetPassageAttemptSummaries;
using BandUp.Application.Features.Exams.ListAttemptHistory;
using BandUp.Application.Features.Exams.SaveAnswer;
using BandUp.Application.Features.Exams.StartAttempt;
using BandUp.Application.Features.Exams.SubmitAttempt;
using Microsoft.AspNetCore.Mvc;

namespace BandUp.Api.Endpoints.Attempts;

/// <summary>
/// The exam sitting. The server owns the clock: the browser never sends a time, and after a
/// refresh it asks for the state and gets the deadline and the time left back.
/// </summary>
public sealed class AttemptEndpoints : IEndpointGroup
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public string Prefix => "attempts";

    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", StartAttemptAsync)
            .WithName("Attempts_Start")
            .RequireAuthorization()
            .Produces<AttemptStateDto>(StatusCodes.Status201Created)
            .Produces<AttemptStateDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet("/", ListAttemptHistoryAsync)
            .WithName("Attempts_History")
            .RequireAuthorization()
            .Produces<PagedResult<AttemptHistoryItemDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet("/summary", GetPassageAttemptSummariesAsync)
            .WithName("Attempts_PassageSummaries")
            .RequireAuthorization()
            .Produces<IReadOnlyList<PassageAttemptSummaryDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet("/{attemptId:guid}", GetAttemptAsync)
            .WithName("Attempts_Get")
            .RequireAuthorization()
            .Produces<AttemptStateDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPut("/{attemptId:guid}/answers/{questionId:guid}", SaveAnswerAsync)
            .WithName("Attempts_SaveAnswer")
            .RequireAuthorization()
            .Produces<AttemptStateDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/{attemptId:guid}/submit", SubmitAttemptAsync)
            .WithName("Attempts_Submit")
            .RequireAuthorization()
            .Produces<AttemptResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet("/{attemptId:guid}/result", GetAttemptResultAsync)
            .WithName("Attempts_GetResult")
            .RequireAuthorization()
            .Produces<AttemptResultDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> StartAttemptAsync(
        StartAttemptRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<StartAttemptResult> result = await dispatcher.SendAsync<StartAttemptCommand, StartAttemptResult>(
            new StartAttemptCommand(request.PassageId),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToProblem(httpContext);
        }

        AttemptStateDto state = result.Value.Attempt;

        return result.Value.Created
            ? TypedResults.Created($"/api/attempts/{state.Id}", state)
            : TypedResults.Ok(state);
    }

    /// <summary>
    /// The learner's test history: submitted attempts only, newest first. A sitting that ran out
    /// of time recorded no result and is not listed.
    /// </summary>
    private static async Task<IResult> ListAttemptHistoryAsync(
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize)
    {
        Result<PagedResult<AttemptHistoryItemDto>> result =
            await dispatcher.QueryAsync<ListAttemptHistoryQuery, PagedResult<AttemptHistoryItemDto>>(
                new ListAttemptHistoryQuery(page, pageSize),
                cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    /// <summary>
    /// Where the learner stands on each passage they have started: running with the time left, or
    /// completed with the last score. A passage with no entry is new to them. The Reading Tests
    /// list combines this with the passage list.
    /// </summary>
    private static async Task<IResult> GetPassageAttemptSummariesAsync(
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<PassageAttemptSummaryDto>> result =
            await dispatcher.QueryAsync<GetPassageAttemptSummariesQuery, IReadOnlyList<PassageAttemptSummaryDto>>(
                new GetPassageAttemptSummariesQuery(),
                cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> GetAttemptAsync(
        Guid attemptId,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<AttemptStateDto> result = await dispatcher.QueryAsync<GetAttemptQuery, AttemptStateDto>(
            new GetAttemptQuery(attemptId),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> SaveAnswerAsync(
        Guid attemptId,
        Guid questionId,
        SaveAnswerRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<AttemptStateDto> result = await dispatcher.SendAsync<SaveAnswerCommand, AttemptStateDto>(
            new SaveAnswerCommand(attemptId, questionId, request.SelectedAnswer),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> SubmitAttemptAsync(
        Guid attemptId,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        SubmitAttemptRequest? request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        SubmittedAnswerInput[] answers =
        [
            .. (request?.Answers ?? []).Select(
                answer => new SubmittedAnswerInput(answer.QuestionId, answer.SelectedAnswer)),
        ];

        Result<AttemptResultDto> result = await dispatcher.SendAsync<SubmitAttemptCommand, AttemptResultDto>(
            new SubmitAttemptCommand(attemptId, idempotencyKey ?? string.Empty, answers),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> GetAttemptResultAsync(
        Guid attemptId,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<AttemptResultDto> result = await dispatcher.QueryAsync<GetAttemptResultQuery, AttemptResultDto>(
            new GetAttemptResultQuery(attemptId),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    public sealed record SubmitAnswerRequest(Guid QuestionId, string? SelectedAnswer);

    public sealed record SubmitAttemptRequest(IReadOnlyList<SubmitAnswerRequest>? Answers);

    public sealed record StartAttemptRequest(Guid PassageId);

    public sealed record SaveAnswerRequest(string? SelectedAnswer);
}
