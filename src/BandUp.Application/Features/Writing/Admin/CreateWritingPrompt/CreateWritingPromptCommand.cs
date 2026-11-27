using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.CreateWritingPrompt;

public sealed record CreateWritingPromptCommand(WritingPromptInput Input) : ICommand<Guid>;

