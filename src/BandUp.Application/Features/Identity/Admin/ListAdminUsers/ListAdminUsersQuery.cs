using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.Admin.ListAdminUsers;

public sealed record ListAdminUsersQuery(PageRequest Page) : IQuery<PagedResult<AdminUserDto>>;
