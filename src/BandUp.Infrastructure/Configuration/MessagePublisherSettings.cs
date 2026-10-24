namespace BandUp.Infrastructure.Configuration;

public sealed class MessagePublisherSettings
{
    public const string SectionName = "MessagePublisher";

    public string Provider { get; set; } = ServiceProviders.RabbitMq;
}