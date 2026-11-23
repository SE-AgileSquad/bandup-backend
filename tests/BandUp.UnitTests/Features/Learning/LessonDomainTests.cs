using BandUp.Domain.Common;
using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using Shouldly;

namespace BandUp.UnitTests.Features.Learning;

public sealed class LessonDomainTests
{
    [Fact]
    public void A_Lesson_Can_Be_Published_Once_It_Has_An_Example()
    {
        Lesson lesson = CreateLesson();

        lesson.Status.ShouldBe(LessonStatus.Draft);
        Should.Throw<DomainException>(() => lesson.Publish());
        lesson.Status.ShouldBe(LessonStatus.Draft);

        LessonExample example = lesson.AddExample("Some birds nest on tall buildings.", "High-rise nesting", 2);

        lesson.Publish();

        lesson.Status.ShouldBe(LessonStatus.Published);
        lesson.Examples.Single().ShouldBeSameAs(example);
        example.LessonId.ShouldBe(lesson.Id);
        example.SortOrder.ShouldBe(2);
    }

    [Fact]
    public void Archiving_Requires_Publication_And_Preserves_Content()
    {
        Lesson lesson = CreateLesson();
        Should.Throw<DomainException>(() => lesson.Archive());
        lesson.Status.ShouldBe(LessonStatus.Draft);
        LessonExample example = lesson.AddExample("The birds build nests.", "Nest building", 1);
        lesson.Publish();

        lesson.Archive();

        lesson.Status.ShouldBe(LessonStatus.Archived);
        lesson.Title.ShouldBe("False vs Not Given");
        lesson.Examples.Single().ShouldBeSameAs(example);
        Should.Throw<DomainException>(() => lesson.Publish());
    }

    [Theory]
    [InlineData("", "paraphrase")]
    [InlineData("evidence", " ")]
    public void An_Incomplete_Example_Is_Rejected_Without_Changing_The_Lesson(string evidence, string paraphrase)
    {
        Lesson lesson = CreateLesson();

        Should.Throw<DomainException>(() => lesson.AddExample(evidence, paraphrase, 1));
        lesson.Examples.ShouldBeEmpty();

        LessonExample example = lesson.AddExample("Some birds nest on tall buildings.", "High-rise nesting", 1);

        Should.Throw<DomainException>(() => example.Edit(evidence, paraphrase, 2));
        example.EvidenceSentence.ShouldBe("Some birds nest on tall buildings.");
        example.Paraphrase.ShouldBe("High-rise nesting");
        example.SortOrder.ShouldBe(1);
    }

    [Fact]
    public void Lesson_Maps_Use_The_Lessons_Question_Type_And_Null_For_The_Default()
    {
        Lesson lesson = CreateLesson();

        LessonMap defaultMapping = LessonMap.Create(lesson);
        LessonMap specificMapping = LessonMap.Create(lesson, " NG_AS_FALSE ");

        defaultMapping.ErrorPattern.ShouldBeNull();
        defaultMapping.QuestionType.ShouldBe(QuestionType.TrueFalseNotGiven);
        defaultMapping.LessonId.ShouldBe(lesson.Id);
        specificMapping.ErrorPattern.ShouldBe("NG_AS_FALSE");
        specificMapping.QuestionType.ShouldBe(defaultMapping.QuestionType);
        specificMapping.LessonId.ShouldBe(lesson.Id);
        Should.Throw<DomainException>(() => LessonMap.Create(lesson, " "));
    }

    [Fact]
    public void Completion_Records_The_User_Lesson_And_Supplied_Time()
    {
        Lesson lesson = CreateLesson();
        Guid userId = Guid.CreateVersion7();
        DateTimeOffset now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

        LessonProgress progress = LessonProgress.Complete(userId, lesson.Id, now);

        progress.UserId.ShouldBe(userId);
        progress.LessonId.ShouldBe(lesson.Id);
        progress.CompletedAt.ShouldBe(now);
        Should.Throw<DomainException>(() => LessonProgress.Complete(Guid.Empty, lesson.Id, now));
        Should.Throw<DomainException>(() => LessonProgress.Complete(userId, Guid.Empty, now));
    }

    private static Lesson CreateLesson() =>
        Lesson.CreateDraft(
            "False vs Not Given",
            "Compare the claim with the evidence.",
            QuestionType.TrueFalseNotGiven,
            1);
}
