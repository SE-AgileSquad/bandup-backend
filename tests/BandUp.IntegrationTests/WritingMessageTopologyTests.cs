using System.Text.Json;
using BandUp.Contracts.Messages;
using BandUp.Infrastructure.Messaging;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingMessageTopologyTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task ApiStartupBindsTheWritingQueueBeforeAnyConsumerRuns()
    {
        await using BandUpApiFactory factory = new(environment);
        var services = factory.Services;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var connection = await services.GetRequiredService<RabbitMqConnectionProvider>().GetConnectionAsync(timeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        const string queue = "bandup.writing.evaluation.v1";
        var declared = await channel.QueueDeclarePassiveAsync(queue, timeout.Token);
        Assert.Equal(0u, declared.ConsumerCount);

        var id = Guid.NewGuid();
        await services.GetRequiredService<IDirectMessagePublisher>()
            .PublishAsync(new WritingEvaluationRequestedV1(id), timeout.Token);

        var received = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken: timeout.Token);
        Assert.NotNull(received);
        Assert.Equal("bandup.jobs", received.Exchange);
        Assert.Equal("writing.evaluation.requested.v1", received.RoutingKey);
        Assert.Equal(DeliveryModes.Persistent, received.BasicProperties.DeliveryMode);
        using var body = JsonDocument.Parse(received.Body);
        var property = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("submissionId", property.Name);
        Assert.Equal(id, property.Value.GetGuid());
        await channel.BasicAckAsync(received.DeliveryTag, multiple: false, timeout.Token);
    }
}
