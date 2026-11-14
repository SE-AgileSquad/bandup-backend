using BandUp.Application.Common;
using BandUp.Application.Features.Vocabulary.ListDueReviews;
using BandUp.Domain.Vocabulary;

namespace BandUp.Application.Features.Vocabulary;

public interface IVocabularyStore
{
    Task<bool> LockCardCreationAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> WordExistsAsync(Guid userId, string word, CancellationToken cancellationToken);
    Task<bool> PublishedQuestionExistsAsync(Guid questionId, CancellationToken cancellationToken);
    void Add(VocabCard card, ReviewItem review);
    Task<PagedResult<DueVocabularyReviewDto>> ListDueAsync(
        Guid userId, DateOnly today, PageRequest page, CancellationToken cancellationToken);
    Task<ReviewItem?> LoadReviewForUpdateAsync(Guid userId, Guid reviewId, CancellationToken cancellationToken);
}
