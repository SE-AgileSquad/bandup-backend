using System.Text.Json;
using BandUp.Contracts.Messages;
using BandUp.Infrastructure.Messaging;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingMessageTopologyTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task ApiStartupBindsTheWritingQueueBeforeAnyConsumerRuns()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var broker = new RabbitMqBuilder("rabbitmq:4-management-alpine")
            .WithUsername(BandUpTestEnvironment.UserName)
            .WithPassword(BandUpTestEnvironment.Password)
            .Build();
        await broker.StartAsync(timeout.Token);
        var settings = new ConnectionFactory
        {
            HostName = broker.Hostname,
            Port = broker.GetMappedPublicPort(5672),
            UserName = BandUpTestEnvironment.UserName,
            Password = BandUpTestEnvironment.Password,
            AutomaticRecoveryEnabled = false,
        };
        await using BandUpApiFactory factory = new(environment, overrides: new()
        {
            ["RabbitMq:Host"] = settings.HostName,
            ["RabbitMq:Port"] = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:User"] = settings.UserName,
            ["RabbitMq:Password"] = settings.Password,
        });
        var services = factory.Services;
        // The production provider declares topology on connect. A raw connection can only observe it.
        await using var connection = await settings.CreateConnectionAsync(timeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        const string queue = "bandup.writing.evaluation.v1";
        await channel.ExchangeDeclarePassiveAsync("bandup.jobs", timeout.Token);
        var declared = await channel.QueueDeclarePassiveAsync(queue, timeout.Token);
        Assert.Equal(0u, declared.ConsumerCount);
        Assert.Equal(0u, declared.MessageCount);

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
