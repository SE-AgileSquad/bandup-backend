using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.SetQuestionExplanation;

public sealed class SetQuestionExplanationHandler(IAdminPassageStore store)
    : ICommandHandler<SetQuestionExplanationCommand, bool>
{
    public Task<Result<bool>> HandleAsync(SetQuestionExplanationCommand command, CancellationToken cancellationToken)
    {
        ExplanationInput input = command.Input;
        return AdminDraftQuestion.ChangeAsync(store, command.PassageId, command.QuestionId, "explanation",
            question => question.SetExplanation(
                input.Explanation, input.EvidenceSentence, input.MisleadingSentence, input.UnspecifiedPart),
            cancellationToken);
    }
}
