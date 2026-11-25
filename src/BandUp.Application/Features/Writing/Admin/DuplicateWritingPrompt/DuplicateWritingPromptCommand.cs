using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.DuplicateWritingPrompt;

public sealed record DuplicateWritingPromptCommand(Guid PromptId) : ICommand<Guid>;

