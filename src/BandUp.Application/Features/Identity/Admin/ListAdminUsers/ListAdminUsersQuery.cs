using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.ListAdminUsers;

public sealed record ListAdminUsersQuery(
    PageRequest Page, Role? Role = null, string? Search = null, bool? IsActive = true)
    : IQuery<PagedResult<AdminUserDto>>;
