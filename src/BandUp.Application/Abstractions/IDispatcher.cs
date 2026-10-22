using BandUp.Application.Common;

namespace BandUp.Application.Abstractions;

public interface IDispatcher
{
    Task<Result<TResponse>> SendAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResponse>;

    Task<Result<TResponse>> QueryAsync<TQuery, TResponse>(
        TQuery query,
        CancellationToken cancellationToken)
        where TQuery : IQuery<TResponse>;
}