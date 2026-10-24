using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    IMessageBuffer messageBuffer)
    : IPipelineBehavior<TRequest, TResponse>, ICommandOnlyPipelineBehavior
{
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        messageBuffer.Open();

        try
        {
            Result<TResponse> result = await next();

            if (result.IsFailure)
            {
                messageBuffer.Discard();
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return result;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            // Only now is the change visible to a consumer, so deferred messages go out here.
            await messageBuffer.FlushAsync(cancellationToken);

            return result;
        }
        catch
        {
            messageBuffer.Discard();
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}