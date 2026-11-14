using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Reading.GetPassage;

/// <summary>
/// Returns a published passage with its questions and, for each question, the lesson mapped to
/// its type. The mapping belongs to the learning module, so it is read through
/// <see cref="ILessonQueries"/> rather than from that module's tables (ADR 0015).
/// </summary>
public sealed class GetPassageHandler(IReadingQueries readingQueries, ILessonQueries lessonQueries)
    : IQueryHandler<GetPassageQuery, PassageDto>
{
    public async Task<Result<PassageDto>> HandleAsync(
        GetPassageQuery query,
        CancellationToken cancellationToken)
    {
        PassageDto? passage = await readingQueries.GetPublishedPassageAsync(query.PassageId, cancellationToken);

        if (passage is null)
        {
            return Result<PassageDto>.Failure(Error.NotFound("The passage does not exist or is not available."));
        }

        QuestionType[] types = [.. passage.Questions.Select(question => question.Type).Distinct()];

        IReadOnlyDictionary<QuestionType, Guid> lessonsByType =
            await lessonQueries.GetDefaultLessonIdsAsync(types, cancellationToken);

        QuestionDto[] questions =
        [
            .. passage.Questions.Select(question => lessonsByType.TryGetValue(question.Type, out Guid lessonId)
                ? question with { RelatedLessonId = lessonId }
                : question),
        ];

        return Result<PassageDto>.Success(passage with { Questions = questions });
    }
}
