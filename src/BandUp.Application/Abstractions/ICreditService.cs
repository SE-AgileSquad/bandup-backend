using BandUp.Application.Common;

namespace BandUp.Application.Abstractions;

public interface ICreditService
{
    Task<Result> OpenSignupAccountAsync(Guid userId, CancellationToken cancellationToken);
}
