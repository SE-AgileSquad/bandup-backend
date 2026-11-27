using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.CreateQuestion;

public sealed class CreateQuestionValidator : AbstractValidator<CreateQuestionCommand>
{
    public CreateQuestionValidator()
    {
        RuleFor(command => command.Input.Type)
            .Must(type => type.HasValue && Enum.IsDefined(type.Value))
            .WithMessage("A supported question type is required.")
            .OverridePropertyName("type");
        RuleFor(command => command.Input.Prompt).NotEmpty().OverridePropertyName("prompt");
        RuleForEach(command => command.Input.AcceptedVariants).NotEmpty().OverridePropertyName("acceptedVariants");
    }
}
