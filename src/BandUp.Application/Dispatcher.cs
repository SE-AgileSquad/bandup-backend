using BandUp.Application.Abstractions;
using BandUp.Application.Behaviors;
using BandUp.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.Application;

public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    public Task<Result<TResponse>> SendAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResponse>
    {
        ICommandHandler<TCommand, TResponse> handler =
            serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

        IReadOnlyList<IPipelineBehavior<TCommand, TResponse>> behaviors = serviceProvider
            .GetServices<IPipelineBehavior<TCommand, TResponse>>()
            .ToArray();

        return Pipeline.ExecuteAsync(
            command,
            cancellationToken,
            () => handler.HandleAsync(command, cancellationToken),
            behaviors);
    }

    public Task<Result<TResponse>> QueryAsync<TQuery, TResponse>(
        TQuery query,
        CancellationToken cancellationToken)
        where TQuery : IQuery<TResponse>
    {
        IQueryHandler<TQuery, TResponse> handler =
            serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();

        IReadOnlyList<IPipelineBehavior<TQuery, TResponse>> behaviors = serviceProvider
            .GetServices<IPipelineBehavior<TQuery, TResponse>>()
            .Where(behavior => behavior is not ICommandOnlyPipelineBehavior)
            .ToArray();

        return Pipeline.ExecuteAsync(
            query,
            cancellationToken,
            () => handler.HandleAsync(query, cancellationToken),
            behaviors);
    }
}