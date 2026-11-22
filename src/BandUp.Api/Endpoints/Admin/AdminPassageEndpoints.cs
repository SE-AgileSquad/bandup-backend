using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Content.Admin;
using BandUp.Application.Features.Content.Admin.ClearQuestionAnswerKey;
using BandUp.Application.Features.Content.Admin.CreatePassage;
using BandUp.Application.Features.Content.Admin.CreatePassageRevision;
using BandUp.Application.Features.Content.Admin.CreateQuestion;
using BandUp.Application.Features.Content.Admin.DeletePassage;
using BandUp.Application.Features.Content.Admin.DeleteQuestion;
using BandUp.Application.Features.Content.Admin.EditPassage;
using BandUp.Application.Features.Content.Admin.EditQuestion;
using BandUp.Application.Features.Content.Admin.GetAdminPassage;
using BandUp.Application.Features.Content.Admin.GetAdminQuestion;
using BandUp.Application.Features.Content.Admin.ListAdminPassages;
using BandUp.Application.Features.Content.Admin.PublishPassage;
using BandUp.Application.Features.Content.Admin.SetQuestionAnswerKey;
using BandUp.Application.Features.Content.Admin.SetQuestionExplanation;
using BandUp.Application.Features.Content.Admin.UnverifyQuestionAnswerKey;
using BandUp.Application.Features.Content.Admin.VerifyQuestionAnswerKey;
using BandUp.Domain.Content;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminPassageEndpoints : IEndpointGroup
{
    private const string ResourcePath = "/api/admin/passages";

    public string Prefix => "admin/passages";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder passages = AdminContentHttp.RequireAdmin(endpoints);
        RouteGroupBuilder passage = passages.MapGroup("/{passageId:guid}");
        RouteGroupBuilder questions = passage.MapGroup("/questions");
        RouteGroupBuilder question = questions.MapGroup("/{questionId:guid}");

        passages.MapGet("", (int? page, int? pageSize, PassageStatus? status, string? search,
                IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminPassagesQuery, PagedResult<PassageSummary>>(
                    new ListAdminPassagesQuery(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize),
                        status, search),
                    dispatcher, http))
            .WithName("Admin_ListPassages").Produces<PagedResult<PassageSummary>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        passages.MapPost("", (PassageInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(new CreatePassageCommand(input), ResourcePath, dispatcher, http))
            .WithName("Admin_CreatePassage").Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        passage.MapGet("", (Guid passageId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminPassageQuery, PassageDetails>(
                    new GetAdminPassageQuery(passageId), dispatcher, http))
            .WithName("Admin_GetPassage").Produces<PassageDetails>().ProducesProblem(StatusCodes.Status404NotFound);

        passage.MapPut("", (Guid passageId, PassageInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new EditPassageCommand(passageId, input), dispatcher, http))
            .WithName("Admin_EditPassage").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        passage.MapDelete("", (Guid passageId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new DeletePassageCommand(passageId), dispatcher, http))
            .WithName("Admin_DeletePassage").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        passage.MapPost("/publish", (Guid passageId, PublishPassageInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new PublishPassageCommand(passageId, input.ReviewConfirmed), dispatcher, http))
            .WithName("Admin_PublishPassage").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        passage.MapPost("/revisions", (Guid passageId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(new CreatePassageRevisionCommand(passageId), ResourcePath, dispatcher, http))
            .WithName("Admin_CreatePassageRevision")
            .Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        questions.MapPost("", (Guid passageId, QuestionInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.CreatedAsync(
                    new CreateQuestionCommand(passageId, input), $"{ResourcePath}/{passageId}/questions", dispatcher, http))
            .WithName("Admin_CreateQuestion").Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        question.MapGet("", (Guid passageId, Guid questionId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminQuestionQuery, QuestionDetails>(
                    new GetAdminQuestionQuery(passageId, questionId), dispatcher, http))
            .WithName("Admin_GetQuestion").Produces<QuestionDetails>().ProducesProblem(StatusCodes.Status404NotFound);

        question.MapPut("", (
                Guid passageId, Guid questionId, QuestionEditInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new EditQuestionCommand(passageId, questionId, input), dispatcher, http))
            .WithName("Admin_EditQuestion").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        question.MapDelete("", (Guid passageId, Guid questionId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new DeleteQuestionCommand(passageId, questionId), dispatcher, http))
            .WithName("Admin_DeleteQuestion").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        question.MapPut("/explanation", (
                Guid passageId, Guid questionId, ExplanationInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new SetQuestionExplanationCommand(passageId, questionId, input), dispatcher, http))
            .WithName("Admin_SetQuestionExplanation").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        question.MapPut("/answer-key", (
                Guid passageId, Guid questionId, AnswerKeyInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new SetQuestionAnswerKeyCommand(passageId, questionId, input), dispatcher, http))
            .WithName("Admin_SetQuestionAnswerKey").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        question.MapPost("/answer-key/verify", (
                Guid passageId, Guid questionId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new VerifyQuestionAnswerKeyCommand(passageId, questionId), dispatcher, http))
            .WithName("Admin_VerifyQuestionAnswerKey").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        question.MapDelete("/answer-key/verify", (
                Guid passageId, Guid questionId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new UnverifyQuestionAnswerKeyCommand(passageId, questionId), dispatcher, http))
            .WithName("Admin_UnverifyQuestionAnswerKey").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        question.MapDelete("/answer-key", (
                Guid passageId, Guid questionId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new ClearQuestionAnswerKeyCommand(passageId, questionId), dispatcher, http))
            .WithName("Admin_ClearQuestionAnswerKey").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
