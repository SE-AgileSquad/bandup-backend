using System.Text.Json;
using System.Threading.Channels;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Contracts.Messages;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Messaging;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Worker.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using RabbitMQ.Client;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingBackgroundPublisherTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    [Fact]
    public async Task PublishesCommittedLeasesSequentiallyInBatchesAndDeliversOnlyIds()
    {
        using var limit = TestDeadline();
        var clock = new PublisherClock();
        var logs = new TestLogSink();
        var (routes, queue) = IsolatedRoute();
        await using var factory = await IsolatedFactory(clock, logs, routes, limit.Token);
        var (owner, ids) = await Accept(factory, 3);
        var baseline = await BusinessState(factory, owner);
        var transport = factory.Services.GetRequiredService<IDirectMessagePublisher>();
        var started = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        var publisher = new ProbePublisher(async (message, ct) =>
        {
            await VerifyCommittedLeaseIsUnlocked(factory, message.SubmissionId, ct);
            if (Interlocked.Increment(ref count) == 1)
            {
                started.SetResult(message.SubmissionId);
                await release.Task.WaitAsync(ct);
            }
            await transport.PublishAsync(message, ct);
        });

        using var worker = CreatePublisher(factory, publisher, clock, batchSize: 2);
        await worker.StartAsync(limit.Token);
        try
        {
            var first = await started.Task.WaitAsync(limit.Token);
            Assert.Equal(ids[0], first);
            Assert.Equal(1, Volatile.Read(ref count));
            Assert.Equal(0, (await Read(factory, ids[1])).PublishAttempts);
            release.SetResult();

            var nextPoll = await clock.NextPoll(limit.Token);
            Assert.Equal(2, Volatile.Read(ref count));
            Assert.NotNull((await Read(factory, ids[0])).PublishedAt);
            Assert.NotNull((await Read(factory, ids[1])).PublishedAt);
            Assert.Equal(0, (await Read(factory, ids[2])).PublishAttempts);
            nextPoll.Fire();
            await clock.NextPoll(limit.Token);
            Assert.Equal(3, Volatile.Read(ref count));
            Assert.NotNull((await Read(factory, ids[2])).PublishedAt);
        }
        finally
        {
            await worker.StopAsync(limit.Token);
        }

        var connection = await factory.Services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(limit.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: limit.Token);
        var messageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expected in ids)
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: true, cancellationToken: limit.Token);
            Assert.NotNull(delivery);
            Assert.Equal(DeliveryModes.Persistent, delivery.BasicProperties.DeliveryMode);
            Assert.False(string.IsNullOrEmpty(delivery.BasicProperties.MessageId));
            Assert.True(messageIds.Add(delivery.BasicProperties.MessageId!));
            using var body = JsonDocument.Parse(delivery.Body);
            var field = Assert.Single(body.RootElement.EnumerateObject());
            Assert.Equal("submissionId", field.Name);
            Assert.Equal(expected, field.Value.GetGuid());
        }
        Assert.Null(await channel.BasicGetAsync(queue, autoAck: true, cancellationToken: limit.Token));
        await channel.QueueDeleteAsync(queue, ifUnused: true, ifEmpty: true, cancellationToken: limit.Token);
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    [Fact]
    public async Task UnroutableMandatoryMessageSchedulesRetryInsteadOfConfirmation()
    {
        using var limit = TestDeadline();
        var clock = new PublisherClock();
        var logs = new TestLogSink();
        await using var factory = await IsolatedFactory(clock, logs, MessageRoutes.CreateDefault(), limit.Token);
        var (owner, ids) = await Accept(factory, 1);
        var baseline = await BusinessState(factory, owner);
        var routes = new MessageRoutes().Map<WritingEvaluationRequestedV1>("unused", $"unbound.{Guid.NewGuid():N}");
        await using var unroutable = new RabbitMqPublisher(
            factory.Services.GetRequiredService<RabbitMqConnectionProvider>(), routes);

        using var worker = CreatePublisher(factory, unroutable, clock, batchSize: 1);
        await worker.StartAsync(limit.Token);
        try
        {
            await clock.NextPoll(limit.Token);
            await AssertRetry(factory, ids[0], clock);
        }
        finally
        {
            await worker.StopAsync(limit.Token);
        }

        Assert.Contains(logs.Events, entry =>
            entry.Level == Serilog.Events.LogEventLevel.Warning &&
            entry.Exception is null &&
            entry.Properties.TryGetValue("FailureType", out var failureType) &&
            failureType is Serilog.Events.ScalarValue
            {
                Value: "PublishReturnException"
            });
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    [Fact]
    public async Task TimeoutCancelsAndAwaitsTheTransportBeforeSchedulingRetryAndShutdownLeavesAnUncertainLease()
    {
        using var limit = TestDeadline();
        var clock = new PublisherClock();
        var logs = new TestLogSink();
        await using var factory = await IsolatedFactory(clock, logs, MessageRoutes.CreateDefault(), limit.Token);
        var (owner, ids) = await Accept(factory, 1);
        var baseline = await BusinessState(factory, owner);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishTransport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        var transport = new ProbePublisher(async (_, ct) =>
        {
            bool first = Interlocked.Increment(ref attempts) == 1;
            (first ? firstStarted : secondStarted).SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (first)
            {
                cancelled.SetResult();
                await finishTransport.Task.WaitAsync(limit.Token);
                throw new IOException("private-essay-or-feedback");
            }
        });

        using var worker = CreatePublisher(factory, transport, clock, batchSize: 1);
        await worker.StartAsync(limit.Token);
        try
        {
            await firstStarted.Task.WaitAsync(limit.Token);
            clock.Advance(TimeSpan.FromSeconds(5));
            (await clock.NextTimeout(limit.Token)).Fire();
            await cancelled.Task.WaitAsync(limit.Token);
            var inFlight = await Read(factory, ids[0]);
            Assert.NotNull(inFlight.PublishLeaseId);
            Assert.Null(inFlight.NextPublishAttemptAt);

            finishTransport.SetResult();
            var nextPoll = await clock.NextPoll(limit.Token);
            await AssertRetry(factory, ids[0], clock);
            clock.Advance(TimeSpan.FromSeconds(1));
            nextPoll.Fire();
            await secondStarted.Task.WaitAsync(limit.Token);
        }
        finally
        {
            finishTransport.TrySetResult();
            await worker.StopAsync(limit.Token);
        }

        var interrupted = await Read(factory, ids[0]);
        Assert.Equal(2, interrupted.PublishAttempts);
        Assert.NotNull(interrupted.PublishLeaseId);
        Assert.Null(interrupted.PublishedAt);
        Assert.Contains(logs.Events, entry => entry.RenderMessage().Contains("timeout: True", StringComparison.OrdinalIgnoreCase));
        AssertLogOmits(logs, "private-essay-or-feedback");
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    [Fact]
    public async Task DatabaseFailuresBackOffAndFailedConfirmationIsRecoveredByANewLease()
    {
        using var limit = TestDeadline();
        var clock = new PublisherClock();
        var logs = new TestLogSink();
        var faults = new DatabaseFaults();
        await using var factory = await IsolatedFactory(clock, logs, MessageRoutes.CreateDefault(), limit.Token,
            services =>
            {
                services.AddSingleton(faults);
                services.AddScoped<IPipelineBehavior<ReserveWritingPublication, WritingPublishReservation?>, FailFirstReservation>();
                services.AddScoped<IPipelineBehavior<ConfirmWritingPublication, bool>, FailFirstConfirmation>();
            });
        var (owner, ids) = await Accept(factory, 1);
        var baseline = await BusinessState(factory, owner);
        var transport = new ProbePublisher((_, _) => Task.CompletedTask);
        using var worker = CreatePublisher(factory, transport, clock, batchSize: 1);
        await worker.StartAsync(limit.Token);
        try
        {
            var poll = await clock.NextPoll(limit.Token);
            Assert.Equal(0, (await Read(factory, ids[0])).PublishAttempts);
            poll.Fire();
            poll = await clock.NextPoll(limit.Token);
            var abandoned = await Read(factory, ids[0]);
            Assert.Equal(1, abandoned.PublishAttempts);
            Assert.Null(abandoned.PublishedAt);
            Assert.NotNull(abandoned.PublishLeaseId);

            poll.Fire();
            poll = await clock.NextPoll(limit.Token);
            Assert.Equal(abandoned.PublishLeaseId, (await Read(factory, ids[0])).PublishLeaseId);
            clock.Advance(TimeSpan.FromSeconds(30));
            poll.Fire();
            await clock.NextPoll(limit.Token);
            var recovered = await Read(factory, ids[0]);
            Assert.Equal(2, recovered.PublishAttempts);
            Assert.NotNull(recovered.PublishedAt);
            Assert.Null(recovered.PublishLeaseId);
        }
        finally
        {
            await worker.StopAsync(limit.Token);
        }

        Assert.Equal(2, logs.Events.Count(entry => entry.RenderMessage().Contains("publication cycle deferred", StringComparison.Ordinal)));
        AssertLogOmits(logs, "private-database-detail");
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    [Fact]
    public async Task TransientBrokerFailureStopsTheBatchBeforeLaterSubmissionsSpendAnAttempt()
    {
        using var limit = TestDeadline();
        var clock = new PublisherClock();
        var logs = new TestLogSink();
        await using var factory = await IsolatedFactory(clock, logs, MessageRoutes.CreateDefault(), limit.Token);
        var (owner, ids) = await Accept(factory, 3);
        var baseline = await BusinessState(factory, owner);
        int sends = 0;
        var transport = new ProbePublisher((_, _) => Interlocked.Increment(ref sends) == 1
            ? Task.FromException(new IOException("Broker disconnected mid-batch."))
            : Task.CompletedTask);
        using var worker = CreatePublisher(factory, transport, clock, batchSize: 3);
        await worker.StartAsync(limit.Token);
        try
        {
            var nextPoll = await clock.NextPoll(limit.Token);
            Assert.Equal(1, Volatile.Read(ref sends));
            await AssertRetry(factory, ids[0], clock);
            foreach (var id in ids[1..])
            {
                var pending = await Read(factory, id);
                Assert.Equal(0, pending.PublishAttempts);
                Assert.Null(pending.PublishLeaseId);
                Assert.Null(pending.PublishedAt);
            }

            nextPoll.Fire();
            await clock.NextPoll(limit.Token);
            Assert.Equal(3, Volatile.Read(ref sends));
            Assert.NotNull((await Read(factory, ids[1])).PublishedAt);
            Assert.NotNull((await Read(factory, ids[2])).PublishedAt);
        }
        finally
        {
            await worker.StopAsync(limit.Token);
        }
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    [Fact]
    public async Task StaleConfirmationDoesNotOverwriteAReplacementLeaseOrStopTheBatch()
    {
        using var limit = TestDeadline();
        var clock = new PublisherClock();
        var logs = new TestLogSink();
        await using var factory = await IsolatedFactory(clock, logs, MessageRoutes.CreateDefault(), limit.Token);
        var (owner, ids) = await Accept(factory, 2);
        var baseline = await BusinessState(factory, owner);
        WritingPublishReservation? replacement = null;
        var transport = new ProbePublisher(async (message, ct) =>
        {
            if (message.SubmissionId == ids[0])
            {
                clock.Advance(TimeSpan.FromSeconds(30));
                using var competitor = factory.Services.CreateScope();
                var dispatch = competitor.ServiceProvider.GetRequiredService<IDispatcher>();
                replacement = (await dispatch.SendAsync<ReserveWritingPublication, WritingPublishReservation?>(new(), ct)).Value;
                Assert.Equal(ids[0], replacement?.SubmissionId);
            }
        });

        using var worker = CreatePublisher(factory, transport, clock, batchSize: 2);
        await worker.StartAsync(limit.Token);
        try
        {
            await clock.NextPoll(limit.Token);
            var stale = await Read(factory, ids[0]);
            Assert.Equal(2, stale.PublishAttempts);
            Assert.Equal(replacement?.LeaseId, stale.PublishLeaseId);
            Assert.Null(stale.PublishedAt);
            Assert.NotNull((await Read(factory, ids[1])).PublishedAt);
        }
        finally
        {
            await worker.StopAsync(limit.Token);
        }
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    private static CancellationTokenSource TestDeadline() => new(TimeSpan.FromSeconds(45));

    private static void AssertLogOmits(TestLogSink logs, string secret) =>
        Assert.DoesNotContain(logs.Events, entry =>
            (entry.RenderMessage() + entry.Exception + string.Join(" ", entry.Properties.Values))
                .Contains(secret, StringComparison.Ordinal));

    private static (MessageRoutes Routes, string Queue) IsolatedRoute()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var queue = $"bandup.test.writing.{suffix}";
        return (MessageRoutes.CreateDefault().Map<WritingEvaluationRequestedV1>(queue, $"writing.test.{suffix}"), queue);
    }

    private async Task<BandUpApiFactory> IsolatedFactory(PublisherClock clock, TestLogSink logs, MessageRoutes routes,
        CancellationToken ct, Action<IServiceCollection>? configure = null)
    {
        string database = $"bandup_publish_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(MigratedConnectionString))
        {
            await admin.OpenAsync(ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync(ct);
        }
        var connectionString = new NpgsqlConnectionStringBuilder(MigratedConnectionString) { Database = database }.ConnectionString;
        await using (var db = BandUpTestEnvironment.CreateContext(connectionString))
        {
            await db.Database.MigrateAsync(ct);
        }
        return new BandUpApiFactory(TestEnvironment,
            overrides: new() { ["ConnectionStrings:Default"] = connectionString, ["Features:WritingSubmissionEnabled"] = "false" },
            logSink: logs, configureServices: services =>
            {
                services.AddScoped(_ => Substitute.For<ICurrentUser>());
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(routes);
                configure?.Invoke(services);
            });
    }

    private static WritingBackgroundPublisher CreatePublisher(BandUpApiFactory factory, IDirectMessagePublisher publisher,
        PublisherClock clock, int batchSize) =>
        new(factory.Services.GetRequiredService<IServiceScopeFactory>(), publisher,
            Options.Create(new WritingPublishingOptions { BatchSize = batchSize }), clock,
            factory.Services.GetRequiredService<ILogger<WritingBackgroundPublisher>>());

    private static async Task<(Guid Owner, Guid[] Ids)> Accept(BandUpApiFactory factory, int count)
    {
        var (owner, command) = await Seed(factory, count);
        using var scope = Scope(factory, owner);
        List<Guid> ids = [];
        for (int i = 0; i < count; i++)
        {
            ids.Add((await Send(scope, command with { IdempotencyKey = Guid.NewGuid() })).Value.SubmissionId);
        }
        return (owner, ids.Order().ToArray());
    }

    private static async Task VerifyCommittedLeaseIsUnlocked(BandUpApiFactory factory, Guid id, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // A separate transaction must see the committed lease and obtain the row lock immediately.
        var lease = await db.Database.SqlQuery<Guid>($"""
            SELECT publish_lease_id AS "Value" FROM writing.writing_submissions
            WHERE id = {id} AND publish_attempts = 1 AND publish_lease_id IS NOT NULL
            FOR UPDATE NOWAIT
            """).ToListAsync(ct);
        Assert.NotEqual(Guid.Empty, Assert.Single(lease));
        await transaction.RollbackAsync(ct);
    }

    private static async Task<WritingSubmission> Read(BandUpApiFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await Db(scope).WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private static async Task AssertRetry(BandUpApiFactory factory, Guid id, PublisherClock clock)
    {
        var row = await Read(factory, id);
        Assert.Equal(1, row.PublishAttempts);
        Assert.Equal(clock.GetUtcNow().AddSeconds(1), row.NextPublishAttemptAt);
        Assert.Null(row.PublishLeaseId);
        Assert.Null(row.PublishedAt);
    }

    private static async Task<string> BusinessState(BandUpApiFactory factory, Guid owner)
    {
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var account = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        var ledger = await db.CreditLedger.AsNoTracking().Where(e => e.UserId == owner).OrderBy(e => e.Id).ToListAsync();
        var submissions = await db.WritingSubmissions.AsNoTracking().Where(s => s.UserId == owner).OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Status, s.StartedAt, s.CompletedAt, s.EssayText, s.SubmittedAt, s.QueueDeadline }).ToListAsync();
        int feedbackRows = await db.WritingFeedbacks.CountAsync();
        return JsonSerializer.Serialize(new { account, ledger, submissions, feedbackRows });
    }

    private sealed class ProbePublisher(Func<WritingEvaluationRequestedV1, CancellationToken, Task> send) : IDirectMessagePublisher
    {
        public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken) where TMessage : class =>
            send(Assert.IsType<WritingEvaluationRequestedV1>(message), cancellationToken);
    }

    private sealed class DatabaseFaults
    {
        public int Reservations { get; set; }
        public int Confirmations { get; set; }
    }

    private sealed class FailFirstReservation(DatabaseFaults faults)
        : IPipelineBehavior<ReserveWritingPublication, WritingPublishReservation?>
    {
        public Task<Result<WritingPublishReservation?>> HandleAsync(ReserveWritingPublication request, CancellationToken ct,
            RequestHandlerDelegate<WritingPublishReservation?> next)
        {
            if (++faults.Reservations == 1)
            {
                throw new IOException("private-database-detail");
            }
            return next();
        }
    }

    private sealed class FailFirstConfirmation(DatabaseFaults faults) : IPipelineBehavior<ConfirmWritingPublication, bool>
    {
        public async Task<Result<bool>> HandleAsync(ConfirmWritingPublication request, CancellationToken ct,
            RequestHandlerDelegate<bool> next)
        {
            var result = await next();
            if (++faults.Confirmations == 1)
            {
                throw new IOException("private-database-detail");
            }
            return result;
        }
    }

    private sealed class PublisherClock : TimeProvider
    {
        private readonly Channel<ManualTimer> _polls = Channel.CreateUnbounded<ManualTimer>();
        private readonly Channel<ManualTimer> _timeouts = Channel.CreateUnbounded<ManualTimer>();
        private long _ticks = new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
        public ValueTask<ManualTimer> NextPoll(CancellationToken ct) => _polls.Reader.ReadAsync(ct);
        public ValueTask<ManualTimer> NextTimeout(CancellationToken ct) => _timeouts.Reader.ReadAsync(ct);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            var destination = dueTime == TimeSpan.FromSeconds(1) ? _polls : _timeouts;
            destination.Writer.TryWrite(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;

        public void Fire()
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
