using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.EditLessonExample;

public sealed class EditLessonExampleValidator : AbstractValidator<EditLessonExampleCommand>
{
    public EditLessonExampleValidator()
    {
        RuleFor(command => command.Input.EvidenceSentence).NotEmpty().OverridePropertyName("evidenceSentence");
        RuleFor(command => command.Input.Paraphrase).NotEmpty().OverridePropertyName("paraphrase");
    }
}
