using BandUp.Application.Features.Media.Admin;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BandUp.Infrastructure.Media;

public sealed class ImageCleanupService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ImageCleanupService> logger)
{
    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = clock.GetUtcNow().AddHours(-1);
        Guid[] candidates;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            candidates = await db.MediaImages.AsNoTracking().Where(image => image.CreatedAt < cutoff
                && !db.Passages.Any(passage => passage.PhotoId == image.Id)
                && !db.Lessons.Any(lesson => lesson.PhotoId == image.Id)
                && !db.WritingPrompts.Any(prompt => prompt.PhotoId == image.Id))
                .OrderBy(image => image.CreatedAt).Take(20).Select(image => image.Id).ToArrayAsync(cancellationToken);
        }
        int deleted = 0;
        foreach (Guid id in candidates)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var image = await db.MediaImages.FromSqlInterpolated(
                $"SELECT * FROM media.images WHERE id = {id} FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(cancellationToken);
            if (image is null || await db.Passages.AnyAsync(item => item.PhotoId == id, cancellationToken)
                || await db.Lessons.AnyAsync(item => item.PhotoId == id, cancellationToken)
                || await db.WritingPrompts.AnyAsync(item => item.PhotoId == id, cancellationToken))
            {
                continue;
            }
            try
            {
                await scope.ServiceProvider.GetRequiredService<IImageStorage>().DeleteAsync(image.StorageKey, cancellationToken);
                db.MediaImages.Remove(image);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                deleted++;
            }
            catch (ImageStorageException)
            {
                // Keep the durable record; the next pass retries without failing unrelated deletions.
                logger.LogWarning("Image cleanup will retry {ImageId}", id);
            }
        }
        return deleted;
    }
}
