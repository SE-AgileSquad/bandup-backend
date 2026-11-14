using BandUp.Application.Abstractions;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.CreateQuestion;

public sealed record CreateQuestionCommand(Guid PassageId, QuestionInput Input) : ICommand<Guid>;

public sealed record QuestionInput(QuestionType? Type, string Prompt, int SortOrder);
