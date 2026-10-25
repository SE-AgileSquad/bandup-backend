using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.System.GetServerTime;

public sealed class GetServerTimeHandler(TimeProvider timeProvider, IServerInfo serverInfo)
    : IQueryHandler<GetServerTimeQuery, ServerTimeDto>
{
    public Task<Result<ServerTimeDto>> HandleAsync(
        GetServerTimeQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<ServerTimeDto>.Success(new ServerTimeDto(
            timeProvider.GetUtcNow(),
            serverInfo.Environment,
            serverInfo.Version)));
}
