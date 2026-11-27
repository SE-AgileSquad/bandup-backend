using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.ReorderLessonExamples;

public sealed class ReorderLessonExamplesValidator : AbstractValidator<ReorderLessonExamplesCommand>
{
    public ReorderLessonExamplesValidator()
    {
        RuleFor(command => command.Input.ExampleIds).Cascade(CascadeMode.Stop).NotNull()
            .Must(ids => ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Length)
            .WithMessage("Example IDs must be valid and must not repeat.").OverridePropertyName("exampleIds");
    }
}
