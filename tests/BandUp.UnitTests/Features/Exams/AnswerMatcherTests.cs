using BandUp.Domain.Content;
using BandUp.Domain.Exams;
using Shouldly;

namespace BandUp.UnitTests.Features.Exams;

/// <summary>
/// How a learner's answer is compared with the answer key: case and spacing are forgiven,
/// spelling is not.
/// </summary>
public sealed class AnswerMatcherTests
{
    private static ScoringKey Key(QuestionType type, string correct, params string[] variants) =>
        new(Guid.CreateVersion7(), type, correct, variants);

    [Theory]
    [InlineData("True")]
    [InlineData("true")]
    [InlineData("  TRUE  ")]
    public void True_False_Not_Given_Ignores_Case_And_Surrounding_Space(string answer)
    {
        AnswerMatcher.IsCorrect(answer, Key(QuestionType.TrueFalseNotGiven, "True")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("not given")]
    [InlineData("NOT   GIVEN")]
    [InlineData(" Not Given ")]
    public void Not_Given_Ignores_Case_And_Repeated_Space(string answer)
    {
        AnswerMatcher.IsCorrect(answer, Key(QuestionType.TrueFalseNotGiven, "Not Given")).ShouldBeTrue();
    }

    [Fact]
    public void A_Different_Option_Is_Wrong()
    {
        AnswerMatcher.IsCorrect("False", Key(QuestionType.TrueFalseNotGiven, "True")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Answer_Is_Never_Correct(string answer)
    {
        AnswerMatcher.IsCorrect(answer, Key(QuestionType.SentenceCompletion, "nests")).ShouldBeFalse();
    }

    [Fact]
    public void Sentence_Completion_Accepts_The_Correct_Answer_And_Its_Variants()
    {
        ScoringKey key = Key(QuestionType.SentenceCompletion, "nests", "a nest", "bird nests");

        AnswerMatcher.IsCorrect("Nests", key).ShouldBeTrue();
        AnswerMatcher.IsCorrect("A  Nest", key).ShouldBeTrue();
        AnswerMatcher.IsCorrect("bird nests", key).ShouldBeTrue();
    }

    [Fact]
    public void Spelling_Mistakes_Are_Not_Forgiven()
    {
        AnswerMatcher.IsCorrect("nest", Key(QuestionType.SentenceCompletion, "nests")).ShouldBeFalse();
    }
}
