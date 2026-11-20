using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.DeleteQuestion;

public sealed class DeleteQuestionHandler(IAdminPassageStore store)
    : ICommandHandler<DeleteQuestionCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteQuestionCommand command, CancellationToken cancellationToken)
    {
        Result<Question> draft = await AdminDraftQuestion.LoadAsync(store, command.PassageId, command.QuestionId, cancellationToken);
        if (draft.IsFailure)
        {
            return Result<bool>.Failure(draft.Error!);
        }

        return await store.TryDeleteQuestionAsync(command.PassageId, command.QuestionId, cancellationToken)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(Error.Conflict("The question is referenced by other records."));
    }
}
