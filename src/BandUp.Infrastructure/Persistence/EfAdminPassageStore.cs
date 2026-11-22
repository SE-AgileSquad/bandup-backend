using BandUp.Application.Common;
using BandUp.Application.Features.Content.Admin;
using BandUp.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfAdminPassageStore(BandUpDbContext context) : IAdminPassageStore
{
    public async Task<Passage?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Take the lock in a separate statement so the aggregate is read after any concurrent writer commits.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM content.passages WHERE id = {id} FOR UPDATE", cancellationToken);
        return await context.Passages
            .Include(passage => passage.Questions)
            .ThenInclude(question => question.AnswerKey)
            .SingleOrDefaultAsync(passage => passage.Id == id, cancellationToken);
    }

    public Task<bool> HasRevisionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Passages.AnyAsync(passage => passage.PreviousVersionId == id, cancellationToken);

    public Task AddAsync(Passage passage, CancellationToken cancellationToken) =>
        context.Passages.AddAsync(passage, cancellationToken).AsTask();

    public Task<bool> TryDeletePassageAsync(Guid id, CancellationToken cancellationToken) =>
        EfStoreOperations.TryDeleteAsync(context.Passages.Where(passage => passage.Id == id), cancellationToken);

    public async Task<bool> TryDeleteQuestionAsync(Guid passageId, Guid questionId, CancellationToken cancellationToken)
    {
        bool deleted = await EfStoreOperations.TryDeleteAsync(
            context.Questions.Where(question => question.PassageId == passageId && question.Id == questionId),
            cancellationToken);
        if (deleted)
        {
            // ExecuteDelete bypasses change tracking; the parent was locked and loaded by the handler.
            context.MarkPassageUpdated(passageId);
        }

        return deleted;
    }

    public Task<PagedResult<PassageSummary>> ListAsync(
        PageRequest page, PassageStatus? status, string? search, CancellationToken cancellationToken)
    {
        IQueryable<Passage> passages = context.Passages.AsNoTracking();
        if (status.HasValue)
        {
            passages = passages.Where(passage => passage.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            passages = passages.Where(passage => passage.Title.ToLower().Contains(term));
        }

        return EfStoreOperations.ReadPageAsync(
            passages.OrderByDescending(passage => passage.UpdatedAt.HasValue)
                .ThenByDescending(passage => passage.UpdatedAt)
                .ThenBy(passage => passage.Title).ThenBy(passage => passage.Id)
                .Select(passage => new PassageSummary(
                    passage.Id, passage.Title, passage.Status, passage.Version, passage.PublishedAt,
                    passage.Questions.Count, passage.UpdatedAt)),
            page, cancellationToken);
    }

    public async Task<PassageDetails?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Passage? passage = await context.Passages.AsNoTracking()
            .Include(item => item.Questions)
            .ThenInclude(question => question.AnswerKey)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return passage is null ? null : new PassageDetails(
            passage.Id, passage.Title, passage.Body, passage.SourceNote, passage.UsageBasis,
            passage.Status, passage.Version, passage.PreviousVersionId, passage.CreatedBy, passage.PublishedAt,
            passage.Questions.OrderBy(question => question.SortOrder).ThenBy(question => question.Id)
                .Select(ToDetails).ToArray(), passage.UpdatedAt);
    }

    public async Task<QuestionDetails?> GetQuestionAsync(
        Guid passageId, Guid questionId, CancellationToken cancellationToken)
    {
        Question? question = await context.Questions.AsNoTracking()
            .Include(item => item.AnswerKey)
            .SingleOrDefaultAsync(item => item.PassageId == passageId && item.Id == questionId, cancellationToken);

        return question is null ? null : ToDetails(question);
    }

    private static QuestionDetails ToDetails(Question question) =>
        new(question.Id, question.PassageId, question.Type, question.Prompt, question.SortOrder, question.Status,
            question.Explanation, question.EvidenceSentence, question.MisleadingSentence, question.UnspecifiedPart,
            question.AnswerKey is { } key
                ? new AnswerKeyDetails(key.CorrectAnswer, key.AcceptedVariants, key.VerifiedBy, key.VerifiedAt)
                : null);
}
