using BandUp.Application.Features.Media.Admin;
using BandUp.Domain.Content;
using BandUp.Domain.Writing;
using BandUp.Domain.Media;
using BandUp.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfAdminPhotoStore(BandUpDbContext context, IServiceScopeFactory scopes) : IAdminPhotoStore
{
    public async Task<PhotoOwnerState?> LoadForUpdateAsync(PhotoOwner owner, Guid id, CancellationToken cancellationToken)
    {
        if (owner == PhotoOwner.Lesson)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM learning.lessons WHERE id = {id} FOR UPDATE", cancellationToken);
            Lesson? lesson = await context.Lessons.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return lesson is null ? null : new(lesson.Status == LessonStatus.Archived, lesson.PhotoId);
        }
        if (owner == PhotoOwner.Passage)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM content.passages WHERE id = {id} FOR UPDATE", cancellationToken);
            Passage? passage = await context.Passages.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return passage is null ? null : new(passage.Status != PassageStatus.Draft, passage.PhotoId);
        }
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM writing.writing_prompts WHERE id = {id} FOR UPDATE", cancellationToken);
        WritingPrompt? prompt = await context.WritingPrompts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return prompt is null ? null : new(prompt.Status != WritingPromptStatus.Draft, prompt.PhotoId);
    }

    public void SetPhoto(PhotoOwner owner, Guid id, Guid? photoId, DateTimeOffset now)
    {
        if (owner == PhotoOwner.Lesson)
        {
            context.Lessons.Local.Single(item => item.Id == id).SetPhoto(photoId);
        }
        else if (owner == PhotoOwner.Passage)
        {
            context.Passages.Local.Single(item => item.Id == id).SetPhoto(photoId);
        }
        else
        {
            context.WritingPrompts.Local.Single(item => item.Id == id).SetPhoto(photoId, now);
        }
    }

    public async Task<MediaImage> ReserveAsync(ImageFileInfo info, int size, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Commit the reservation separately BEFORE contacting Cloudinary. Even a process crash,
        // cancellation or an ambiguous outer commit leaves a durable object for reconciliation.
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        var reservation = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        MediaImage image = MediaImage.Reserve(info.Format, info.ContentType, size, actorId, now);
        reservation.MediaImages.Add(image);
        await reservation.SaveChangesAsync(cancellationToken);
        return image;
    }

    public async Task<MediaImage?> GetAsync(PhotoOwner owner, Guid id, CancellationToken cancellationToken)
    {
        Guid? imageId = owner switch
        {
            PhotoOwner.Passage => await context.Passages.Where(item => item.Id == id).Select(item => item.PhotoId).SingleOrDefaultAsync(cancellationToken),
            PhotoOwner.WritingPrompt => await context.WritingPrompts.Where(item => item.Id == id).Select(item => item.PhotoId).SingleOrDefaultAsync(cancellationToken),
            PhotoOwner.Lesson => await context.Lessons.Where(item => item.Id == id).Select(item => item.PhotoId).SingleOrDefaultAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };
        return imageId is null ? null : await context.MediaImages.AsNoTracking().SingleOrDefaultAsync(image => image.Id == imageId, cancellationToken);
    }
}
