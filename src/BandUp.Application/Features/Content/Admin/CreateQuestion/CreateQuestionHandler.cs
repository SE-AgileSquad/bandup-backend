using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.CreateQuestion;

public sealed class CreateQuestionHandler(IAdminPassageStore store)
    : ICommandHandler<CreateQuestionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        Passage? passage = await store.LoadForUpdateAsync(command.PassageId, cancellationToken);
        if (passage is null)
        {
            return Result<Guid>.Failure(Error.NotFound("Passage not found."));
        }

        if (passage.Status != PassageStatus.Draft)
        {
            return Result<Guid>.Failure(Error.Conflict("Questions can only be changed in a draft passage."));
        }

        QuestionInput input = command.Input;
        Question question = passage.AddQuestion(input.Type!.Value, input.Prompt, input.SortOrder);
        return Result<Guid>.Success(question.Id);
    }
}
