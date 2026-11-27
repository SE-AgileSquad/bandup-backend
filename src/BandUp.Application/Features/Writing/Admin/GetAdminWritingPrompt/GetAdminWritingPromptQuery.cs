using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Writing.Admin.GetAdminWritingPrompt;

public sealed record GetAdminWritingPromptQuery(Guid PromptId) : IQuery<WritingPromptDetails>;

