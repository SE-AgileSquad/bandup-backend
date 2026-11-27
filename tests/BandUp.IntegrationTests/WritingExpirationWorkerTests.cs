using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Contracts.Messages;
using BandUp.Domain.Billing;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Messaging;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Worker;
using BandUp.Worker.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingExpirationWorkerTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    [Fact]
    public async Task RealWorkerExpiresWhileBrokerIsOfflineAndRecreatesItsConnectionsAfterEachOutage()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = limit.Token;
        var clock = new TestClock();
        var connectionString = await NewDatabase(ct);
        await using var setup = SeedFactory(connectionString, clock);
        var (expiredOwner, expired) = await Accept(setup);
        var deadline = (await Read(connectionString, expired, ct)).QueueDeadline;
        clock.Advance(deadline - clock.GetUtcNow());
        var (eligibleOwner, eligible) = await Accept(setup);

        await using var rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
            .WithUsername(BandUpTestEnvironment.UserName).WithPassword(BandUpTestEnvironment.Password).Build();
        await rabbit.StartAsync(ct);
        await using var relay = new TestBrokerRelay(rabbit.Hostname, rabbit.GetMappedPublicPort(5672));
        var settings = TestEnvironment.SettingsFor(connectionString);
        settings["RabbitMq:Host"] = IPAddress.Loopback.ToString();
        settings["RabbitMq:Port"] = relay.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings["Features:WritingSubmissionEnabled"] = "false";
        settings["Writing:Publishing:PollInterval"] = "00:00:00.100";
        settings["Writing:Publishing:ExpirationPollInterval"] = "00:00:00.100";
        var logs = new TestLogSink();
        using var worker = WorkerHost.Build([], logs, builder =>
        {
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddSingleton<TimeProvider>(clock);
        });

        using var startupLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startupLimit.CancelAfter(TimeSpan.FromSeconds(10));
        await worker.StartAsync(startupLimit.Token);
        try
        {
            await Eventually(ct, async () => (await Read(connectionString, expired, ct)).Status == WritingEvaluationStatus.Failed);
            await AssertRefunded(connectionString, expiredOwner, expired, ct);
            Assert.Equal(0, (await Read(connectionString, eligible, ct)).PublishAttempts);
            await Eventually(ct, () => Task.FromResult(logs.Events.Any(entry =>
                entry.RenderMessage().Contains("waiting for RabbitMQ", StringComparison.Ordinal))));

            relay.Open();
            await Eventually(ct, async () => (await Read(connectionString, eligible, ct)).PublishedAt is not null);
            await AssertPingAndSingleConsumer(worker, logs, ct);
            var provider = worker.Services.GetRequiredService<RabbitMqConnectionProvider>();
            var originalConnection = await provider.GetConnectionAsync(ct);
            await using (var channel = await originalConnection.CreateChannelAsync(cancellationToken: ct))
            {
                var delivery = await channel.BasicGetAsync(MessageRoutes.WritingEvaluationQueue, autoAck: true, cancellationToken: ct);
                Assert.NotNull(delivery);
                using var body = JsonDocument.Parse(delivery.Body);
                Assert.Equal(eligible, body.RootElement.GetProperty("submissionId").GetGuid());
            }

            await relay.CloseAsync();
            await Eventually(ct, () => Task.FromResult(!originalConnection.IsOpen));
            var nextDeadline = (await Read(connectionString, eligible, ct)).QueueDeadline;
            clock.Advance(nextDeadline - clock.GetUtcNow());
            await Eventually(ct, async () => (await Read(connectionString, eligible, ct)).Status == WritingEvaluationStatus.Failed);
            await AssertRefunded(connectionString, eligibleOwner, eligible, ct);

            relay.Open();
            await Eventually(ct, async () =>
            {
                try
                {
                    return !ReferenceEquals(originalConnection, await provider.GetConnectionAsync(ct));
                }
                catch (Exception exception) when (!ct.IsCancellationRequested &&
                    RabbitMqConnectionProvider.IsTransientFailure(exception))
                {
                    return false;
                }
            });
            await AssertPingAndSingleConsumer(worker, logs, ct);
            await relay.CloseAsync();
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(shutdown.Token);
            Assert.False(shutdown.IsCancellationRequested);
        }
    }

    [Fact]
    public async Task BoundedScanAdvancesPastAFailedRefundAndRevisitsItAfterLaterRows()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = limit.Token;
        var clock = new ExpirationClock();
        var connectionString = await NewDatabase(ct);
        var fault = new ExpirationFault();
        var logs = new TestLogSink();
        await using var factory = SeedFactory(connectionString, clock, logs, services =>
        {
            services.AddSingleton(fault);
            services.AddScoped<IPipelineBehavior<ExpireWriting, bool>, FailFirstExpiration>();
        }, maxAttempts: 1);
        var (owner, command) = await Seed(factory, 6);
        using var access = Scope(factory, owner);
        List<Guid> submissions = [];
        for (int i = 0; i < 6; i++)
        {
            submissions.Add((await Send(access, command with { IdempotencyKey = Guid.NewGuid() }, ct)).Value.SubmissionId);
        }
        var ids = submissions.Order().ToArray();
        var confirmed = Assert.IsType<WritingPublishReservation>(await Dispatch<ReserveWritingPublication, WritingPublishReservation?>(factory, new(), ct));
        Assert.Equal(ids[0], confirmed.SubmissionId);
        Assert.True(await Dispatch<ConfirmWritingPublication, bool>(factory, new(confirmed), ct));
        var exhausted = Assert.IsType<WritingPublishReservation>(await Dispatch<ReserveWritingPublication, WritingPublishReservation?>(factory, new(), ct));
        Assert.Equal(ids[1], exhausted.SubmissionId);
        Assert.True(await Dispatch<RecordWritingPublishFailure, bool>(factory, new(exhausted), ct));
        Assert.True(await Dispatch<ClaimWriting, bool>(factory, new(ids[3]), ct));
        Assert.True(await Dispatch<ClaimWriting, bool>(factory, new(ids[4]), ct));
        var criterion = new CriterionFeedback(7, "Explanation", [], [], ["Add an example."]);
        Assert.True(await Dispatch<CompleteWriting, bool>(factory,
            new(ids[4], new WritingFeedback(ids[4], "Summary", criterion, criterion, criterion, criterion)), ct));
        Assert.True(await Dispatch<ClaimWriting, bool>(factory, new(ids[5]), ct));
        Assert.True(await Dispatch<FailWriting, bool>(factory, new(ids[5]), ct));
        var unaffected = await Task.WhenAll(ids[3..].Select(id => Read(connectionString, id, ct)));

        var deadline = (await Read(connectionString, ids[0], ct)).QueueDeadline;
        clock.Advance(deadline - clock.GetUtcNow() - TimeSpan.FromMicroseconds(1));
        var active = Assert.IsType<WritingPublishReservation>(await Dispatch<ReserveWritingPublication, WritingPublishReservation?>(factory, new(), ct));
        Assert.Equal(ids[2], active.SubmissionId);
        fault.Target = ids[0];
        using var service = new WritingExpirationService(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WritingPublishingOptions { ExpirationBatchSize = 1 }), clock,
            factory.Services.GetRequiredService<ILogger<WritingExpirationService>>());
        await service.StartAsync(ct);
        try
        {
            var poll = await clock.Poll(ct);
            Assert.Equal(WritingEvaluationStatus.Queued, (await Read(connectionString, ids[0], ct)).Status);
            await AssertFinancialState(connectionString, owner, 1, 4, ct);

            clock.Advance(TimeSpan.FromMicroseconds(1));
            poll();
            poll = await clock.Poll(ct);
            // The first refund and terminal write were staged, then rolled back together.
            Assert.Equal(WritingEvaluationStatus.Queued, (await Read(connectionString, ids[0], ct)).Status);
            await AssertFinancialState(connectionString, owner, 1, 4, ct);
            Assert.Equal(1, fault.Failures);

            clock.Advance(TimeSpan.FromSeconds(1));
            poll();
            poll = await clock.Poll(ct);
            Assert.Equal(WritingEvaluationStatus.Failed, (await Read(connectionString, ids[1], ct)).Status);
            Assert.Equal(WritingEvaluationStatus.Queued, (await Read(connectionString, ids[2], ct)).Status);
            await AssertFinancialState(connectionString, owner, 2, 3, ct);

            poll();
            poll = await clock.Poll(ct);
            var leased = await Read(connectionString, ids[2], ct);
            Assert.Equal(WritingEvaluationStatus.Failed, leased.Status);
            Assert.Equal(active.LeaseId, leased.PublishLeaseId);
            await AssertFinancialState(connectionString, owner, 3, 2, ct);

            poll();
            poll = await clock.Poll(ct); // End of scan. The next bounded scan wraps to the failed candidate.
            poll();
            await clock.Poll(ct);
            Assert.Equal(WritingEvaluationStatus.Failed, (await Read(connectionString, ids[0], ct)).Status);
            Assert.Equal(WritingEvaluationStatus.Processing, (await Read(connectionString, ids[3], ct)).Status);
            Assert.Equal(WritingEvaluationStatus.Succeeded, (await Read(connectionString, ids[4], ct)).Status);
            Assert.Equal(WritingEvaluationStatus.Failed, (await Read(connectionString, ids[5], ct)).Status);
            foreach (var previous in unaffected)
            {
                var current = await Read(connectionString, previous.Id, ct);
                Assert.Equal(previous.Status, current.Status);
                Assert.Equal(previous.StartedAt, current.StartedAt);
                Assert.Equal(previous.CompletedAt, current.CompletedAt);
            }
            await AssertFinancialState(connectionString, owner, 4, 1, ct);
            await Task.WhenAll(
                Dispatch<ExpireWriting, bool>(factory, new(ids[0]), ct),
                Dispatch<ExpireWriting, bool>(factory, new(ids[0]), ct));
            await AssertFinancialState(connectionString, owner, 4, 1, ct);
        }
        finally
        {
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(stopping.Token);
        }

        Assert.Contains(logs.Events, entry => entry.RenderMessage().Contains("Writing expiration deferred", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Events, entry => (entry.RenderMessage() + entry.Exception)
            .Contains("private-expiration-data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentFirstExpirationCommitsExactlyOneRefund()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = limit.Token;
        var clock = new TestClock();
        var connectionString = await NewDatabase(ct);
        await using var factory = SeedFactory(connectionString, clock);
        var (owner, id) = await Accept(factory);
        var deadline = (await Read(connectionString, id, ct)).QueueDeadline;
        clock.Advance(deadline - clock.GetUtcNow());
        using var first = factory.Services.CreateScope();
        using var competing = factory.Services.CreateScope();
        await Work(first).BeginTransactionAsync(ct);
        await Work(competing).BeginTransactionAsync(ct);
        var command = new ExpireWriting(id);
        var winner = await first.ServiceProvider.GetRequiredService<ICommandHandler<ExpireWriting, bool>>().HandleAsync(command, ct);
        Assert.True(winner.Value);
        var waiting = competing.ServiceProvider.GetRequiredService<ICommandHandler<ExpireWriting, bool>>().HandleAsync(command, ct);
        await WritingBillingTests.WaitForBlockAsync(Db(first), Db(competing), ct);
        Assert.Equal(WritingEvaluationStatus.Queued, (await Read(connectionString, id, ct)).Status);
        await AssertFinancialState(connectionString, owner, 0, 1, ct);

        clock.Advance(TimeSpan.FromSeconds(1));
        await Work(first).SaveChangesAsync(ct);
        await Work(first).CommitTransactionAsync(ct);
        Assert.False((await waiting).Value);
        await Work(competing).SaveChangesAsync(ct);
        await Work(competing).CommitTransactionAsync(ct);

        var settled = await Read(connectionString, id, ct);
        Assert.Equal(WritingEvaluationStatus.Failed, settled.Status);
        Assert.Null(settled.StartedAt);
        Assert.Equal(deadline, settled.CompletedAt);
        await AssertRefunded(connectionString, owner, id, ct);
    }

    private async Task<string> NewDatabase(CancellationToken ct)
    {
        var name = $"bandup_expiration_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(MigratedConnectionString);
        await admin.OpenAsync(ct);
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await create.ExecuteNonQueryAsync(ct);
        var connectionString = new NpgsqlConnectionStringBuilder(MigratedConnectionString) { Database = name }.ConnectionString;
        await using var db = BandUpTestEnvironment.CreateContext(connectionString);
        await db.Database.MigrateAsync(ct);
        return connectionString;
    }

    private BandUpApiFactory SeedFactory(string connectionString, TimeProvider clock, TestLogSink? logs = null,
        Action<IServiceCollection>? configure = null, int maxAttempts = 5) =>
        new(TestEnvironment, overrides: new()
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Features:WritingSubmissionEnabled"] = "false",
            ["Writing:Publishing:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, logSink: logs, configureServices: services =>
        {
            services.AddScoped(_ => Substitute.For<ICurrentUser>());
            services.AddSingleton(clock);
            configure?.Invoke(services);
        });

    private static async Task<(Guid Owner, Guid Id)> Accept(BandUpApiFactory factory)
    {
        var (owner, command) = await Seed(factory);
        using var scope = Scope(factory, owner);
        return (owner, (await Send(scope, command)).Value.SubmissionId);
    }

    private static async Task<TResult> Dispatch<TCommand, TResult>(BandUpApiFactory factory, TCommand command, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        using var scope = factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        return (await dispatcher.SendAsync<TCommand, TResult>(command, ct)).Value;
    }

    private static async Task<WritingSubmission> Read(string connectionString, Guid id, CancellationToken ct)
    {
        await using var db = BandUpTestEnvironment.CreateContext(connectionString);
        return await db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id, ct);
    }

    private static async Task AssertRefunded(string connectionString, Guid owner, Guid id, CancellationToken ct)
    {
        await AssertFinancialState(connectionString, owner, 1, 0, ct);
        await using var db = BandUpTestEnvironment.CreateContext(connectionString);
        var refund = await db.CreditLedger.SingleAsync(e => e.UserId == owner && e.Reason == CreditLedgerEntry.WritingRefundReason, ct);
        Assert.Equal(id, refund.ReferenceId);
        Assert.Equal(1, refund.Delta);
        Assert.False(await db.WritingFeedbacks.AnyAsync(f => f.Id == id, ct));
    }

    private static async Task AssertFinancialState(string connectionString, Guid owner, int balance, int reserved, CancellationToken ct)
    {
        await using var db = BandUpTestEnvironment.CreateContext(connectionString);
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner, ct);
        Assert.Equal(balance, account.Balance);
        Assert.Equal(reserved, account.ReservedRefundCapacity);
        var ledger = await db.CreditLedger.Where(e => e.UserId == owner).ToListAsync(ct);
        Assert.Equal(balance, ledger.Sum(e => e.Delta));
        Assert.All(ledger.Where(e => e.Reason == CreditLedgerEntry.WritingRefundReason).GroupBy(e => e.ReferenceId),
            refunds => Assert.Single(refunds));
    }

    private static async Task AssertPingAndSingleConsumer(IHost host, TestLogSink logs, CancellationToken ct)
    {
        var connections = host.Services.GetRequiredService<RabbitMqConnectionProvider>();
        await Eventually(ct, async () =>
        {
            try
            {
                var connection = await connections.GetConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                var queue = await channel.QueueDeclarePassiveAsync(MessageRoutes.PingQueue, ct);
                return queue.ConsumerCount == 1;
            }
            catch (Exception exception) when (!ct.IsCancellationRequested &&
                RabbitMqConnectionProvider.IsTransientFailure(exception))
            {
                return false;
            }
        });

        var ping = new PingRequested(Guid.NewGuid(), "broker recovery probe", DateTimeOffset.UtcNow);
        await host.Services.GetRequiredService<IDirectMessagePublisher>().PublishAsync(ping, ct);
        await Eventually(ct, () => Task.FromResult(logs.Events.Any(entry =>
            entry.RenderMessage().Contains(ping.PingId.ToString(), StringComparison.OrdinalIgnoreCase))));
    }

    private static async Task Eventually(CancellationToken ct, Func<Task<bool>> condition)
    {
        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(75), ct);
        }
    }

    private sealed class ExpirationFault
    {
        public Guid Target { get; set; }
        public int Failures { get; set; }
    }

    private sealed class FailFirstExpiration(ExpirationFault fault) : IPipelineBehavior<ExpireWriting, bool>
    {
        public async Task<Result<bool>> HandleAsync(ExpireWriting request, CancellationToken ct, RequestHandlerDelegate<bool> next)
        {
            var result = await next();
            if (request.SubmissionId == fault.Target && result.IsSuccess && result.Value && fault.Failures == 0)
            {
                fault.Failures++;
                throw new IOException("private-expiration-data");
            }
            return result;
        }
    }

    private sealed class ExpirationClock : TimeProvider
    {
        private readonly Channel<Action> _polls = Channel.CreateUnbounded<Action>();
        private long _ticks = DateTimeOffset.UnixEpoch.AddYears(125).Ticks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
        public ValueTask<Action> Poll(CancellationToken ct) => _polls.Reader.ReadAsync(ct);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PollTimer(callback, state);
            _polls.Writer.TryWrite(timer.Fire);
            return timer;
        }

        private sealed class PollTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
