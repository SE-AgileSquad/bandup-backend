using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Vocabulary;

namespace BandUp.Application.Features.Vocabulary.RecordVocabularyReview;

public sealed class RecordVocabularyReviewHandler(IVocabularyStore store, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<RecordVocabularyReviewCommand, VocabularyReviewDto>
{
    public async Task<Result<VocabularyReviewDto>> HandleAsync(
        RecordVocabularyReviewCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return Result<VocabularyReviewDto>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        ReviewItem? review = await store.LoadReviewForUpdateAsync(userId, command.ReviewId, cancellationToken);
        if (review is null)
        {
            return Result<VocabularyReviewDto>.Failure(Error.NotFound("Vocabulary review not found."));
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (!review.IsDue(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return Result<VocabularyReviewDto>.Failure(Error.Conflict("This review is not due yet."));
        }

        review.RecordReview(command.IsCorrect!.Value, now);
        return Result<VocabularyReviewDto>.Success(new VocabularyReviewDto(
            review.Id, review.LeitnerBox, review.DueDate, review.StreakCorrect, review.LastReviewedAt));
    }
}
