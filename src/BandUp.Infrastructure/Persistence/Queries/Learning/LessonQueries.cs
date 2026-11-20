using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence.Queries.Learning;

internal sealed class LessonQueries(BandUpDbContext context) : ILessonQueries
{
    public async Task<PagedResult<LessonSummaryDto>> ListPublishedLessonsAsync(
        QuestionType? questionType,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        IQueryable<Lesson> lessons = context.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Status == LessonStatus.Published);

        if (questionType is { } type)
        {
            lessons = lessons.Where(lesson => lesson.QuestionType == type);
        }

        int totalCount = await lessons.CountAsync(cancellationToken);

        List<LessonSummaryDto> items = await lessons
            .OrderBy(lesson => lesson.SortOrder)
            .ThenBy(lesson => lesson.Title)
            .ThenBy(lesson => lesson.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(lesson => new LessonSummaryDto(lesson.Id, lesson.Title, lesson.QuestionType, lesson.SortOrder))
            .ToListAsync(cancellationToken);

        return PagedResult<LessonSummaryDto>.Create(items, page, totalCount);
    }

    public async Task<LessonDto?> GetPublishedLessonAsync(Guid lessonId, CancellationToken cancellationToken)
    {
        var header = await context.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Id == lessonId && lesson.Status == LessonStatus.Published)
            .Select(lesson => new
            {
                lesson.Id,
                lesson.Title,
                lesson.Body,
                lesson.QuestionType,
                lesson.SortOrder,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        List<LessonExampleDto> examples = await context.LessonExamples
            .AsNoTracking()
            .Where(example => example.LessonId == lessonId)
            .OrderBy(example => example.SortOrder)
            .ThenBy(example => example.Id)
            .Select(example => new LessonExampleDto(example.EvidenceSentence, example.Paraphrase, example.SortOrder))
            .ToListAsync(cancellationToken);

        return new LessonDto(
            header.Id,
            header.Title,
            header.Body,
            header.QuestionType,
            header.SortOrder,
            examples);
    }

    public async Task<IReadOnlyDictionary<QuestionType, Guid>> GetDefaultLessonIdsAsync(
        IReadOnlyCollection<QuestionType> questionTypes,
        CancellationToken cancellationToken)
    {
        if (questionTypes.Count == 0)
        {
            return new Dictionary<QuestionType, Guid>();
        }

        QuestionType[] types = [.. questionTypes];

        // The unique index on (question_type, error_pattern) treats null as a value, so each
        // type has at most one default mapping.
        return await (
                from map in context.LessonMaps.AsNoTracking()
                join lesson in context.Lessons.AsNoTracking() on map.LessonId equals lesson.Id
                where map.ErrorPattern == null
                    && types.Contains(map.QuestionType)
                    && lesson.Status == LessonStatus.Published
                select new { map.QuestionType, map.LessonId })
            .ToDictionaryAsync(row => row.QuestionType, row => row.LessonId, cancellationToken);
    }
}
