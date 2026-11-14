using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.GetAdminQuestion;

public sealed class GetAdminQuestionHandler(IAdminPassageStore store)
    : IQueryHandler<GetAdminQuestionQuery, QuestionDetails>
{
    public async Task<Result<QuestionDetails>> HandleAsync(
        GetAdminQuestionQuery query, CancellationToken cancellationToken) =>
        await store.GetQuestionAsync(query.PassageId, query.QuestionId, cancellationToken) is { } question
            ? Result<QuestionDetails>.Success(question)
            : Result<QuestionDetails>.Failure(Error.NotFound("Question not found in this passage."));
}
