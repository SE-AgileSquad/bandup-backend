using BandUp.Application.Common;
using BandUp.Application.Features.Reading;
using BandUp.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence.Queries.Reading;

/// <summary>
/// Projects straight into learner DTOs. The answer key, explanation and evidence columns are
/// never selected, so they cannot leak through a serializer or a future field.
/// </summary>
internal sealed class ReadingQueries(BandUpDbContext context) : IReadingQueries
{
    public async Task<PagedResult<PassageSummaryDto>> ListPublishedPassagesAsync(
        PageRequest page,
        CancellationToken cancellationToken)
    {
        IQueryable<Passage> published = context.Passages
            .AsNoTracking()
            .Where(passage => passage.Status == PassageStatus.Published);

        int totalCount = await published.CountAsync(cancellationToken);

        List<PassageSummaryDto> items = await published
            .OrderByDescending(passage => passage.PublishedAt)
            .ThenBy(passage => passage.Title)
            .ThenBy(passage => passage.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(passage => new PassageSummaryDto(
                passage.Id,
                passage.Title,
                passage.Version,
                passage.PublishedAt,
                context.Questions.Count(question =>
                    question.PassageId == passage.Id && question.Status == QuestionStatus.Published)))
            .ToListAsync(cancellationToken);

        return PagedResult<PassageSummaryDto>.Create(items, page, totalCount);
    }

    public async Task<PassageDto?> GetPublishedPassageAsync(Guid passageId, CancellationToken cancellationToken)
    {
        var header = await context.Passages
            .AsNoTracking()
            .Where(passage => passage.Id == passageId && passage.Status == PassageStatus.Published)
            .Select(passage => new
            {
                passage.Id,
                passage.Title,
                passage.Body,
                passage.Version,
                passage.PublishedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        List<QuestionDto> questions = await context.Questions
            .AsNoTracking()
            .Where(question => question.PassageId == passageId && question.Status == QuestionStatus.Published)
            .OrderBy(question => question.SortOrder)
            .ThenBy(question => question.Id)
            .Select(question => new QuestionDto(
                question.Id,
                question.Type,
                question.Prompt,
                question.SortOrder,
                null))
            .ToListAsync(cancellationToken);

        return new PassageDto(header.Id, header.Title, header.Body, header.Version, header.PublishedAt, questions);
    }
}
