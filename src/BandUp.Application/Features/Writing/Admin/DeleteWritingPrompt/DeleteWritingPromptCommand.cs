using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.DeleteWritingPrompt;

public sealed record DeleteWritingPromptCommand(Guid PromptId) : ICommand<bool>;

