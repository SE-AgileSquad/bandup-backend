using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Reading;
using BandUp.Application.Features.Reading.GetPassage;
using BandUp.Application.Features.Reading.ListPassages;

namespace BandUp.Api.Endpoints.Reading;

/// <summary>
/// Reading content for signed-in learners. Passages come back without any answer key: the
/// correct answers and explanations are revealed only by the result of a submitted attempt.
/// </summary>
public sealed class ReadingEndpoints : IEndpointGroup
{
    public string Prefix => "reading";

    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/passages", ListPassagesAsync)
            .WithName("Reading_ListPassages")
            .RequireAuthorization()
            .Produces<PagedResult<PassageSummaryDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet("/passages/{passageId:guid}", GetPassageAsync)
            .WithName("Reading_GetPassage")
            .RequireAuthorization()
            .Produces<PassageDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListPassagesAsync(
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize)
    {
        Result<PagedResult<PassageSummaryDto>> result =
            await dispatcher.QueryAsync<ListPassagesQuery, PagedResult<PassageSummaryDto>>(
                new ListPassagesQuery(page, pageSize),
                cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> GetPassageAsync(
        Guid passageId,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<PassageDto> result = await dispatcher.QueryAsync<GetPassageQuery, PassageDto>(
            new GetPassageQuery(passageId),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }
}
