using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Learning;

/// <summary>
/// Learner-facing reads of lessons. Only published lessons are returned.
/// </summary>
public interface ILessonQueries
{
    Task<PagedResult<LessonSummaryDto>> ListPublishedLessonsAsync(
        QuestionType? questionType,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<LessonDto?> GetPublishedLessonAsync(Guid lessonId, CancellationToken cancellationToken);

    /// <summary>
    /// For each given question type, the published lesson its default mapping (the mapping
    /// without an error pattern) points to. A type with no such lesson is absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<QuestionType, Guid>> GetDefaultLessonIdsAsync(
        IReadOnlyCollection<QuestionType> questionTypes,
        CancellationToken cancellationToken);
}
