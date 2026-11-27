using System.Text.Json;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Contracts.Messages;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Messaging;

namespace BandUp.UnitTests.Features.Writing;

public sealed class WritingPublishingTests
{
    [Fact]
    public void EvaluationMessageCarriesOnlyTheSubmissionIdAndHasAnIndependentRoute()
    {
        var id = Guid.NewGuid();
        var body = JsonSerializer.SerializeToElement(new WritingEvaluationRequestedV1(id), JsonSerialization.Options);
        var property = Assert.Single(body.EnumerateObject());
        Assert.Equal("submissionId", property.Name);
        Assert.Equal(id, property.Value.GetGuid());
        var routes = MessageRoutes.CreateDefault();
        Assert.Equal("bandup.writing.evaluation.v1", routes.QueueFor<WritingEvaluationRequestedV1>());
        Assert.Equal("writing.evaluation.requested.v1", routes.RoutingKeyFor<WritingEvaluationRequestedV1>());
        Assert.Equal("system.ping", routes.RoutingKeyFor<PingRequested>());
        Assert.Equal("system.ping", routes.QueueFor<PingRequested>());
    }

    [Fact]
    public void DefaultsAreValidAndBackoffSaturatesBeforeItCanOverflow()
    {
        var defaults = new WritingPublishingOptions();
        Assert.True(defaults.IsValid());
        Assert.Equal(TimeSpan.FromSeconds(1), defaults.PollInterval);
        Assert.Equal(10, defaults.BatchSize);
        Assert.Equal(5, defaults.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), defaults.PublishTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(1), defaults.ExpirationPollInterval);
        Assert.Equal(10, defaults.ExpirationBatchSize);
        Assert.Equal(new[] { 1d, 2d, 4d, 8d },
            Enumerable.Range(1, 4).Select(attempt => defaults.RetryDelay(attempt).TotalSeconds).ToArray());

        var bounded = new WritingPublishingOptions
        {
            InitialRetryDelay = TimeSpan.FromMinutes(1), BackoffMultiplier = 10, MaxAttempts = 20,
        };
        Assert.True(bounded.IsValid());
        Assert.Equal(TimeSpan.FromHours(1), bounded.RetryDelay(19));
    }

    [Fact]
    public void RejectsOutOfRangeValuesAndTimeoutAtOrBeyondTheLease()
    {
        WritingPublishingOptions[] invalid =
        [
            new() { PollInterval = TimeSpan.Zero },
            new() { PollInterval = TimeSpan.MaxValue },
            new() { BatchSize = 0 },
            new() { BatchSize = 101 },
            new() { MaxAttempts = 0 },
            new() { MaxAttempts = 21 },
            new() { PublishTimeout = TimeSpan.Zero },
            new() { PublishTimeout = TimeSpan.FromSeconds(30) },
            new() { LeaseDuration = TimeSpan.FromSeconds(4) },
            new() { LeaseDuration = TimeSpan.MaxValue },
            new() { InitialRetryDelay = TimeSpan.FromSeconds(-1) },
            new() { InitialRetryDelay = TimeSpan.MaxValue },
            new() { BackoffMultiplier = 0 },
            new() { BackoffMultiplier = int.MaxValue },
            new() { ExpirationPollInterval = TimeSpan.Zero },
            new() { ExpirationPollInterval = TimeSpan.MaxValue },
            new() { ExpirationBatchSize = 0 },
            new() { ExpirationBatchSize = 101 },
        ];
        Assert.All(invalid, options => Assert.False(options.IsValid()));
    }
}
