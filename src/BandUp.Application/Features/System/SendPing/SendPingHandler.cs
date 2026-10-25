using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Contracts.Messages;

namespace BandUp.Application.Features.System.SendPing;

public sealed class SendPingHandler(IMessagePublisher publisher, TimeProvider timeProvider)
    : ICommandHandler<SendPingCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        SendPingCommand command,
        CancellationToken cancellationToken)
    {
        Guid pingId = Guid.CreateVersion7();

        await publisher.PublishAsync(
            new PingRequested(pingId, command.Message, timeProvider.GetUtcNow()),
            cancellationToken);

        return Result<Guid>.Success(pingId);
    }
}
