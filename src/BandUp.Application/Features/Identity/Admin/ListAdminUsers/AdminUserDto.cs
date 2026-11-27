using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.ListAdminUsers;

public sealed record AdminUserDto(
    Guid Id, string DisplayName, string Email, Role Role, bool IsActive,
    DateTimeOffset CreatedAt, int? CreditBalance);
