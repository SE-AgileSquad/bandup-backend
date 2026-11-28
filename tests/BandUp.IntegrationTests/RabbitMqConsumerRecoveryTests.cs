using System.Threading.Channels;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Messaging;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class RabbitMqConsumerRecoveryTests(BandUpTestEnvironment environment)
{
    [Fact]
    public void ChannelTransportClosurePreservesPermanentBrokerErrors()
    {
        Assert.True(RabbitMqConnectionProvider.IsTransientFailure(new ChannelClosedException()));
        Assert.True(RabbitMqConnectionProvider.IsTransientFailure(new BrokerUnreachableException(
            new AggregateException(new ChannelClosedException(new IOException())))));
        Assert.False(RabbitMqConnectionProvider.IsTransientFailure(
            new ChannelClosedException(new AuthenticationFailureException("Authentication refused."))));
        Assert.False(RabbitMqConnectionProvider.IsTransientFailure(new ChannelClosedException(
            new OperationInterruptedException(new ShutdownEventArgs(ShutdownInitiator.Peer, 406, "Topology mismatch.")))));
    }

    [Fact]
    public async Task DisconnectCancelsAnActiveDeliveryAndRedeliversAfterTheOldHandlerExits()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = limit.Token;
        string queue = $"bandup.test.delivery.{Guid.NewGuid():N}";
        var routes = new MessageRoutes().Map<ProbeMessage>(queue);
        var topology = new RabbitMqTopology(routes);
        await using var relay = new TestBrokerRelay(environment.RabbitMqHost, environment.RabbitMqPort);
        relay.Open();
        await using var producerConnections = new RabbitMqConnectionProvider(
            Options.Create(Settings(environment.RabbitMqHost, environment.RabbitMqPort)), topology);
        await using var consumerConnections = new RabbitMqConnectionProvider(
            Options.Create(Settings("127.0.0.1", relay.Port)), topology);
        await using var publisher = new RabbitMqPublisher(producerConnections, routes);
        var cache = Substitute.For<ICacheService>();
        var marked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.SetAsync(Arg.Any<string>(), true, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                marked.TrySetResult();
                return Task.CompletedTask;
            });
        using var consumer = new ProbeConsumer(consumerConnections, topology, cache, queue);
        await consumer.StartAsync(ct);
        try
        {
            await publisher.PublishAsync(new ProbeMessage(Guid.NewGuid()), ct);
            await consumer.FirstEntered.Task.WaitAsync(ct);
            await relay.CloseAsync();
            await consumer.FirstCancelled.Task.WaitAsync(ct);
            Assert.False(marked.Task.IsCompleted);

            // Restore transport while the old callback is still deliberately held open.
            relay.Open();
            await Assert.ThrowsAsync<TimeoutException>(() =>
                consumer.Redelivered.Task.WaitAsync(TimeSpan.FromSeconds(2), ct));
            consumer.ReleaseFirst.TrySetResult();
            Assert.True(await consumer.Redelivered.Task.WaitAsync(ct));
            await marked.Task.WaitAsync(ct);
            await cache.Received(1).SetAsync(Arg.Any<string>(), true, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
            // Prefetch is one. A following delivery proves the redelivery was acknowledged.
            var following = new ProbeMessage(Guid.NewGuid());
            await publisher.PublishAsync(following, ct);
            Assert.Equal(following.Id, await consumer.FollowingDelivery.Task.WaitAsync(ct));
        }
        finally
        {
            consumer.ReleaseFirst.TrySetResult();
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await consumer.StopAsync(shutdown.Token);
        }
        var connection = await producerConnections.GetConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.QueueDeleteAsync(queue, ifUnused: true, ifEmpty: false, cancellationToken: ct);
    }

    private RabbitMqSettings Settings(string host, int port) => new()
    {
        Host = host,
        Port = port,
        User = environment.RabbitMqUser,
        Password = environment.RabbitMqPassword,
    };

    private sealed record ProbeMessage(Guid Id);

    private sealed class ProbeConsumer(RabbitMqConnectionProvider connections, RabbitMqTopology topology,
        ICacheService cache, string queue) : RabbitMqConsumerBase<ProbeMessage>(connections, topology, cache, NullLogger.Instance)
    {
        private int _deliveries;
        private int _firstExited;
        protected override string QueueName => queue;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Redelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Guid> FollowingDelivery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task HandleAsync(ProbeMessage message, CancellationToken cancellationToken)
        {
            int delivery = Interlocked.Increment(ref _deliveries);
            if (delivery == 2)
            {
                Redelivered.TrySetResult(Volatile.Read(ref _firstExited) == 1);
                return;
            }
            if (delivery > 2)
            {
                FollowingDelivery.TrySetResult(message.Id);
                return;
            }

            FirstEntered.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                FirstCancelled.SetResult();
                await ReleaseFirst.Task;
                throw;
            }
            finally
            {
                Volatile.Write(ref _firstExited, 1);
            }
        }
    }
}
