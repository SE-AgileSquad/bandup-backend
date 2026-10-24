namespace BandUp.Application.Abstractions;

/// <summary>
/// Holds messages produced while a transaction is open so they can be released after it commits.
/// </summary>
public interface IMessageBuffer
{
    bool IsOpen { get; }

    void Open();

    /// <summary>
    /// Defers a publish until the transaction commits. The delegate keeps the message's
    /// generic type, so no reflection is needed at flush time.
    /// </summary>
    void Enqueue(Func<CancellationToken, Task> publish);

    Task FlushAsync(CancellationToken cancellationToken);

    void Discard();
}