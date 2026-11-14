using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.SetQuestionExplanation;

public sealed record SetQuestionExplanationCommand(Guid PassageId, Guid QuestionId, ExplanationInput Input) : ICommand<bool>;

public sealed record ExplanationInput(
    string Explanation, string? EvidenceSentence, string? MisleadingSentence, string? UnspecifiedPart);
