using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.Admin.ChangeUserRole;
using BandUp.Application.Features.Billing.Admin;
using BandUp.Application.Features.Identity.Admin.CreateAdminUser;
using BandUp.Application.Features.Identity.Admin.DeactivateUser;
using BandUp.Application.Features.Identity.Admin.ListAdminUsers;
using BandUp.Api.Errors;
using BandUp.Domain.Identity;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminUserEndpoints : IEndpointGroup
{
    public string Prefix => "admin/users";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder users = AdminContentHttp.RequireSuperAdmin(endpoints);

        users.MapGet("/{userId:guid}/credits", (Guid userId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminUserCreditsQuery, AdminCreditBalance>(new(userId), dispatcher, http))
            .WithName("Admin_GetUserCredits").Produces<AdminCreditBalance>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapGet("/{userId:guid}/credits/history", (Guid userId, int? page, int? pageSize,
                IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminCreditHistoryQuery, PagedResult<CreditHistoryItem>>(
                    new(userId, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)), dispatcher, http))
            .WithName("Admin_ListUserCreditHistory").Produces<PagedResult<CreditHistoryItem>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{userId:guid}/credits/adjustments", async (Guid userId, AdjustUserCreditsInput input,
                IDispatcher dispatcher, HttpContext http) =>
            {
                Result<CreditAdjustmentResult> result = await dispatcher.SendAsync<AdjustUserCreditsCommand, CreditAdjustmentResult>(
                    new(userId, input), http.RequestAborted);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(http);
            })
            .WithName("Admin_AdjustUserCredits").Produces<CreditAdjustmentResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapGet("", (int? page, int? pageSize, Role? role, string? search, bool? isActive,
                IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminUsersQuery, PagedResult<AdminUserDto>>(
                    new ListAdminUsersQuery(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize),
                        role, search, isActive ?? true),
                    dispatcher, http))
            .WithName("Admin_ListUsers").Produces<PagedResult<AdminUserDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        users.MapPost("", CreateUserAsync)
            .WithName("Admin_CreateUser").Produces<AdminContentHttp.CreatedResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPatch("/{userId:guid}/role", (
                Guid userId, ChangeRoleInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new ChangeUserRoleCommand(userId, input.Role), dispatcher, http))
            .WithName("Admin_ChangeUserRole").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapDelete("/{userId:guid}", (Guid userId, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new DeactivateUserCommand(userId), dispatcher, http))
            .WithName("Admin_DeactivateUser").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CreateUserAsync(
        CreateAdminUserCommand input, IDispatcher dispatcher, HttpContext http)
    {
        Result<Guid> result = await dispatcher.SendAsync<CreateAdminUserCommand, Guid>(input, http.RequestAborted);
        return result.IsSuccess
            ? TypedResults.Created((string?)null, new AdminContentHttp.CreatedResource(result.Value))
            : result.ToProblem(http);
    }

    public sealed record ChangeRoleInput(Role? Role);
}
