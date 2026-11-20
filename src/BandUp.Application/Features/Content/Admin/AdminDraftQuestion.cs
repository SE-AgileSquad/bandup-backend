using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin;

internal static class AdminDraftQuestion
{
    internal static async Task<Result<bool>> ChangeAsync(
        IAdminPassageStore store,
        Guid passageId,
        Guid questionId,
        string field,
        Action<Question> change,
        CancellationToken cancellationToken)
    {
        Result<Question> draft = await LoadAsync(store, passageId, questionId, cancellationToken);
        if (draft.IsFailure)
        {
            return Result<bool>.Failure(draft.Error!);
        }

        try
        {
            change(draft.Value);
            return Result<bool>.Success(true);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(
                Error.Validation(new Dictionary<string, string[]> { [field] = [exception.Message] }));
        }
    }

    internal static async Task<Result<Question>> LoadAsync(
        IAdminPassageStore store, Guid passageId, Guid questionId, CancellationToken cancellationToken)
    {
        Passage? passage = await store.LoadForUpdateAsync(passageId, cancellationToken);
        Question? question = passage?.Questions.SingleOrDefault(item => item.Id == questionId);
        if (question is null)
        {
            return Result<Question>.Failure(Error.NotFound("Question not found in this passage."));
        }

        return passage!.Status == PassageStatus.Draft && question.Status == QuestionStatus.Draft
            ? Result<Question>.Success(question)
            : Result<Question>.Failure(Error.Conflict("Questions can only be changed in a draft passage."));
    }
}
