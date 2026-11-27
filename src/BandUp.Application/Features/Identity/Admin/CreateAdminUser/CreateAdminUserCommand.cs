using BandUp.Application.Abstractions;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.CreateAdminUser;

public sealed record CreateAdminUserCommand(string Email, string Password, string DisplayName, Role? Role)
    : ICommand<Guid>;
