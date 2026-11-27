using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.CreatePassage;

public sealed class CreatePassageValidator : AbstractValidator<CreatePassageCommand>
{
    public CreatePassageValidator()
    {
        RuleFor(command => command.Input.DurationMinutes).Must(minutes => !minutes.HasValue || minutes.Value > 0)
            .WithMessage("Duration must be a positive number of minutes.").OverridePropertyName("durationMinutes");
        RuleFor(command => command.Input.Title).NotEmpty().OverridePropertyName("title");
        RuleFor(command => command.Input.Body).NotEmpty().OverridePropertyName("body");
        RuleFor(command => command.Input.SourceNote).NotEmpty().OverridePropertyName("sourceNote");
        RuleFor(command => command.Input.UsageBasis).NotEmpty().OverridePropertyName("usageBasis");
    }
}
