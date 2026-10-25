using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// Captures the log events a process actually emitted, so a test can assert on the structured
/// properties rather than on text.
/// </summary>
public sealed class TestLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public IReadOnlyList<LogEvent> Events => _events.ToArray();

    public void Clear() => _events.Clear();

    /// <summary>
    /// A request log line is written when the response completes, which can be a moment after
    /// the client has read it, so the sink is polled rather than read once.
    /// </summary>
    public async Task<LogEvent?> WaitForAsync(
        Func<LogEvent, bool> predicate,
        TimeSpan? timeout = null)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTimeOffset.UtcNow < deadline)
        {
            LogEvent? match = Events.FirstOrDefault(predicate);

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        return null;
    }
}