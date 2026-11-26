using System.Threading.Channels;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Email;

public sealed class EmailDeliveryQueue(IOptions<EmailSettings> options, ILogger<EmailDeliveryQueue> logger)
{
    private readonly Channel<PendingEmail> _channel = Channel.CreateBounded<PendingEmail>(new BoundedChannelOptions(options.Value.QueueCapacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });

    internal ChannelReader<PendingEmail> Reader => _channel.Reader;

    public bool TryEnqueue(EmailMessage message, DateTimeOffset? expiresAt)
    {
        if (_channel.Writer.TryWrite(new PendingEmail(message, expiresAt)))
        {
            return true;
        }
        logger.LogWarning("Email delivery queue is full; the committed email could not be queued");
        return false;
    }

    internal sealed record PendingEmail(EmailMessage Message, DateTimeOffset? ExpiresAt);
}
