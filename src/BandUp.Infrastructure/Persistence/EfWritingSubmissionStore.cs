using BandUp.Application.Features.Writing;
using BandUp.Domain.Writing;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfWritingSubmissionStore(BandUpDbContext context) : IWritingSubmissionStore
{
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

    public Task AddAsync(WritingSubmission submission, CancellationToken cancellationToken) =>
        context.WritingSubmissions.AddAsync(submission, cancellationToken).AsTask();
}
