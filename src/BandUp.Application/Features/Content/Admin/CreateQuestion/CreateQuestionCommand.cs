using BandUp.Application.Abstractions;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.CreateQuestion;

public sealed record CreateQuestionCommand(Guid PassageId, QuestionInput Input) : ICommand<Guid>;

public sealed record QuestionInput(
    QuestionType? Type, string Prompt, int SortOrder, string? CorrectAnswer = null,
    string[]? AcceptedVariants = null, string? Explanation = null, string? EvidenceSentence = null,
    string? MisleadingSentence = null, string? UnspecifiedPart = null, bool IsVerified = false) : IQuestionContentInput;
