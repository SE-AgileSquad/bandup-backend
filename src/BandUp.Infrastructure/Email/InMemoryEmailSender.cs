using System.Collections.Concurrent;
using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Email;

/// <summary>Bounded offline inbox, accessible only through DI in local development/tests.</summary>
public sealed class InMemoryEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();
    public IReadOnlyList<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Enqueue(message);
        while (_messages.Count > 256)
        {
            _messages.TryDequeue(out _);
        }
        return Task.CompletedTask;
    }
}
