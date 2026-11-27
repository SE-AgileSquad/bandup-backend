using BandUp.Application.Common;
using BandUp.Application.Features.Writing.Admin;
using BandUp.Domain.Writing;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfAdminWritingPromptStore(BandUpDbContext context) : IAdminWritingPromptStore
{
    public Task AddAsync(WritingPrompt prompt, CancellationToken cancellationToken) =>
        context.WritingPrompts.AddAsync(prompt, cancellationToken).AsTask();

    public async Task<WritingPrompt?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM writing.writing_prompts WHERE id = {id} FOR UPDATE", cancellationToken);
        return await context.WritingPrompts.SingleOrDefaultAsync(prompt => prompt.Id == id, cancellationToken);
    }

    public Task<WritingPromptDetails?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.WritingPrompts.AsNoTracking().Where(prompt => prompt.Id == id)
            .Select(prompt => new WritingPromptDetails(prompt.Id, prompt.Title, prompt.TaskText, prompt.EssayType,
                prompt.MinWords, prompt.MaxWords, prompt.Status, prompt.CreatedBy,
                prompt.CreatedAt, prompt.UpdatedAt, prompt.PublishedAt)).SingleOrDefaultAsync(cancellationToken);

    public Task<PagedResult<WritingPromptSummary>> ListAsync(
        PageRequest page, WritingPromptStatus? status, string? search, CancellationToken cancellationToken)
    {
        IQueryable<WritingPrompt> prompts = context.WritingPrompts.AsNoTracking();
        if (status.HasValue)
        {
            prompts = prompts.Where(prompt => prompt.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            prompts = prompts.Where(prompt => prompt.Title.ToLower().Contains(term));
        }

        return EfStoreOperations.ReadPageAsync(prompts.OrderByDescending(prompt => prompt.UpdatedAt)
            .ThenBy(prompt => prompt.Title).ThenBy(prompt => prompt.Id)
            .Select(prompt => new WritingPromptSummary(prompt.Id, prompt.Title, prompt.EssayType,
                prompt.MinWords, prompt.MaxWords, prompt.Status, prompt.UpdatedAt)), page, cancellationToken);
    }

    public Task<bool> TryDeleteAsync(Guid id, CancellationToken cancellationToken) =>
        EfStoreOperations.TryDeleteAsync(context.WritingPrompts.Where(prompt => prompt.Id == id), cancellationToken);
}

