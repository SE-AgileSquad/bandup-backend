using System.Net;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class PasswordResetEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_15_Email_Handoff_Occurs_Only_After_The_Commit_Buffer_Is_Flushed(bool commit)
    {
        MessageBuffer buffer = new();
        EmailDeliveryQueue queue = NewQueue();
        InMemoryEmailSender sender = new();
        using EmailDeliveryWorker worker = new(queue, sender, new FakeTimeProvider(Now), NullLogger<EmailDeliveryWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var scheduled = new PostCommitEmailQueue(buffer, queue);
        Should.Throw<InvalidOperationException>(() => scheduled.Enqueue(new EmailMessage("user@example.com", "Outside transaction", "012345")));
        buffer.Open();
        scheduled.Enqueue(new EmailMessage("user@example.com", "Recovery", "012345"), Now.AddMinutes(10));
        sender.Messages.ShouldBeEmpty();
        if (commit)
        {
            await buffer.FlushAsync(CancellationToken.None);
        }
        else
        {
            buffer.Discard();
        }

        queue.TryEnqueue(new EmailMessage("barrier@example.com", "Barrier", "done"), null).ShouldBeTrue();
        await WaitForAsync(() => sender.Messages.Any(message => message.Subject == "Barrier"));
        sender.Messages.Count(message => message.Subject == "Recovery").ShouldBe(commit ? 1 : 0);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AUTH_15_Expired_Emails_Are_Skipped_And_Provider_Failures_Do_Not_Stop_Delivery_Or_Leak_Content()
    {
        EmailDeliveryQueue queue = NewQueue();
        IEmailSender sender = Substitute.For<IEmailSender>();
        TaskCompletionSource delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (call.Arg<EmailMessage>().Subject == "Fail")
            {
                return Task.FromException(new InvalidOperationException("private@example.com code 012345"));
            }

            delivered.TrySetResult();
            return Task.CompletedTask;
        });
        ILogger<EmailDeliveryWorker> logger = Substitute.For<ILogger<EmailDeliveryWorker>>();
        using EmailDeliveryWorker worker = new(queue, sender, new FakeTimeProvider(Now), logger);
        queue.TryEnqueue(new EmailMessage("private@example.com", "Expired", "012345"), Now);
        queue.TryEnqueue(new EmailMessage("private@example.com", "Fail", "012345"), Now.AddMinutes(1));
        queue.TryEnqueue(new EmailMessage("user@example.com", "Success", "654321"), Now.AddMinutes(1));
        await worker.StartAsync(CancellationToken.None);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        await sender.DidNotReceive().SendAsync(Arg.Is<EmailMessage>(message => message.Subject == "Expired"), Arg.Any<CancellationToken>());
        string logs = string.Join(" ", logger.ReceivedCalls().SelectMany(call => call.GetArguments()).Select(value => value?.ToString()));
        logs.ShouldNotContain("private@example.com");
        logs.ShouldNotContain("012345");
    }

    [Fact]
    public void AUTH_15_Full_Queue_Rejects_New_Email_Without_Blocking_The_Committed_Response()
    {
        var queue = NewQueue(1);
        queue.TryEnqueue(new EmailMessage("one@example.com", "One", "012345"), null).ShouldBeTrue();
        queue.TryEnqueue(new EmailMessage("two@example.com", "Two", "654321"), null).ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_15_Mailpit_Adapter_Uses_The_Documented_Send_Payload()
    {
        string? payload = null;
        Uri? target = null;
        using var handler = new RecordingHttpHandler(async request =>
        {
            target = request.RequestUri;
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        IHttpClientFactory clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(MailpitEmailSender.ClientName).Returns(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8025/") });
        MailpitEmailSender sender = new(clients, Options.Create(new EmailSettings()));
        await sender.SendAsync(new EmailMessage("user@example.com", "Recovery", "Code 012345"), CancellationToken.None);
        target.ShouldBe(new Uri("http://localhost:8025/api/v1/send"));
        using JsonDocument json = JsonDocument.Parse(payload!);
        json.RootElement.GetProperty("To")[0].GetProperty("Email").GetString().ShouldBe("user@example.com");
        json.RootElement.GetProperty("Text").GetString().ShouldBe("Code 012345");
        json.RootElement.GetProperty("From").GetProperty("Email").GetString().ShouldBe("no-reply@bandup.local");
    }

    [Theory]
    [InlineData("unknown", "http://localhost:8025/")]
    [InlineData("Mailpit", "https://external.example/")]
    [InlineData("Mailpit", "http://user:password@localhost:8025/")]
    public void AUTH_15_Email_Configuration_Rejects_Unsupported_Providers_And_Unsafe_Local_Inbox_Urls(string provider, string url)
    {
        ServiceCollection services = new();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "InMemory", ["MessagePublisher:Provider"] = "InMemory",
            ["Email:Provider"] = provider, ["Email:MailpitBaseUrl"] = url,
        }).Build());
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        Should.Throw<OptionsValidationException>(() => serviceProvider.GetRequiredService<IOptions<EmailSettings>>().Value);
    }

    private static EmailDeliveryQueue NewQueue(int capacity = 256) =>
        new(Options.Create(new EmailSettings { QueueCapacity = capacity }), NullLogger<EmailDeliveryQueue>.Instance);

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
