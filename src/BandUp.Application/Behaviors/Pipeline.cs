using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Behaviors;

internal static class Pipeline
{
    internal static Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> handler,
        IReadOnlyList<IPipelineBehavior<TRequest, TResponse>> behaviors)
    {
        RequestHandlerDelegate<TResponse> pipeline = handler;

        for (int index = behaviors.Count - 1; index >= 0; index--)
        {
            IPipelineBehavior<TRequest, TResponse> behavior = behaviors[index];
            RequestHandlerDelegate<TResponse> next = pipeline;
            pipeline = () => behavior.HandleAsync(request, cancellationToken, next);
        }

        return pipeline();
    }
}