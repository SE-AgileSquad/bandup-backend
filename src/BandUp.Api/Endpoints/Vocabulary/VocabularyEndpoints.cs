using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Vocabulary.ListDueReviews;
using BandUp.Application.Features.Vocabulary.RecordVocabularyReview;
using BandUp.Application.Features.Vocabulary.SaveVocabCard;

namespace BandUp.Api.Endpoints.Vocabulary;

public sealed class VocabularyEndpoints : IEndpointGroup
{
    public string Prefix => "vocabulary";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder vocabulary = endpoints.MapGroup("").RequireAuthorization()
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []));

        vocabulary.MapPost("/cards", SaveCardAsync)
            .WithName("Vocabulary_SaveCard").Produces<SavedVocabCardDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        vocabulary.MapGet("/reviews/due", ListDueAsync)
            .WithName("Vocabulary_ListDueReviews").Produces<PagedResult<DueVocabularyReviewDto>>();

        vocabulary.MapPost("/reviews/{reviewId:guid}/complete", CompleteReviewAsync)
            .WithName("Vocabulary_CompleteReview").Produces<VocabularyReviewDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> SaveCardAsync(SaveVocabCardCommand input, IDispatcher dispatcher, HttpContext http)
    {
        Result<SavedVocabCardDto> result = await dispatcher.SendAsync<SaveVocabCardCommand, SavedVocabCardDto>(
            input, http.RequestAborted);
        return result.IsSuccess ? TypedResults.Created((string?)null, result.Value) : result.ToProblem(http);
    }

    private static async Task<IResult> ListDueAsync(
        int? page, int? pageSize, IDispatcher dispatcher, HttpContext http)
    {
        ListDueReviewsQuery query = new(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize));
        Result<PagedResult<DueVocabularyReviewDto>> result = await dispatcher
            .QueryAsync<ListDueReviewsQuery, PagedResult<DueVocabularyReviewDto>>(query, http.RequestAborted);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(http);
    }

    private static async Task<IResult> CompleteReviewAsync(
        Guid reviewId, ReviewOutcomeInput input, IDispatcher dispatcher, HttpContext http)
    {
        Result<VocabularyReviewDto> result = await dispatcher
            .SendAsync<RecordVocabularyReviewCommand, VocabularyReviewDto>(
                new RecordVocabularyReviewCommand(reviewId, input.IsCorrect), http.RequestAborted);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(http);
    }

    public sealed record ReviewOutcomeInput(bool? IsCorrect);
}
