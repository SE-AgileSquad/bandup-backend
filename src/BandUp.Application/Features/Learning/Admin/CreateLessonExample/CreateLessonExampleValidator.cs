using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.CreateLessonExample;

public sealed class CreateLessonExampleValidator : AbstractValidator<CreateLessonExampleCommand>
{
    public CreateLessonExampleValidator()
    {
        RuleFor(command => command.Input.EvidenceSentence).NotEmpty().OverridePropertyName("evidenceSentence");
        RuleFor(command => command.Input.Paraphrase).NotEmpty().OverridePropertyName("paraphrase");
    }
}
