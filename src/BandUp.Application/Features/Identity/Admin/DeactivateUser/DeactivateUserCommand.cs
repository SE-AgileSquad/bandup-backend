using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.Admin.DeactivateUser;

public sealed record DeactivateUserCommand(Guid UserId) : ICommand<bool>;
