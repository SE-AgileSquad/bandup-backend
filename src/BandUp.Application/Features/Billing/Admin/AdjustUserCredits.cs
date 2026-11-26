using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using FluentValidation;

namespace BandUp.Application.Features.Billing.Admin;

public sealed record AdjustUserCreditsCommand(Guid UserId, AdjustUserCreditsInput Input) : ICommand<CreditAdjustmentResult>;

public sealed class AdjustUserCreditsValidator : AbstractValidator<AdjustUserCreditsCommand>
{
    public AdjustUserCreditsValidator()
    {
        RuleFor(command => command.Input.Delta).Must(delta => delta.HasValue && delta.Value is not 0 and not int.MinValue)
            .WithMessage("A supported non-zero integer credit adjustment is required.").OverridePropertyName("delta");
        RuleFor(command => command.Input.RequestId).Must(id => id.HasValue && id.Value != Guid.Empty)
            .WithMessage("A non-empty request ID is required.").OverridePropertyName("requestId");
        RuleFor(command => command.Input.Note).MaximumLength(200).OverridePropertyName("note");
    }
}

public sealed class AdjustUserCreditsHandler(IUserRepository users, ICurrentUser currentUser, ICreditService credits)
    : ICommandHandler<AdjustUserCreditsCommand, CreditAdjustmentResult>
{
    public async Task<Result<CreditAdjustmentResult>> HandleAsync(AdjustUserCreditsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actorId)
        {
            return Result<CreditAdjustmentResult>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        // Shares the role-change lock so a demotion waiting/racing with an adjustment is ordered.
        // It also serializes request-ID checks across admin adjustments to different users.
        await users.LockRoleChangesAsync(cancellationToken);
        if (!await users.IsActiveSuperAdminAsync(actorId, cancellationToken))
        {
            return Result<CreditAdjustmentResult>.Failure(Error.Forbidden("An active super admin is required."));
        }

        if (await users.GetByIdAsync(command.UserId, cancellationToken) is null)
        {
            return Result<CreditAdjustmentResult>.Failure(Error.NotFound("User not found."));
        }

        return await credits.AdjustAsync(command.UserId, command.Input.Delta!.Value, actorId,
            command.Input.RequestId!.Value, command.Input.Note, cancellationToken);
    }
}
