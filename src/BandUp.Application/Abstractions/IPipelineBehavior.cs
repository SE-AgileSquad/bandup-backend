using BandUp.Application.Common;

namespace BandUp.Application.Abstractions;

public delegate Task<Result<TResponse>> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next);
}

/// <summary>
/// Marks a behavior that runs for commands only. The dispatcher excludes these
/// from the query pipeline, so transaction control never wraps a read.
/// </summary>
public interface ICommandOnlyPipelineBehavior;