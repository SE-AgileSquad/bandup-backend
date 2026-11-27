using BandUp.Application.Features.Learning.Admin.Mappings;
using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfAdminLessonMappingStore(BandUpDbContext context) : IAdminLessonMappingStore
{
    public async Task<IReadOnlyList<LessonMappingRow>> GetAsync(CancellationToken cancellationToken)
    {
        var selected = await (from map in context.LessonMaps.AsNoTracking()
            join lesson in context.Lessons.AsNoTracking() on map.LessonId equals lesson.Id
            where map.ErrorPattern == null
            select new { map.QuestionType, Lesson = new MappedLesson(lesson.Id, lesson.Title, lesson.Status) })
            .ToDictionaryAsync(item => item.QuestionType, item => item.Lesson, cancellationToken);
        var options = await context.Lessons.AsNoTracking().Where(lesson => lesson.Status == LessonStatus.Published)
            .OrderBy(lesson => lesson.SortOrder).ThenBy(lesson => lesson.Title).ThenBy(lesson => lesson.Id)
            .Select(lesson => new { lesson.QuestionType, Option = new LessonMappingOption(lesson.Id, lesson.Title) })
            .ToListAsync(cancellationToken);
        return Enum.GetValues<QuestionType>().Select(type => new LessonMappingRow(type,
            selected.GetValueOrDefault(type), options.Where(item => item.QuestionType == type)
                .Select(item => item.Option).ToArray())).ToArray();
    }

    public Task LockChangesAsync(CancellationToken cancellationToken) =>
        // Serialize even the first save, when there are no mapping rows to lock yet.
        context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({42412}, {1})", cancellationToken);

    public async Task<IReadOnlyList<Lesson>> LoadLessonsForUpdateAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        // Lesson editing/archiving uses the same row locks. Lock in ID order for batch saves.
        return await context.Lessons.FromSqlInterpolated(
                $"SELECT * FROM learning.lessons WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);
    }

    public async Task SetDefaultsAsync(IReadOnlyList<LessonMappingSelection> selections,
        IReadOnlyList<Lesson> lessons, CancellationToken cancellationToken)
    {
        List<LessonMap> defaults = await context.LessonMaps.Where(map => map.ErrorPattern == null)
            .ToListAsync(cancellationToken);
        foreach (LessonMappingSelection selection in selections)
        {
            LessonMap? existing = defaults.SingleOrDefault(map => map.QuestionType == selection.QuestionType);
            if (selection.LessonId is not { } id)
            {
                if (existing is not null)
                {
                    context.LessonMaps.Remove(existing);
                }
                continue;
            }

            Lesson lesson = lessons.Single(item => item.Id == id);
            if (existing is null)
            {
                context.LessonMaps.Add(LessonMap.Create(lesson));
            }
            else
            {
                existing.ChangeLesson(lesson);
            }
        }
    }
}
