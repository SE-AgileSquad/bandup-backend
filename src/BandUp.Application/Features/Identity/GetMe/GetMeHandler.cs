using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.GetMe;

/// <summary>
/// AUTH-08. The 401 for an anonymous caller is produced here, not by the endpoint, so the
/// rule holds for every future caller of the query.
/// </summary>
public sealed class GetMeHandler(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    ICreditService creditService) : IQueryHandler<GetMeQuery, MeDto>
{
    public async Task<Result<MeDto>> HandleAsync(
        GetMeQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<MeDto>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        User? user = await userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Result<MeDto>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        int? balance = await creditService.GetBalanceAsync(userId, cancellationToken);

        return Result<MeDto>.Success(new MeDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            balance));
    }
}
