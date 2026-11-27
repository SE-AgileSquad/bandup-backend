using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.PublishWritingPrompt;

public sealed record PublishWritingPromptCommand(Guid PromptId) : ICommand<bool>;

