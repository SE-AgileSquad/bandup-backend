using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Exams;
using BandUp.Application.Features.Exams.GetAttempt;
using BandUp.Application.Features.Exams.SaveAnswer;
using BandUp.Application.Features.Exams.StartAttempt;

namespace BandUp.Api.Endpoints.Attempts;

/// <summary>
/// The exam sitting. The server owns the clock: the browser never sends a time, and after a
/// refresh it asks for the state and gets the deadline and the time left back.
/// </summary>
public sealed class AttemptEndpoints : IEndpointGroup
{
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

    public sealed record StartAttemptRequest(Guid PassageId);

    public sealed record SaveAnswerRequest(string? SelectedAnswer);
}
