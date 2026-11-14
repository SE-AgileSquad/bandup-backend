using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Vocabulary.ListDueReviews;

public sealed class ListDueReviewsHandler(IVocabularyStore store, ICurrentUser currentUser, TimeProvider clock)
    : IQueryHandler<ListDueReviewsQuery, PagedResult<DueVocabularyReviewDto>>
{
    public async Task<Result<PagedResult<DueVocabularyReviewDto>>> HandleAsync(
        ListDueReviewsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return Result<PagedResult<DueVocabularyReviewDto>>.Failure(
                Error.Unauthorized("An authenticated user is required."));
        }

        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return Result<PagedResult<DueVocabularyReviewDto>>.Success(
            await store.ListDueAsync(userId, today, query.Page, cancellationToken));
    }
}
