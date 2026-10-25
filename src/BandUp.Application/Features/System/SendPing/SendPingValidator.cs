using FluentValidation;

namespace BandUp.Application.Features.System.SendPing;

public sealed class SendPingValidator : AbstractValidator<SendPingCommand>
{
    public const int MaxMessageLength = 100;

    public SendPingValidator() =>
        RuleFor(command => command.Message)
            .NotEmpty()
            .MaximumLength(MaxMessageLength);
}
