using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.EditQuestion;

public sealed record EditQuestionCommand(Guid PassageId, Guid QuestionId, QuestionEditInput Input) : ICommand<bool>;

public sealed record QuestionEditInput(
    string Prompt, int SortOrder, string? CorrectAnswer = null, string[]? AcceptedVariants = null,
    string? Explanation = null, string? EvidenceSentence = null, string? MisleadingSentence = null,
    string? UnspecifiedPart = null, bool IsVerified = false) : IQuestionContentInput;
