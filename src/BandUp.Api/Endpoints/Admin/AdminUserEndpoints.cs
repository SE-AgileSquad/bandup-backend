using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.Admin.ChangeUserRole;
using BandUp.Application.Features.Identity.Admin.ListAdminUsers;
using BandUp.Domain.Identity;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminUserEndpoints : IEndpointGroup
{
    public string Prefix => "admin/users";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder users = AdminContentHttp.RequireAdmin(endpoints);

        users.MapGet("", (int? page, int? pageSize, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<ListAdminUsersQuery, PagedResult<AdminUserDto>>(
                    new ListAdminUsersQuery(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize)),
                    dispatcher, http))
            .WithName("Admin_ListUsers").Produces<PagedResult<AdminUserDto>>();

        users.MapPatch("/{userId:guid}/role", (
                Guid userId, ChangeRoleInput input, IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ChangeAsync(new ChangeUserRoleCommand(userId, input.Role), dispatcher, http))
            .WithName("Admin_ChangeUserRole").Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public sealed record ChangeRoleInput(Role? Role);
}
