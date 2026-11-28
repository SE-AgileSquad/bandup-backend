using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence.Configurations.Writing;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

internal sealed class EfWritingSubmissionQueries(BandUpDbContext context) : IWritingSubmissionQueries
{
    private IQueryable<WritingSubmission> Owned(Guid owner) =>
        context.WritingSubmissions.AsNoTracking().Where(s => s.UserId == owner);

    public async Task<PagedResult<WritingSubmissionHistoryItemDto>> ListAsync(Guid owner, PageRequest page, CancellationToken ct)
    {
        var owned = Owned(owner);
        int total = await owned.CountAsync(ct);
        // Skip takes an int. Compare the wide offset with the row count before narrowing it.
        long offset = ((long)page.Page - 1) * page.PageSize;
        if (offset >= total)
        {
            return PagedResult<WritingSubmissionHistoryItemDto>.Create([], page, total);
        }

        var items = await owned.OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id)
            .Skip((int)offset).Take(page.PageSize)
            .Select(s => new WritingSubmissionHistoryItemDto(s.Id, s.PromptId, EF.Property<string>(s, "PromptTitle"),
                s.Status, s.SubmittedAt, s.CompletedAt, s.Status == WritingEvaluationStatus.Succeeded
                    ? context.WritingFeedbacks.AsNoTracking().Where(f => f.Id == s.Id)
                        .Select(f => (decimal?)f.EstimatedOverallBand).FirstOrDefault()
                    : null))
            .ToListAsync(ct);
        if (items.Any(item => item.Status == WritingEvaluationStatus.Succeeded && item.EstimatedOverallBand is null))
        {
            throw new InvalidOperationException("A successful Writing submission is missing its feedback.");
        }
        return PagedResult<WritingSubmissionHistoryItemDto>.Create(items, page, total);
    }

    public Task<WritingEvaluationStatusDto?> FindStatusAsync(Guid owner, Guid id, CancellationToken ct) =>
        Owned(owner).Where(s => s.Id == id)
            .Select(s => new WritingEvaluationStatusDto(s.Id, s.Status, s.SubmittedAt, s.CompletedAt, null, null))
            .SingleOrDefaultAsync(ct);

    public Task<WritingSubmissionDetailDto?> FindDetailAsync(Guid owner, Guid id, CancellationToken ct) =>
        Owned(owner).Where(s => s.Id == id)
            .Select(s => new WritingSubmissionDetailDto(s.Id, s.EssayText,
                new WritingPromptDto(s.PromptId, EF.Property<string>(s, "PromptTitle"), EF.Property<string>(s, "PromptText"))))
            .SingleOrDefaultAsync(ct);

    public async Task<WritingFeedbackDto?> FindSuccessfulFeedbackAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var content = await (
            from submission in Owned(owner)
            where submission.Id == id && submission.Status == WritingEvaluationStatus.Succeeded
            join feedback in context.WritingFeedbacks.AsNoTracking() on submission.Id equals feedback.Id
            select feedback.Content).SingleOrDefaultAsync(ct);
        return content is null ? null : WritingFeedbackConfiguration.ToPublicFeedback(content);
    }
}
