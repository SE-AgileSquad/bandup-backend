using BandUp.Domain.Common;

namespace BandUp.Domain.Vocabulary;

public sealed class ReviewItem : Entity
{
    private const int FirstBox = 1;
    private static readonly int[] BoxIntervalsInDays = [1, 2, 4, 8, 16];

    private ReviewItem()
    {
    }

    private ReviewItem(Guid userId, ReviewItemType itemType, DateOnly scheduledOn)
    {
        UserId = userId;
        ItemType = itemType;
        LeitnerBox = FirstBox;
        DueDate = DueDateFor(scheduledOn, FirstBox);
    }

    public Guid UserId { get; private set; }
    public ReviewItemType ItemType { get; private set; }
    public Guid? VocabCardId { get; private set; }
    public Guid? ParaphrasePairId { get; private set; }
    public int LeitnerBox { get; private set; }
    public DateOnly DueDate { get; private set; }
    public int StreakCorrect { get; private set; }
    public DateTimeOffset? LastReviewedAt { get; private set; }

    public static ReviewItem ScheduleVocabulary(VocabCard card, DateOnly scheduledOn)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new ReviewItem(card.UserId, ReviewItemType.Vocab, scheduledOn) { VocabCardId = card.Id };
    }

    public static ReviewItem ScheduleParaphrase(Guid userId, ParaphrasePair pair, DateOnly scheduledOn)
    {
        ArgumentNullException.ThrowIfNull(pair);

        if (userId == Guid.Empty)
        {
            throw new DomainException("A paraphrase review requires a user.");
        }

        if (pair.Status != ParaphrasePairStatus.Approved)
        {
            throw new DomainException("A paraphrase pair must be approved before it can be scheduled.");
        }

        return new ReviewItem(userId, ReviewItemType.Paraphrase, scheduledOn) { ParaphrasePairId = pair.Id };
    }

    public bool IsDue(DateOnly onDate) => DueDate <= onDate;

    // The caller supplies the offset for the calendar used by the review queue.
    public void RecordReview(bool isCorrect, DateTimeOffset reviewedAt)
    {
        if (LastReviewedAt is { } lastReviewedAt && reviewedAt < lastReviewedAt)
        {
            throw new DomainException("A review cannot precede the last recorded review.");
        }

        int nextBox = isCorrect ? Math.Min(LeitnerBox + 1, BoxIntervalsInDays.Length) : FirstBox;
        int nextStreak = isCorrect ? checked(StreakCorrect + 1) : 0;
        DateOnly reviewDate = DateOnly.FromDateTime(reviewedAt.DateTime);
        DateOnly nextDueDate = DueDateFor(reviewDate, nextBox);

        LeitnerBox = nextBox;
        StreakCorrect = nextStreak;
        DueDate = nextDueDate;
        LastReviewedAt = reviewedAt.ToUniversalTime();
    }

    private static DateOnly DueDateFor(DateOnly onDate, int box) =>
        onDate.AddDays(BoxIntervalsInDays[box - FirstBox]);
}
