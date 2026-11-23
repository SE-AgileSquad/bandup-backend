using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.EditPassage;

public sealed class EditPassageValidator : AbstractValidator<EditPassageCommand>
{
    public EditPassageValidator()
    {
        RuleFor(command => command.Input.Title).NotEmpty().OverridePropertyName("title");
        RuleFor(command => command.Input.Body).NotEmpty().OverridePropertyName("body");
        RuleFor(command => command.Input.SourceNote).NotEmpty().OverridePropertyName("sourceNote");
        RuleFor(command => command.Input.UsageBasis).NotEmpty().OverridePropertyName("usageBasis");
    }
}
