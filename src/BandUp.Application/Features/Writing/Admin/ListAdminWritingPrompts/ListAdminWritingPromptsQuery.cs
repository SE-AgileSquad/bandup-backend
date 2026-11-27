using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin.ListAdminWritingPrompts;

public sealed record ListAdminWritingPromptsQuery(
    PageRequest Page, WritingPromptStatus? Status = null, string? Search = null) : IQuery<PagedResult<WritingPromptSummary>>;

