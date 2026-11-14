using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Application.Features.Learning.GetLesson;
using BandUp.Application.Features.Learning.ListLessons;
using BandUp.Domain.Content;

namespace BandUp.Api.Endpoints.Lessons;

/// <summary>
/// Published lessons for signed-in learners, optionally narrowed to one question type.
/// </summary>
public sealed class LessonEndpoints : IEndpointGroup
{
    public string Prefix => "lessons";

    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", ListLessonsAsync)
            .WithName("Lessons_List")
            .RequireAuthorization()
            .Produces<PagedResult<LessonSummaryDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGet("/{lessonId:guid}", GetLessonAsync)
            .WithName("Lessons_Get")
            .RequireAuthorization()
            .Produces<LessonDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListLessonsAsync(
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        QuestionType? questionType = null,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize)
    {
        Result<PagedResult<LessonSummaryDto>> result =
            await dispatcher.QueryAsync<ListLessonsQuery, PagedResult<LessonSummaryDto>>(
                new ListLessonsQuery(questionType, page, pageSize),
                cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> GetLessonAsync(
        Guid lessonId,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<LessonDto> result = await dispatcher.QueryAsync<GetLessonQuery, LessonDto>(
            new GetLessonQuery(lessonId),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }
}
