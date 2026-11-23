using BandUp.Application.Common;
using BandUp.Application.Features.Learning.Admin;
using BandUp.Domain.Learning;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfAdminLessonStore(BandUpDbContext context) : IAdminLessonStore
{
    public async Task<Lesson?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM learning.lessons WHERE id = {id} FOR UPDATE", cancellationToken);
        return await context.Lessons
            .Include(lesson => lesson.Examples)
            .SingleOrDefaultAsync(lesson => lesson.Id == id, cancellationToken);
    }

    public Task AddAsync(Lesson lesson, CancellationToken cancellationToken) =>
        context.Lessons.AddAsync(lesson, cancellationToken).AsTask();

    public Task<bool> TryDeleteLessonAsync(Guid id, CancellationToken cancellationToken) =>
        EfStoreOperations.TryDeleteAsync(context.Lessons.Where(lesson => lesson.Id == id), cancellationToken);

    public Task<bool> TryDeleteExampleAsync(Guid lessonId, Guid exampleId, CancellationToken cancellationToken) =>
        EfStoreOperations.TryDeleteAsync(
            context.LessonExamples.Where(example => example.LessonId == lessonId && example.Id == exampleId),
            cancellationToken);

    public Task<PagedResult<LessonSummary>> ListAsync(
        PageRequest page, LessonStatus? status, string? search, CancellationToken cancellationToken)
    {
        IQueryable<Lesson> lessons = context.Lessons.AsNoTracking();
        if (status.HasValue)
        {
            lessons = lessons.Where(lesson => lesson.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            lessons = lessons.Where(lesson => lesson.Title.ToLower().Contains(term));
        }

        return EfStoreOperations.ReadPageAsync(
            lessons.OrderBy(lesson => lesson.SortOrder).ThenBy(lesson => lesson.Title).ThenBy(lesson => lesson.Id)
                .Select(lesson => new LessonSummary(
                    lesson.Id, lesson.Title, lesson.QuestionType, lesson.SortOrder, lesson.Status,
                    lesson.Examples.Count)),
            page, cancellationToken);
    }

    public Task<LessonDetails?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.Lessons.AsNoTracking().Where(lesson => lesson.Id == id)
            .Select(lesson => new LessonDetails(
                lesson.Id, lesson.Title, lesson.Body, lesson.QuestionType, lesson.SortOrder, lesson.Status,
                lesson.Examples.OrderBy(example => example.SortOrder).ThenBy(example => example.Id)
                    .Select(example => new LessonExampleDetails(
                        example.Id, example.EvidenceSentence, example.Paraphrase, example.SortOrder)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<LessonExampleDetails?> GetExampleAsync(
        Guid lessonId, Guid exampleId, CancellationToken cancellationToken) =>
        context.LessonExamples.AsNoTracking()
            .Where(example => example.LessonId == lessonId && example.Id == exampleId)
            .Select(example => new LessonExampleDetails(
                example.Id, example.EvidenceSentence, example.Paraphrase, example.SortOrder))
            .SingleOrDefaultAsync(cancellationToken);
}
