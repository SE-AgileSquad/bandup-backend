using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Scope-lived hold for messages produced inside a transaction. Flushing releases them in
/// publication order; discarding drops them, which is what a rollback requires.
/// </summary>
public sealed class MessageBuffer : IMessageBuffer
{
    private readonly List<Func<CancellationToken, Task>> _pending = [];

    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;

    public void Enqueue(Func<CancellationToken, Task> publish) => _pending.Add(publish);

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task>[] pending = [.. _pending];
        _pending.Clear();
        IsOpen = false;

        foreach (Func<CancellationToken, Task> publish in pending)
        {
            await publish(cancellationToken);
        }
    }

    public void Discard()
    {
        _pending.Clear();
        IsOpen = false;
    }
}