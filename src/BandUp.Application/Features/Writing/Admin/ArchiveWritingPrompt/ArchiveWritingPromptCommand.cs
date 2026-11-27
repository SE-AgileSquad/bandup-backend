using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.ArchiveWritingPrompt;

public sealed record ArchiveWritingPromptCommand(Guid PromptId) : ICommand<bool>;

