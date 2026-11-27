using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin;

public interface IQuestionContentInput
{
    string Prompt { get; }
    int SortOrder { get; }
    string? CorrectAnswer { get; }
    string[]? AcceptedVariants { get; }
    string? Explanation { get; }
    string? EvidenceSentence { get; }
    string? MisleadingSentence { get; }
    string? UnspecifiedPart { get; }
    bool IsVerified { get; }
}

internal static class AdminQuestionContent
{
    internal static Result<bool> Apply(Question question, IQuestionContentInput input, ICurrentUser actor, TimeProvider clock)
    {
        if (input.IsVerified && (!actor.IsAuthenticated || actor.UserId is null || actor.UserId == Guid.Empty))
        {
            return Result<bool>.Failure(Error.Unauthorized("An authenticated reviewer is required."));
        }

        if (input.AcceptedVariants is { Length: > 0 }
            && (string.IsNullOrWhiteSpace(input.CorrectAnswer) || question.Type == QuestionType.TrueFalseNotGiven))
        {
            return Result<bool>.Failure(Error.Validation(new Dictionary<string, string[]>
            {
                ["acceptedVariants"] = ["Variants require a sentence-completion answer."],
            }));
        }

        try
        {
            if (string.IsNullOrWhiteSpace(input.CorrectAnswer))
            {
                question.ClearAnswerKey();
            }
            else
            {
                question.SetAnswerKey(input.CorrectAnswer, input.AcceptedVariants);
            }
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(Error.Validation(new Dictionary<string, string[]>
            {
                ["correctAnswer"] = [exception.Message],
            }));
        }

        question.EditDraft(input.Prompt, input.SortOrder);
        question.SetExplanation(input.Explanation, input.EvidenceSentence, input.MisleadingSentence, input.UnspecifiedPart);
        if (!input.IsVerified)
        {
            question.ClearAnswerKeyVerification();
            return Result<bool>.Success(true);
        }

        PassagePublicationReadiness readiness = PassagePublicationReadiness.FromIssues(question.GetPublicationIssues(false));
        if (!readiness.IsReady)
        {
            return Result<bool>.Failure(Error.Conflict("Complete the answer and explanation before verifying the question.")
                with { ValidationErrors = readiness.Errors });
        }

        if (question.AnswerKey is not { IsVerified: true })
        {
            question.VerifyAnswerKey(actor.UserId!.Value, clock.GetUtcNow());
        }
        return Result<bool>.Success(true);
    }
}
