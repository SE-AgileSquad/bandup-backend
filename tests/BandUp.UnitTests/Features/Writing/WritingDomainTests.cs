using BandUp.Domain.Common;
using BandUp.Domain.Writing;

namespace BandUp.UnitTests.Features.Writing;

public sealed class WritingDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset Deadline = Now.AddMinutes(5);
    private static readonly string Essay = string.Join(" ", Enumerable.Repeat("cafe\u0301", 100));

    private static WritingPrompt Prompt()
    {
        WritingPrompt prompt = new(Guid.NewGuid(), "Cities", "Discuss urban growth.", Now);
        prompt.Approve(Now);
        prompt.Activate(Now);
        return prompt;
    }

    private static WritingSubmission Accept(WritingPrompt prompt, string essay) =>
        new(Guid.NewGuid(), Guid.NewGuid(), prompt, essay, Now, Deadline);

    private static WritingFeedback Feedback(Guid id)
    {
        var criterion = new CriterionFeedback(6, "Explanation", [], [], ["Give examples"]);
        return new WritingFeedback(id, "Summary", criterion, criterion, criterion, criterion);
    }

    [Fact]
    public void PromptEligibilityAndSnapshotAreIndependentAfterAcceptance()
    {
        Assert.Throws<DomainException>(() => new WritingPrompt(Guid.Empty, "Title", "Text", Now));
        Assert.Throws<DomainException>(() => new WritingPrompt(Guid.NewGuid(), " ", "Text", Now));
        Assert.Throws<DomainException>(() => new WritingPrompt(Guid.NewGuid(), "Title", "", Now));
        var prompt = new WritingPrompt(Guid.NewGuid(), "Cities", "Discuss urban growth.", Now);
        Assert.Throws<DomainException>(() => prompt.Activate(Now));
        prompt.Approve(Now);
        Assert.Throws<DomainException>(() => Accept(prompt, Essay));
        prompt.Activate(Now);
        var submission = Accept(prompt, Essay);
        prompt.Retire(Now);
        Assert.Throws<DomainException>(() => Accept(prompt, Essay));
        Assert.Equal(new WritingPromptSnapshot(prompt.Id, prompt.Title, prompt.PromptText), submission.PromptSnapshot);
        Assert.Equal(Essay, submission.EssayText);
        Assert.True(submission.MatchesPayload(prompt.Id, Essay));
        Assert.False(submission.MatchesPayload(prompt.Id, Essay.Normalize()));
        Assert.False(submission.MatchesPayload(Guid.NewGuid(), Essay));
    }

    [Fact]
    public void ScalarLimitCountsSupplementaryCharactersAndRejectsMalformedText()
    {
        string maximum = Essay + string.Concat(Enumerable.Repeat("😊", EssayPolicy.MaxScalars - Essay.Length));
        Assert.Equal(maximum, Accept(Prompt(), maximum).EssayText);
        foreach (string invalid in new[] { maximum + "!", Essay + "\ud800", Essay + "\udfff", Essay + "\0" })
        {
            Assert.Throws<DomainException>(() => Accept(Prompt(), invalid));
        }
    }

    [Fact]
    public void QueueDeadlinePartitionsClaimAndExpiry()
    {
        var submission = Accept(Prompt(), Essay);
        Assert.Null(submission.CompletedAt);
        Assert.Equal(WritingEvaluationStatus.Queued, submission.Status);
        Assert.Throws<DomainException>(() => submission.StartProcessing(Now.AddTicks(-1)));
        Assert.Throws<DomainException>(() => submission.StartProcessing(Deadline));
        Assert.Throws<DomainException>(() => submission.Fail(Deadline.AddTicks(-1)));
        Assert.Throws<DomainException>(() => submission.Succeed(Feedback(submission.Id), Now));
        submission.Fail(Deadline);
        AssertTerminal(submission, WritingEvaluationStatus.Failed, Deadline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProcessingCompletesOnlyOnceAndInChronologicalOrder(bool succeeds)
    {
        var submission = Accept(Prompt(), Essay);
        var start = Deadline.AddTicks(-1);
        submission.StartProcessing(start);
        Assert.Equal(WritingEvaluationStatus.Processing, submission.Status);
        Assert.Null(submission.CompletedAt);
        Assert.Throws<DomainException>(() => submission.StartProcessing(start));
        Assert.Throws<DomainException>(() => submission.Fail(Now));
        Assert.Throws<DomainException>(() => submission.Succeed(Feedback(submission.Id), Now));
        Assert.Throws<DomainException>(() => submission.Succeed(Feedback(Guid.NewGuid()), start));
        if (succeeds)
        {
            submission.Succeed(Feedback(submission.Id), Deadline);
        }
        else
        {
            submission.Fail(Deadline);
        }
        AssertTerminal(submission, succeeds ? WritingEvaluationStatus.Succeeded : WritingEvaluationStatus.Failed, Deadline);
    }

    private static void AssertTerminal(WritingSubmission submission, WritingEvaluationStatus expected, DateTimeOffset now)
    {
        Assert.Throws<DomainException>(() => submission.StartProcessing(now));
        Assert.Throws<DomainException>(() => submission.Fail(now));
        Assert.Throws<DomainException>(() => submission.Succeed(Feedback(submission.Id), now));
        Assert.Equal(expected, submission.Status);
        Assert.Equal(now, submission.CompletedAt);
    }

    [Fact]
    public void FeedbackOwnsItsRecommendationsAndDerivesOverall()
    {
        string[] advice = ["Revise the opening."];
        var lower = new CriterionFeedback(6, "Reason", [], [], advice);
        var higher = new CriterionFeedback(6.5m, "Reason", [], [], advice);
        var feedback = new WritingFeedback(Guid.NewGuid(), "Summary", lower, lower, higher, higher);
        advice[0] = "changed";
        Assert.Equal("Revise the opening.", Assert.Single(feedback.Tr.Recommendations));
        Assert.Equal(6.5m, feedback.EstimatedOverallBand);
        Assert.Throws<DomainException>(() => new CriterionFeedback(6.25m, "Reason", [], [], advice));
        Assert.Throws<DomainException>(() => new CriterionFeedback(6, "Reason", [], [], []));
    }
}
