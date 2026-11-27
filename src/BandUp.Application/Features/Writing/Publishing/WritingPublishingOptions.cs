namespace BandUp.Application.Features.Writing.Publishing;

public sealed class WritingPublishingOptions
{
    public const string SectionName = "Writing:Publishing";
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int BatchSize { get; set; } = 10;
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public int BackoffMultiplier { get; set; } = 2;
    public TimeSpan ExpirationPollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int ExpirationBatchSize { get; set; } = 10;

    internal bool IsValid() =>
        IsBounded(PollInterval, TimeSpan.FromMinutes(1)) &&
        BatchSize is >= 1 and <= 100 &&
        MaxAttempts is >= 1 and <= 20 &&
        IsBounded(PublishTimeout, TimeSpan.FromMinutes(1)) &&
        IsBounded(LeaseDuration, TimeSpan.FromMinutes(5)) &&
        PublishTimeout < LeaseDuration &&
        IsBounded(InitialRetryDelay, TimeSpan.FromMinutes(1)) &&
        BackoffMultiplier is >= 1 and <= 10 &&
        IsBounded(ExpirationPollInterval, TimeSpan.FromMinutes(1)) &&
        ExpirationBatchSize is >= 1 and <= 100;

    internal TimeSpan RetryDelay(int failedAttempt)
    {
        long ticks = InitialRetryDelay.Ticks;
        // The validated multiplier is at most 10. Each multiplication starts below the one-hour cap.
        for (int attempt = 1; attempt < failedAttempt && ticks < MaxRetryDelay.Ticks; attempt++)
        {
            ticks = Math.Min(ticks * BackoffMultiplier, MaxRetryDelay.Ticks);
        }
        return TimeSpan.FromTicks(ticks);
    }

    private static bool IsBounded(TimeSpan value, TimeSpan upperBound) =>
        value >= TimeSpan.FromMilliseconds(1) && value <= upperBound;
}
