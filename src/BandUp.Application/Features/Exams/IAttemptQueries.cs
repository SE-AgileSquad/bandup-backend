using BandUp.Application.Common;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// Read-only questions the exam module asks that reach into other modules' tables (passages,
/// paraphrase pairs) or span many attempts. Like <see cref="IAnswerKeyQueries"/>, the
/// implementation lives in the infrastructure layer so the exam handlers never touch another
/// module's tables themselves (ADR 0015).
/// </summary>
public interface IAttemptQueries
{
    /// <summary>
    /// The title of the passage whatever its status, so the result of an old attempt still names
    /// a passage that has since been archived. Null when the passage does not exist.
    /// </summary>
    Task<string?> GetPassageTitleAsync(Guid passageId, CancellationToken cancellationToken);

    /// <summary>
    /// The approved paraphrase pairs of the given questions, keyed by question. Draft pairs are
    /// never returned, and a question without pairs is simply absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ParaphrasePairDto>>> GetApprovedParaphrasePairsAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// The learner's submitted attempts, newest first.
    /// </summary>
    Task<PagedResult<AttemptHistoryItemDto>> ListSubmittedAsync(
        Guid userId,
        PageRequest page,
        CancellationToken cancellationToken);

    /// <summary>
    /// One entry for each passage the learner is working on or has finished, judged by the clock
    /// <paramref name="now"/>: a running sitting wins over a finished one, and a sitting that ran
    /// out of time counts as neither.
    /// </summary>
    Task<IReadOnlyList<PassageAttemptSummaryDto>> GetPassageSummariesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
