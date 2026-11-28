using System.Data;
using BandUp.Application.Features.Writing;
using BandUp.Domain.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BandUp.Infrastructure.Persistence;

internal sealed class EfWritingSubmissionStore(BandUpDbContext context) : IWritingSubmissionStore, IWritingMutationWriter
{
    public async Task<WritingPromptListDto> ListEligiblePromptsAsync(CancellationToken cancellationToken)
    {
        var published = context.WritingPrompts.AsNoTracking()
            .Where(prompt => prompt.Status == WritingPromptStatus.Published)
            .OrderBy(prompt => prompt.Id).AsAsyncEnumerable();
        List<WritingPromptDto> items = [];
        await foreach (var prompt in published.WithCancellation(cancellationToken))
        {
            if (prompt.IsEligibleForSubmission())
            {
                items.Add(new(prompt.Id, prompt.Title, prompt.TaskText));
            }
        }
        return new(items);
    }

    public async Task<WritingPrompt?> FindEligiblePromptAsync(Guid promptId, CancellationToken cancellationToken)
    {
        // Read the locked database version even if this scope previously tracked an older draft.
        var prompt = await context.WritingPrompts.FromSqlInterpolated(
            $"SELECT * FROM writing.writing_prompts WHERE id = {promptId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return prompt?.IsEligibleForSubmission() == true ? prompt : null;
    }

    public Task<WritingSubmission?> FindByKeyAsync(Guid userId, Guid idempotencyKey, CancellationToken cancellationToken) =>
        context.WritingSubmissions.AsNoTracking().SingleOrDefaultAsync(
            s => s.UserId == userId && s.IdempotencyKey == idempotencyKey, cancellationToken);

    async Task IWritingMutationWriter.AddAcceptedSubmissionAsync(WritingSubmission submission, CancellationToken cancellationToken)
    {
        await context.WritingSubmissions.AddAsync(submission, cancellationToken);
        context.AllowWritingSubmissionInsert(submission);
    }

    public Task<Guid?> FindOwnerAsync(Guid id, CancellationToken cancellationToken) =>
        context.WritingSubmissions.Where(s => s.Id == id).Select(s => (Guid?)s.UserId).SingleOrDefaultAsync(cancellationToken);

    public async Task<WritingSubmission?> LockSubmissionAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
        {
            throw new InvalidOperationException("Submission locks require a Read Committed transaction.");
        }

        var fresh = await context.WritingSubmissions.FromSqlInterpolated(
            $"SELECT * FROM writing.writing_submissions WHERE id = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (fresh is null)
        {
            return null;
        }
        var tracked = context.WritingSubmissions.Local.SingleOrDefault(s => s.Id == id);
        if (tracked is null)
        {
            context.Attach(fresh);
            return fresh;
        }
        var entry = context.Entry(tracked);
        if (entry.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException("Lock the submission before changing it.");
        }
        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        return tracked;
    }

    public Task<WritingFeedback?> FindFeedbackAsync(Guid id, CancellationToken cancellationToken) =>
        context.WritingFeedbacks.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<int> CountOutstandingAsync(Guid owner, CancellationToken cancellationToken)
    {
        var count = await context.WritingSubmissions.CountAsync(s => s.UserId == owner &&
            (s.Status == WritingEvaluationStatus.Queued || s.Status == WritingEvaluationStatus.Processing), cancellationToken);

        // SQL sees prior flushes in this transaction. Apply only the changes that are still pending.
        foreach (var entry in context.ChangeTracker.Entries<WritingSubmission>().Where(e => e.Entity.UserId == owner))
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                count += Outstanding(entry.Entity.Status);
            }
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                count -= Outstanding(entry.OriginalValues.GetValue<WritingEvaluationStatus>(nameof(WritingSubmission.Status)));
            }
        }
        return count;

        static int Outstanding(WritingEvaluationStatus status) =>
            status is WritingEvaluationStatus.Queued or WritingEvaluationStatus.Processing ? 1 : 0;
    }

    void IWritingMutationWriter.AuthorizeLifecycleChange(WritingSubmission submission) =>
        context.AllowWritingLifecycleChange(submission);

    async Task IWritingMutationWriter.AddFeedbackAsync(WritingFeedback feedback, CancellationToken cancellationToken)
    {
        await context.WritingFeedbacks.AddAsync(feedback, cancellationToken);
        context.AllowWritingFeedbackInsert(feedback);
    }
}
