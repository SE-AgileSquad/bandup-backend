using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using Microsoft.Extensions.Logging;

namespace BandUp.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next)
    {
        string requestName = typeof(TRequest).Name;

        try
        {
            Result<TResponse> result = await next();

            if (result.IsSuccess)
            {
                logger.LogInformation("Handled {RequestName}", requestName);
            }
            else
            {
                logger.LogWarning(
                    "Handled {RequestName} with {ErrorCode}",
                    requestName,
                    result.Error!.Code);
            }

            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Handling {RequestName} failed", requestName);
            throw;
        }
    }
}