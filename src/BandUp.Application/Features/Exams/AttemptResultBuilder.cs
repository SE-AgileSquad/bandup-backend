using BandUp.Application.Features.Learning;
using BandUp.Domain.Content;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// Builds the result of a submitted attempt. The first submission and every replay of it go
/// through here, which is what makes a retried submit return exactly the same body.
/// </summary>
internal sealed class AttemptResultBuilder(IAnswerKeyQueries answerKeyQueries, ILessonQueries lessonQueries)
{
    public async Task<AttemptResultDto> BuildAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        IReadOnlyList<QuestionReview> review =
            await answerKeyQueries.GetReviewAsync(attempt.PassageId, cancellationToken);

        QuestionType[] types = [.. review.Select(item => item.Type).Distinct()];

        IReadOnlyDictionary<QuestionType, Guid> lessonsByType =
            await lessonQueries.GetDefaultLessonIdsAsync(types, cancellationToken);

        return AttemptResultDto.From(attempt, review, lessonsByType);
    }
}
