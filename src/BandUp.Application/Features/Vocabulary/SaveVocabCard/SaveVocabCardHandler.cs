using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Vocabulary;

namespace BandUp.Application.Features.Vocabulary.SaveVocabCard;

public sealed class SaveVocabCardHandler(IVocabularyStore store, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SaveVocabCardCommand, SavedVocabCardDto>
{
    public async Task<Result<SavedVocabCardDto>> HandleAsync(
        SaveVocabCardCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return Result<SavedVocabCardDto>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        if (!await store.LockCardCreationAsync(userId, cancellationToken))
        {
            return Result<SavedVocabCardDto>.Failure(Error.Forbidden("An active account is required."));
        }

        if (command.SourceQuestionId is { } questionId
            && !await store.PublishedQuestionExistsAsync(questionId, cancellationToken))
        {
            return Result<SavedVocabCardDto>.Failure(Error.NotFound("Published source question not found."));
        }

        DateTimeOffset now = clock.GetUtcNow();
        VocabCard card = VocabCard.Create(userId, command.Word, command.ContextSentence, now, command.SourceQuestionId);
        if (await store.WordExistsAsync(userId, card.Word, cancellationToken))
        {
            return Result<SavedVocabCardDto>.Failure(Error.Conflict("This word is already in your vocabulary."));
        }

        ReviewItem review = ReviewItem.ScheduleVocabulary(card, DateOnly.FromDateTime(now.UtcDateTime));
        store.Add(card, review);
        return Result<SavedVocabCardDto>.Success(new SavedVocabCardDto(card.Id, review.Id, review.DueDate));
    }
}
