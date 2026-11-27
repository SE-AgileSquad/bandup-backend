using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.VerifyQuestionAnswerKey;

public sealed class VerifyQuestionAnswerKeyHandler(IAdminPassageStore store, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<VerifyQuestionAnswerKeyCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(VerifyQuestionAnswerKeyCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || actorId == Guid.Empty)
        {
            return Result<bool>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        Result<Question> draft = await AdminDraftQuestion.LoadAsync(store, command.PassageId, command.QuestionId, cancellationToken);
        if (draft.IsFailure)
        {
            return Result<bool>.Failure(draft.Error!);
        }

        PassagePublicationReadiness readiness = PassagePublicationReadiness.FromIssues(
            draft.Value.GetPublicationIssues(requireVerification: false));
        if (!readiness.IsReady)
        {
            return Result<bool>.Failure(Error.Conflict("The answer cannot be verified until its explanation is complete.")
                with { ValidationErrors = readiness.Errors });
        }

        if (draft.Value.AnswerKey is { IsVerified: true })
        {
            return Result<bool>.Success(true);
        }

        try
        {
            draft.Value.VerifyAnswerKey(actorId, clock.GetUtcNow());
            return Result<bool>.Success(true);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(Error.Conflict(exception.Message));
        }
    }
}
