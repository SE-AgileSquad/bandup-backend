using BandUp.Domain.Common;
using BandUp.Domain.Vocabulary;
using Shouldly;

namespace BandUp.UnitTests.Features.Vocabulary;

public sealed class VocabularyReviewDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.DateTime);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();

    [Fact]
    public void A_Vocabulary_Review_Belongs_To_The_Card_Owner()
    {
        Guid questionId = Guid.CreateVersion7();
        VocabCard card = VocabCard.Create(UserId, " nest ", "The birds build nests.", Now, questionId);

        ReviewItem review = ReviewItem.ScheduleVocabulary(card, Today);

        card.Word.ShouldBe("nest");
        card.SourceQuestionId.ShouldBe(questionId);
        review.UserId.ShouldBe(card.UserId);
        review.ItemType.ShouldBe(ReviewItemType.Vocab);
        review.VocabCardId.ShouldBe(card.Id);
        review.ParaphrasePairId.ShouldBeNull();
        review.LeitnerBox.ShouldBe(1);
        review.StreakCorrect.ShouldBe(0);
        review.LastReviewedAt.ShouldBeNull();
        review.DueDate.ShouldBe(Today.AddDays(1));
        review.IsDue(Today).ShouldBeFalse();
        review.IsDue(Today.AddDays(1)).ShouldBeTrue();
    }

    [Fact]
    public void A_Paraphrase_Must_Be_Approved_To_Schedule_A_Review()
    {
        ParaphrasePair pair = ParaphrasePair.CreateDraft(
            "The birds build nests.",
            "The birds make homes.",
            AdminId);

        pair.QuestionId.ShouldBeNull();
        Should.Throw<DomainException>(() => ReviewItem.ScheduleParaphrase(UserId, pair, Today));

        pair.Approve();
        pair.Edit(pair.SourceSentence, pair.EquivalentSentence);
        pair.Status.ShouldBe(ParaphrasePairStatus.Approved);
        ReviewItem review = ReviewItem.ScheduleParaphrase(UserId, pair, Today);

        review.UserId.ShouldBe(UserId);
        review.ItemType.ShouldBe(ReviewItemType.Paraphrase);
        review.ParaphrasePairId.ShouldBe(pair.Id);
        review.VocabCardId.ShouldBeNull();
        review.DueDate.ShouldBe(Today.AddDays(1));

        pair.Edit("The birds build nests.", "The birds construct their own shelters.");
        pair.Status.ShouldBe(ParaphrasePairStatus.Draft);
        Should.Throw<DomainException>(() => ReviewItem.ScheduleParaphrase(UserId, pair, Today));
    }

    [Fact]
    public void Correct_Reviews_Advance_Through_The_Intervals_And_Stay_In_The_Last_Box()
    {
        ReviewItem review = CreateVocabularyReview();
        int[] expectedIntervals = [2, 4, 8, 16, 16];

        for (int index = 0; index < expectedIntervals.Length; index++)
        {
            DateOnly reviewDate = review.DueDate;
            DateTimeOffset reviewedAt = new(reviewDate.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);

            review.RecordReview(isCorrect: true, reviewedAt);

            review.LeitnerBox.ShouldBe(Math.Min(index + 2, 5));
            review.StreakCorrect.ShouldBe(index + 1);
            review.DueDate.ShouldBe(reviewDate.AddDays(expectedIntervals[index]));
            review.LastReviewedAt.ShouldBe(reviewedAt);
        }
    }

    [Fact]
    public void An_Incorrect_Review_Resets_The_Box_And_Schedules_From_The_Actual_Review_Date()
    {
        ReviewItem review = CreateVocabularyReview();
        review.RecordReview(isCorrect: true, Now.AddDays(1));
        DateTimeOffset reviewedAt = new(2026, 11, 18, 0, 15, 0, TimeSpan.FromMinutes(210));

        review.RecordReview(isCorrect: false, reviewedAt);

        review.LeitnerBox.ShouldBe(1);
        review.StreakCorrect.ShouldBe(0);
        review.DueDate.ShouldBe(new DateOnly(2026, 11, 19));
        review.LastReviewedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        review.LastReviewedAt.ShouldBe(reviewedAt.ToUniversalTime());
    }

    [Fact]
    public void An_Out_Of_Order_Review_Does_Not_Overwrite_The_Current_Schedule()
    {
        ReviewItem review = CreateVocabularyReview();
        DateTimeOffset reviewedAt = Now.AddDays(1);
        review.RecordReview(isCorrect: true, reviewedAt);
        DateOnly dueDate = review.DueDate;

        Should.Throw<DomainException>(() => review.RecordReview(isCorrect: false, Now));

        review.LastReviewedAt.ShouldBe(reviewedAt);
        review.DueDate.ShouldBe(dueDate);
        review.LeitnerBox.ShouldBe(2);
        review.StreakCorrect.ShouldBe(1);
    }

    private static ReviewItem CreateVocabularyReview() =>
        ReviewItem.ScheduleVocabulary(VocabCard.Create(UserId, "nest", "The birds build nests.", Now), Today);
}
