using BandUp.Application.Abstractions;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.ChangeUserRole;

public sealed record ChangeUserRoleCommand(Guid UserId, Role? Role) : ICommand<bool>;
