using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.EditWritingPrompt;

public sealed record EditWritingPromptCommand(Guid PromptId, WritingPromptInput Input) : ICommand<bool>;

