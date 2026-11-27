using BandUp.Application.Abstractions;
using BandUp.Application.Features.Learning.Admin.Mappings;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminLessonMappingEndpoints : IEndpointGroup
{
    public string Prefix => "admin/lesson-mappings";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder mappings = AdminContentHttp.RequireAdmin(endpoints);
        mappings.MapGet("", (IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetLessonMappingsQuery, IReadOnlyList<LessonMappingRow>>(
                    new GetLessonMappingsQuery(), dispatcher, http))
            .WithName("Admin_GetLessonMappings").Produces<IReadOnlyList<LessonMappingRow>>();
        mappings.MapPut("", (SaveLessonMappingsInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new SaveLessonMappingsCommand(input), dispatcher, http))
            .WithName("Admin_SaveLessonMappings").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
