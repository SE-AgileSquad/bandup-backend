using BandUp.Domain.Common;
using BandUp.Domain.Content;
using BandUp.Domain.Writing;
using Shouldly;

namespace BandUp.UnitTests.Features.Content;

public sealed class ActivityDurationTests
{
    [Fact]
    public void Passage_Duration_Only_Changes_Do_Not_Invalidate_Answers_And_Are_Copied_To_Revisions()
    {
        Guid actor = Guid.NewGuid();
        Passage passage = Passage.CreateDraft("Title", "Birds build nests.", "Team", "Original", actor);
        passage.DurationMinutes.ShouldBe(20);
        Question question = passage.AddQuestion(QuestionType.TrueFalseNotGiven, "Birds build nests.", 1);
        question.SetAnswerKey("True");
        question.SetExplanation("The text states this.", "Birds build nests.");
        question.VerifyAnswerKey(actor, DateTimeOffset.UtcNow);
        passage.EditDraft(passage.Title, passage.Body, passage.SourceNote, passage.UsageBasis, 25);
        question.AnswerKey!.IsVerified.ShouldBeTrue();
        passage.EditDraft(passage.Title, passage.Body, passage.SourceNote, passage.UsageBasis);
        passage.DurationMinutes.ShouldBe(25);
        passage.Publish(DateTimeOffset.UtcNow);
        passage.CreateRevision(actor).DurationMinutes.ShouldBe(25);
    }

    [Fact]
    public void Writing_Duration_Is_Preserved_On_Omission_And_Duplication_And_Updates_The_Timestamp_When_Changed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid actor = Guid.NewGuid();
        WritingPrompt prompt = WritingPrompt.CreateDraft("Title", "Text", "Opinion", 100, 350, actor, now);
        prompt.DurationMinutes.ShouldBe(40);
        prompt.EditDraft("Title", "Text", "Opinion", 100, 350, now.AddMinutes(1), 30);
        prompt.UpdatedAt.ShouldBe(now.AddMinutes(1));
        prompt.EditDraft("Title", "Text", "Opinion", 100, 350, now.AddMinutes(2));
        prompt.UpdatedAt.ShouldBe(now.AddMinutes(1));
        prompt.DurationMinutes.ShouldBe(30);
        prompt.Duplicate(actor, now).DurationMinutes.ShouldBe(30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_Durations_Do_Not_Mutate_Drafts(int minutes)
    {
        Guid actor = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Passage passage = Passage.CreateDraft("Title", "Body", "Team", "Original", actor);
        Should.Throw<DomainException>(() => passage.EditDraft("Changed", "Changed", "Team", "Original", minutes));
        passage.Title.ShouldBe("Title");
        passage.DurationMinutes.ShouldBe(20);
        WritingPrompt prompt = WritingPrompt.CreateDraft("Title", "Text", "Opinion", 100, 350, actor, now);
        Should.Throw<DomainException>(() => prompt.EditDraft("Changed", "Changed", "Opinion", 100, 350, now, minutes));
        prompt.Title.ShouldBe("Title");
        prompt.DurationMinutes.ShouldBe(40);
    }
}
