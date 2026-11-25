using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning.Admin;
using BandUp.Application.Features.Learning.Admin.ArchiveLesson;
using BandUp.Application.Features.Learning.Admin.CreateLesson;
using BandUp.Application.Features.Learning.Admin.CreateLessonExample;
using BandUp.Application.Features.Learning.Admin.DeleteLesson;
using BandUp.Application.Features.Learning.Admin.DeleteLessonExample;
using BandUp.Application.Features.Learning.Admin.EditLesson;
using BandUp.Application.Features.Learning.Admin.EditLessonExample;
using BandUp.Application.Features.Learning.Admin.GetAdminLesson;
using BandUp.Application.Features.Learning.Admin.GetAdminLessonExample;
using BandUp.Application.Features.Learning.Admin.ListAdminLessons;
using BandUp.Application.Features.Learning.Admin.ListAdminLessonExamples;
using BandUp.Application.Features.Learning.Admin.PublishLesson;
using BandUp.Application.Features.Learning.Admin.ReorderLessonExamples;
using BandUp.Domain.Content;
using BandUp.Domain.Learning;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminLessonEndpoints : IEndpointGroup
{
    private const string ResourcePath = "/api/admin/lessons";

    public string Prefix => "admin/lessons";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder lessons = AdminContentHttp.RequireAdmin(endpoints);
        RouteGroupBuilder lesson = lessons.MapGroup("/{lessonId:guid}");
        RouteGroupBuilder examples = lesson.MapGroup("/examples");
        RouteGroupBuilder example = examples.MapGroup("/{exampleId:guid}");

        lessons.MapGet("", (int? page, int? pageSize, LessonStatus? status, string? search, QuestionType? questionType,
                IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminLessonsQuery, PagedResult<LessonSummary>>(
                    new ListAdminLessonsQuery(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize),
                        status, search, questionType),
                    dispatcher, http))
            .WithName("Admin_ListLessons").Produces<PagedResult<LessonSummary>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        lessons.MapPost("", (LessonInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(new CreateLessonCommand(input), ResourcePath, dispatcher, http))
            .WithName("Admin_CreateLesson").Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        lesson.MapGet("", (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminLessonQuery, LessonDetails>(
                    new GetAdminLessonQuery(lessonId), dispatcher, http))
            .WithName("Admin_GetLesson").Produces<LessonDetails>().ProducesProblem(StatusCodes.Status404NotFound);

        lesson.MapPut("", (Guid lessonId, LessonEditInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new EditLessonCommand(lessonId, input), dispatcher, http))
            .WithName("Admin_EditLesson").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        lesson.MapDelete("", (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new DeleteLessonCommand(lessonId), dispatcher, http))
            .WithName("Admin_DeleteLesson").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        lesson.MapPost("/publish", (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new PublishLessonCommand(lessonId), dispatcher, http))
            .WithName("Admin_PublishLesson").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        lesson.MapPost("/archive", (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new ArchiveLessonCommand(lessonId), dispatcher, http))
            .WithName("Admin_ArchiveLesson").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        examples.MapPost("", (Guid lessonId, LessonExampleInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(
                    new CreateLessonExampleCommand(lessonId, input), $"{ResourcePath}/{lessonId}/examples", dispatcher, http))
            .WithName("Admin_CreateLessonExample")
            .Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        examples.MapGet("", (Guid lessonId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminLessonExamplesQuery, IReadOnlyList<LessonExampleDetails>>(
                    new ListAdminLessonExamplesQuery(lessonId), dispatcher, http))
            .WithName("Admin_ListLessonExamples").Produces<IReadOnlyList<LessonExampleDetails>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        examples.MapPut("/order", (Guid lessonId, LessonExampleOrderInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new ReorderLessonExamplesCommand(lessonId, input), dispatcher, http))
            .WithName("Admin_ReorderLessonExamples").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        example.MapGet("", (Guid lessonId, Guid exampleId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminLessonExampleQuery, LessonExampleDetails>(
                    new GetAdminLessonExampleQuery(lessonId, exampleId), dispatcher, http))
            .WithName("Admin_GetLessonExample").Produces<LessonExampleDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        example.MapPut("", (
                Guid lessonId, Guid exampleId, LessonExampleInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new EditLessonExampleCommand(lessonId, exampleId, input), dispatcher, http))
            .WithName("Admin_EditLessonExample").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        example.MapDelete("", (Guid lessonId, Guid exampleId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new DeleteLessonExampleCommand(lessonId, exampleId), dispatcher, http))
            .WithName("Admin_DeleteLessonExample").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
