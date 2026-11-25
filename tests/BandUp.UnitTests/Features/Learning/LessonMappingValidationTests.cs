using BandUp.Application.Features.Learning.Admin.Mappings;
using BandUp.Domain.Content;
using BandUp.Domain.Common;
using BandUp.Domain.Learning;
using Shouldly;

namespace BandUp.UnitTests.Features.Learning;

public sealed class LessonMappingValidationTests
{
    [Fact]
    public void Only_A_Complete_Unique_Set_Of_Supported_Types_With_Valid_Ids_Is_Accepted()
    {
        SaveLessonMappingsValidator validator = new();
        LessonMappingSelection tfng = new(QuestionType.TrueFalseNotGiven, Guid.NewGuid());
        LessonMappingSelection sc = new(QuestionType.SentenceCompletion, null);
        foreach (LessonMappingSelection[]? items in new LessonMappingSelection[]?[]
            { null, [], [tfng], [tfng, tfng], [tfng, sc with { QuestionType = (QuestionType)99 }],
                [tfng with { LessonId = Guid.Empty }, sc], [tfng with { QuestionType = null }, sc], [null!, sc] })
        {
            validator.Validate(new SaveLessonMappingsCommand(new(items!))).IsValid.ShouldBeFalse();
        }
        validator.Validate(new SaveLessonMappingsCommand(new([tfng, sc]))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Retargeting_Preserves_Identity_And_Error_Pattern_And_Rejects_A_Different_Type()
    {
        Lesson first = Lesson.CreateDraft("First", "Text", QuestionType.TrueFalseNotGiven, 0);
        Lesson second = Lesson.CreateDraft("Second", "Text", QuestionType.TrueFalseNotGiven, 0);
        Lesson wrong = Lesson.CreateDraft("Wrong", "Text", QuestionType.SentenceCompletion, 0);
        LessonMap map = LessonMap.Create(first, "NG_AS_FALSE");
        Guid id = map.Id;
        map.ChangeLesson(second);
        map.Id.ShouldBe(id);
        map.LessonId.ShouldBe(second.Id);
        map.ErrorPattern.ShouldBe("NG_AS_FALSE");
        Should.Throw<DomainException>(() => map.ChangeLesson(wrong));
        map.LessonId.ShouldBe(second.Id);
    }
}
