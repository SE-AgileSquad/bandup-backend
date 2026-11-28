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
        Result<TResponse> result;
        try
        {
            messageBuffer.Open();
            result = await next();
            if (result.IsSuccess)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);
            }
        }
        catch (Exception original)
        {
            messageBuffer.Discard();
            try
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
            catch (Exception cleanup)
            {
                original.Data["TransactionCleanup"] = cleanup;
            }
            throw;
        }

        if (result.IsFailure)
        {
            messageBuffer.Discard();
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            return result;
        }

        // Publication failures occur after the database has committed.
        await messageBuffer.FlushAsync(cancellationToken);
        return result;
    }
}
