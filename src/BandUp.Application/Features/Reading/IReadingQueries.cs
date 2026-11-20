using BandUp.Application.Common;

namespace BandUp.Application.Features.Reading;

/// <summary>
/// Learner-facing reads of reading content. Only published passages and questions are ever
/// returned, and nothing here projects an answer key.
/// </summary>
public interface IReadingQueries
{
    Task<PagedResult<PassageSummaryDto>> ListPublishedPassagesAsync(
        PageRequest page,
        CancellationToken cancellationToken);

    /// <summary>
    /// The published passage with its published questions, or null when it does not exist or is
    /// not published. Questions come back without <see cref="QuestionDto.RelatedLessonId"/>.
    /// </summary>
    Task<PassageDto?> GetPublishedPassageAsync(Guid passageId, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of every question of a passage, whatever its publication state. An attempt that
    /// is already running must stay answerable even if the passage is archived meanwhile.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetQuestionIdsAsync(Guid passageId, CancellationToken cancellationToken);
}
