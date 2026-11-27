using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing;

public sealed record ListWritingSubmissionsQuery(PageRequest Page) : IQuery<PagedResult<WritingSubmissionHistoryItemDto>>;
public sealed record GetWritingStatusQuery(Guid Id) : IQuery<WritingEvaluationStatusDto>;
public sealed record GetWritingDetailQuery(Guid Id) : IQuery<WritingSubmissionDetailDto>;
public sealed record GetWritingResultQuery(Guid Id) : IQuery<WritingEvaluationResultDto>;

public interface IWritingSubmissionQueries
{
    Task<PagedResult<WritingSubmissionHistoryItemDto>> ListAsync(Guid owner, PageRequest page, CancellationToken ct);
    Task<WritingEvaluationStatusDto?> FindStatusAsync(Guid owner, Guid id, CancellationToken ct);
    Task<WritingSubmissionDetailDto?> FindDetailAsync(Guid owner, Guid id, CancellationToken ct);
    Task<WritingFeedbackDto?> FindSuccessfulFeedbackAsync(Guid owner, Guid id, CancellationToken ct);
}

internal sealed class ReadWritingSubmissionsHandler(ICurrentUser currentUser, IWritingSubmissionQueries store) :
    IQueryHandler<ListWritingSubmissionsQuery, PagedResult<WritingSubmissionHistoryItemDto>>,
    IQueryHandler<GetWritingStatusQuery, WritingEvaluationStatusDto>,
    IQueryHandler<GetWritingDetailQuery, WritingSubmissionDetailDto>,
    IQueryHandler<GetWritingResultQuery, WritingEvaluationResultDto>
{
    private Guid? Owner => currentUser.IsAuthenticated ? currentUser.UserId : null;

    public async Task<Result<PagedResult<WritingSubmissionHistoryItemDto>>> HandleAsync(ListWritingSubmissionsQuery query, CancellationToken ct) =>
        Owner is { } owner
            ? Result<PagedResult<WritingSubmissionHistoryItemDto>>.Success(await store.ListAsync(owner, query.Page, ct))
            : Unauthorized<PagedResult<WritingSubmissionHistoryItemDto>>();

    public async Task<Result<WritingEvaluationStatusDto>> HandleAsync(GetWritingStatusQuery query, CancellationToken ct)
    {
        if (Owner is not { } owner)
        {
            return Unauthorized<WritingEvaluationStatusDto>();
        }
        var status = await store.FindStatusAsync(owner, query.Id, ct);
        return status is null ? NotFound<WritingEvaluationStatusDto>() : Result<WritingEvaluationStatusDto>.Success(status.Status switch
        {
            WritingEvaluationStatus.Succeeded => status with { ResultUrl = $"/api/writing/submissions/{status.SubmissionId}/result" },
            WritingEvaluationStatus.Failed => status with { Failure = new(WritingErrorCodes.EvaluationFailed) },
            _ => status,
        });
    }

    public async Task<Result<WritingSubmissionDetailDto>> HandleAsync(GetWritingDetailQuery query, CancellationToken ct)
    {
        if (Owner is not { } owner)
        {
            return Unauthorized<WritingSubmissionDetailDto>();
        }
        var detail = await store.FindDetailAsync(owner, query.Id, ct);
        return detail is null ? NotFound<WritingSubmissionDetailDto>() : Result<WritingSubmissionDetailDto>.Success(detail);
    }

    public async Task<Result<WritingEvaluationResultDto>> HandleAsync(GetWritingResultQuery query, CancellationToken ct)
    {
        if (Owner is not { } owner)
        {
            return Unauthorized<WritingEvaluationResultDto>();
        }
        var status = await store.FindStatusAsync(owner, query.Id, ct);
        if (status is null)
        {
            return NotFound<WritingEvaluationResultDto>();
        }
        if (status.Status != WritingEvaluationStatus.Succeeded)
        {
            var error = status.Status == WritingEvaluationStatus.Failed
                ? new Error(WritingErrorCodes.EvaluationFailed, "The evaluation failed.", ErrorType.Conflict)
                : new Error(WritingErrorCodes.EvaluationNotReady, "The evaluation result is not ready.", ErrorType.Conflict);
            return Result<WritingEvaluationResultDto>.Failure(error);
        }
        var feedback = await store.FindSuccessfulFeedbackAsync(owner, query.Id, ct);
        if (feedback is null || status.CompletedAt is not { } completed)
        {
            throw new InvalidOperationException("A successful Writing submission has an incomplete result.");
        }
        return Result<WritingEvaluationResultDto>.Success(new(status.SubmissionId, completed, feedback));
    }

    private static Result<T> Unauthorized<T>() => Result<T>.Failure(Error.Unauthorized("Sign in to access this resource."));
    private static Result<T> NotFound<T>() => Result<T>.Failure(Error.NotFound("Writing submission not found."));
}
