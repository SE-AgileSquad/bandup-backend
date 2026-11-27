using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing.Admin;
using BandUp.Application.Features.Writing.Admin.CreateWritingPrompt;
using BandUp.Application.Features.Writing.Admin.EditWritingPrompt;
using BandUp.Application.Features.Writing.Admin.GetAdminWritingPrompt;
using BandUp.Application.Features.Writing.Admin.ListAdminWritingPrompts;
using BandUp.Application.Features.Writing.Admin.PublishWritingPrompt;
using BandUp.Application.Features.Writing.Admin.ArchiveWritingPrompt;
using BandUp.Application.Features.Writing.Admin.DeleteWritingPrompt;
using BandUp.Application.Features.Writing.Admin.DuplicateWritingPrompt;
using BandUp.Domain.Writing;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminWritingPromptEndpoints : IEndpointGroup
{
    private const string ResourcePath = "/api/admin/writing-prompts";
    public string Prefix => "admin/writing-prompts";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder prompts = AdminContentHttp.RequireAdmin(endpoints);
        RouteGroupBuilder prompt = prompts.MapGroup("/{promptId:guid}");

        prompts.MapGet("", (int? page, int? pageSize, WritingPromptStatus? status, string? search,
                IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminWritingPromptsQuery, PagedResult<WritingPromptSummary>>(
                    new ListAdminWritingPromptsQuery(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize),
                        status, search), dispatcher, http))
            .WithName("Admin_ListWritingPrompts").Produces<PagedResult<WritingPromptSummary>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        prompts.MapPost("", (WritingPromptInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(new CreateWritingPromptCommand(input), ResourcePath, dispatcher, http))
            .WithName("Admin_CreateWritingPrompt")
            .Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        prompt.MapGet("", (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminWritingPromptQuery, WritingPromptDetails>(
                    new GetAdminWritingPromptQuery(promptId), dispatcher, http))
            .WithName("Admin_GetWritingPrompt").Produces<WritingPromptDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        prompt.MapPut("", (Guid promptId, WritingPromptInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new EditWritingPromptCommand(promptId, input), dispatcher, http))
            .WithName("Admin_EditWritingPrompt").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        prompt.MapPost("/publish", (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new PublishWritingPromptCommand(promptId), dispatcher, http))
            .WithName("Admin_PublishWritingPrompt").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        prompt.MapPost("/archive", (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new ArchiveWritingPromptCommand(promptId), dispatcher, http))
            .WithName("Admin_ArchiveWritingPrompt").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        prompt.MapDelete("", (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new DeleteWritingPromptCommand(promptId), dispatcher, http))
            .WithName("Admin_DeleteWritingPrompt").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        prompt.MapPost("/duplicate", (Guid promptId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(new DuplicateWritingPromptCommand(promptId), ResourcePath, dispatcher, http))
            .WithName("Admin_DuplicateWritingPrompt")
            .Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

