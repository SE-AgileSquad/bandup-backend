using BandUp.Application.Features.Exams;
using BandUp.Domain.Content;
using BandUp.Domain.Exams;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence.Queries.Exams;

/// <summary>
/// The one place that reads answer keys for the exam module. Accepted variants are read through
/// the field-mapped column because the domain exposes them only as a read-only wrapper.
/// </summary>
internal sealed class AnswerKeyQueries(BandUpDbContext context) : IAnswerKeyQueries
{
    public async Task<IReadOnlyList<ScoringKey>> GetScoringKeysAsync(
        Guid passageId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from question in context.Questions.AsNoTracking()
                join key in context.AnswerKeys.AsNoTracking() on question.Id equals key.QuestionId
                where question.PassageId == passageId && question.Status == QuestionStatus.Published
                orderby question.SortOrder, question.Id
                select new
                {
                    question.Id,
                    question.Type,
                    key.CorrectAnswer,
                    Variants = EF.Property<List<string>>(key, "_acceptedVariants"),
                })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ScoringKey(row.Id, row.Type, row.CorrectAnswer, row.Variants))];
    }

    public async Task<IReadOnlyList<QuestionReview>> GetReviewAsync(
        Guid passageId,
        CancellationToken cancellationToken) =>
        await (
                from question in context.Questions.AsNoTracking()
                join key in context.AnswerKeys.AsNoTracking() on question.Id equals key.QuestionId
                where question.PassageId == passageId && question.Status == QuestionStatus.Published
                orderby question.SortOrder, question.Id
                select new QuestionReview(
                    question.Id,
                    question.Type,
                    question.Prompt,
                    question.SortOrder,
                    key.CorrectAnswer,
                    question.Explanation,
                    question.EvidenceSentence,
                    question.UnspecifiedPart))
            .ToListAsync(cancellationToken);
}
