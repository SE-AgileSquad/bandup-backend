using BandUp.Application.Common;
using BandUp.Application.Features.Exams;
using BandUp.Domain.Exams;
using BandUp.Domain.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence.Queries.Exams;

/// <summary>
/// Reads for the exam module that touch other modules' tables or span many attempts. Everything
/// is a plain read of the caller's own data or of approved content, and nothing here can reveal
/// an answer key.
/// </summary>
internal sealed class AttemptQueries(BandUpDbContext context) : IAttemptQueries
{
    public Task<string?> GetPassageTitleAsync(Guid passageId, CancellationToken cancellationToken) =>
        context.Passages
            .AsNoTracking()
            .Where(passage => passage.Id == passageId)
            .Select(passage => (string?)passage.Title)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ParaphrasePairDto>>> GetApprovedParaphrasePairsAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ParaphrasePairDto>>();
        }

        Guid[] ids = [.. questionIds];

        var rows = await context.ParaphrasePairs
            .AsNoTracking()
            .Where(pair => pair.Status == ParaphrasePairStatus.Approved
                && pair.QuestionId != null
                && ids.Contains(pair.QuestionId.Value))
            .OrderBy(pair => pair.Id)
            .Select(pair => new
            {
                QuestionId = pair.QuestionId!.Value,
                pair.SourceSentence,
                pair.EquivalentSentence,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.QuestionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ParaphrasePairDto>)group
                    .Select(row => new ParaphrasePairDto(row.SourceSentence, row.EquivalentSentence))
                    .ToList());
    }

    public async Task<PagedResult<AttemptHistoryItemDto>> ListSubmittedAsync(
        Guid userId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        IQueryable<Attempt> submitted = context.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId && attempt.Status == AttemptStatus.Submitted);

        int totalCount = await submitted.CountAsync(cancellationToken);

        List<AttemptHistoryItemDto> items = await (
                from attempt in submitted
                join passage in context.Passages.AsNoTracking() on attempt.PassageId equals passage.Id
                orderby attempt.SubmittedAt descending, attempt.Id
                select new AttemptHistoryItemDto(
                    attempt.Id,
                    attempt.PassageId,
                    passage.Title,
                    attempt.SubmittedAt ?? attempt.ExpiresAt,
                    attempt.RawScore ?? 0,
                    attempt.TotalQuestions ?? 0,
                    attempt.ApproxBand))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<AttemptHistoryItemDto>.Create(items, page, totalCount);
    }

    public async Task<IReadOnlyList<PassageAttemptSummaryDto>> GetPassageSummariesAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A learner has a handful of attempts, so the rows are read once and judged against the
        // clock in memory: whether a sitting is still running depends on the deadline plus grace.
        var rows = await context.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId && attempt.Status != AttemptStatus.Expired)
            .Select(attempt => new
            {
                attempt.Id,
                attempt.PassageId,
                attempt.Status,
                attempt.ExpiresAt,
                attempt.SubmittedAt,
                attempt.RawScore,
                attempt.TotalQuestions,
                attempt.ApproxBand,
            })
            .ToListAsync(cancellationToken);

        List<PassageAttemptSummaryDto> summaries = [];

        foreach (var passageRows in rows.GroupBy(row => row.PassageId))
        {
            var running = passageRows
                .Where(row => row.Status == AttemptStatus.InProgress
                    && now <= row.ExpiresAt + Attempt.SubmissionGrace)
                .OrderByDescending(row => row.ExpiresAt)
                .FirstOrDefault();

            if (running is not null)
            {
                TimeSpan remaining = running.ExpiresAt - now;
                int remainingSeconds = remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;

                summaries.Add(new PassageAttemptSummaryDto(
                    passageRows.Key,
                    PassageAttemptState.InProgress,
                    running.Id,
                    remainingSeconds,
                    null,
                    null,
                    null,
                    null));

                continue;
            }

            var completed = passageRows
                .Where(row => row.Status == AttemptStatus.Submitted)
                .OrderByDescending(row => row.SubmittedAt)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();

            if (completed is not null)
            {
                summaries.Add(new PassageAttemptSummaryDto(
                    passageRows.Key,
                    PassageAttemptState.Completed,
                    completed.Id,
                    null,
                    completed.RawScore,
                    completed.TotalQuestions,
                    completed.ApproxBand,
                    completed.SubmittedAt));
            }
        }

        return [.. summaries.OrderBy(summary => summary.PassageId)];
    }
}
