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

    [Fact]
    public void A_Draft_Can_Change_Question_Type_But_A_Published_Lesson_Keeps_Its_Type()
    {
        Lesson lesson = CreateLesson();
        lesson.Edit("Completion", "Use the text.", 2, QuestionType.SentenceCompletion);
        lesson.QuestionType.ShouldBe(QuestionType.SentenceCompletion);
        lesson.AddExample("Birds build nests.", "Nest building", 1);
        lesson.Publish();

        Should.Throw<DomainException>(() => lesson.Edit("Bad edit", "New body", 3, QuestionType.TrueFalseNotGiven));
        lesson.Title.ShouldBe("Completion");
        lesson.SortOrder.ShouldBe(2);
        lesson.Edit("Updated", "Updated body", 3);
        lesson.QuestionType.ShouldBe(QuestionType.SentenceCompletion);
        lesson.Title.ShouldBe("Updated");
    }

    [Fact]
    public void Archived_Lesson_Content_And_Examples_Are_Read_Only()
    {
        Lesson lesson = CreateLesson();
        LessonExample example = lesson.AddExample("Birds build nests.", "Nest building", 1);
        lesson.Publish();
        lesson.Archive();

        Should.Throw<DomainException>(() => lesson.Edit("Changed", "Body", 2));
        Should.Throw<DomainException>(() => lesson.AddExample("New evidence", "New paraphrase", 2));
        Should.Throw<DomainException>(() => lesson.EditExample(example.Id, "Changed", "Changed", 2));
        Should.Throw<DomainException>(() => lesson.ReorderExamples([example.Id]));
        example.EvidenceSentence.ShouldBe("Birds build nests.");
        example.SortOrder.ShouldBe(1);
    }

    [Fact]
    public void Reordering_Examples_Validates_All_Ids_Before_Changing_Any_Order()
    {
        Lesson lesson = CreateLesson();
        LessonExample first = lesson.AddExample("First", "First paraphrase", 10);
        LessonExample second = lesson.AddExample("Second", "Second paraphrase", 20);
        Should.Throw<DomainException>(() => lesson.ReorderExamples([second.Id, Guid.NewGuid()]));
        Should.Throw<DomainException>(() => lesson.ReorderExamples([second.Id, second.Id]));
        Should.Throw<DomainException>(() => lesson.ReorderExamples([second.Id]));
        first.SortOrder.ShouldBe(10);
        second.SortOrder.ShouldBe(20);

        lesson.ReorderExamples([second.Id, first.Id]);
        second.SortOrder.ShouldBe(1);
        first.SortOrder.ShouldBe(2);
    }

    [Fact]
    public void Publication_Readiness_Tracks_Examples_And_The_Lesson_Status()
    {
        Lesson lesson = CreateLesson();
        lesson.GetPublicationIssues().Single().Field.ShouldBe("examples");
        lesson.AddExample("Evidence", "Paraphrase", 1);
        lesson.GetPublicationIssues().ShouldBeEmpty();
        lesson.Publish();
        lesson.GetPublicationIssues().Single().Field.ShouldBe("status");
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
