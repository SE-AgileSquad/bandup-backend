using BandUp.Application.Common;
using BandUp.Application.Features.Vocabulary;
using BandUp.Application.Features.Vocabulary.ListDueReviews;
using BandUp.Domain.Content;
using BandUp.Domain.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfVocabularyStore(BandUpDbContext context) : IVocabularyStore
{
    public async Task<bool> LockCardCreationAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Serialize duplicate checks for one owner through the command transaction.
        List<Guid> users = await context.Database.SqlQuery<Guid>(
            $"SELECT id FROM identity.users WHERE id = {userId} AND is_active FOR NO KEY UPDATE")
            .ToListAsync(cancellationToken);
        return users.Count == 1;
    }

    public Task<bool> WordExistsAsync(Guid userId, string word, CancellationToken cancellationToken) =>
        context.VocabCards.AnyAsync(card => card.UserId == userId && card.Word == word, cancellationToken);

    public Task<bool> PublishedQuestionExistsAsync(Guid questionId, CancellationToken cancellationToken) =>
        (from question in context.Questions
         join passage in context.Passages on question.PassageId equals passage.Id
         where question.Id == questionId && question.Status == QuestionStatus.Published
             && passage.Status == PassageStatus.Published
         select question.Id).AnyAsync(cancellationToken);

    public void Add(VocabCard card, ReviewItem review)
    {
        context.VocabCards.Add(card);
        context.ReviewItems.Add(review);
    }

    public Task<PagedResult<DueVocabularyReviewDto>> ListDueAsync(
        Guid userId, DateOnly today, PageRequest page, CancellationToken cancellationToken) =>
        EfStoreOperations.ReadPageAsync(
            from review in context.ReviewItems.AsNoTracking()
            join card in context.VocabCards.AsNoTracking()
                on new { review.UserId, Id = review.VocabCardId } equals new { card.UserId, Id = (Guid?)card.Id }
            where review.UserId == userId && review.ItemType == ReviewItemType.Vocab && review.DueDate <= today
            orderby review.DueDate, review.Id
            select new DueVocabularyReviewDto(
                review.Id, card.Id, card.Word, card.ContextSentence, review.DueDate, review.LeitnerBox),
            page, cancellationToken);

    public async Task<ReviewItem?> LoadReviewForUpdateAsync(
        Guid userId, Guid reviewId, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT id FROM vocabulary.review_items
            WHERE id = {reviewId} AND user_id = {userId} AND item_type = {nameof(ReviewItemType.Vocab)}
            FOR UPDATE
            """, cancellationToken);
        return await context.ReviewItems.SingleOrDefaultAsync(
            review => review.Id == reviewId && review.UserId == userId && review.ItemType == ReviewItemType.Vocab,
            cancellationToken);
    }
}
