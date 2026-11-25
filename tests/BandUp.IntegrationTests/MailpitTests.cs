using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

public sealed class MailpitTests
{
    [Fact]
    public async Task AUTH_15_Local_Mailpit_Adapter_Delivers_The_Code_Using_The_Real_Http_Api()
    {
        await using IContainer inbox = new ContainerBuilder("axllent/mailpit:v1.27.0")
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025)))
            .Build();
        await inbox.StartAsync();
        string baseUrl = $"http://{inbox.Hostname}:{inbox.GetMappedPublicPort(8025)}/";
        ServiceCollection services = new();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "InMemory", ["MessagePublisher:Provider"] = "InMemory",
            ["Email:Provider"] = "Mailpit", ["Email:MailpitBaseUrl"] = baseUrl,
        }).Build());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IEmailSender>().SendAsync(
            new EmailMessage("learner@example.com", "BandUp recovery", "Your code is 012345."), CancellationToken.None);
        using HttpClient client = new() { BaseAddress = new Uri(baseUrl) };
        using JsonDocument message = JsonDocument.Parse(await client.GetStringAsync("api/v1/message/latest"));
        message.RootElement.GetProperty("Text").GetString().ShouldNotBeNull().ShouldContain("012345");
        message.RootElement.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("learner@example.com");
    }
}
