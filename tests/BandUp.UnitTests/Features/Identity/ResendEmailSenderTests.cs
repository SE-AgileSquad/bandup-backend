using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class ResendEmailSenderTests
{
    private const string Key = "re_unit_test_not_a_real_key";
    private const string PrivateContent = "private@example.com code 012345 re_private_provider_data";

    [Fact]
    public async Task AUTH_15_Resend_Uses_Bearer_Authentication_And_The_Documented_Https_Json_Payload()
    {
        using RecordingHandler handler = new(async (request, cancellationToken) =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri.ShouldBe(new Uri("https://api.resend.com/emails"));
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            request.Headers.Authorization.Parameter.ShouldBe(Key);
            request.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
            using JsonDocument payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            MailAddress from = new(payload.RootElement.GetProperty("from").GetString()!);
            from.Address.ShouldBe("no-reply@bandup.example");
            from.DisplayName.ShouldBe("BandUp Recovery");
            payload.RootElement.GetProperty("to")[0].GetString().ShouldBe("learner@example.com");
            payload.RootElement.GetProperty("subject").GetString().ShouldBe("Recovery");
            payload.RootElement.GetProperty("text").GetString().ShouldBe("Your code is 012345.");
            payload.RootElement.TryGetProperty("apiKey", out _).ShouldBeFalse();
            return Accepted();
        });
        await Sender(handler).SendAsync(new EmailMessage("learner@example.com", "Recovery", "Your code is 012345."), CancellationToken.None);
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AUTH_15_Resend_Rejects_Failed_Submissions_Without_Retrying_Or_Leaking_Provider_Content(int status)
    {
        using RecordingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(PrivateContent),
        }));
        HttpRequestException error = await Should.ThrowAsync<HttpRequestException>(() => Sender(handler).SendAsync(
            new EmailMessage("learner@example.com", "Recovery", "012345"), CancellationToken.None));
        error.StatusCode.ShouldBe((HttpStatusCode)status);
        AssertSanitized(error);
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"id\":null}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":123}")]
    [InlineData("private@example.com code 012345 re_private_provider_data")]
    public async Task AUTH_15_Resend_Requires_A_Submission_Id_And_Sanitizes_Malformed_Success_Responses(string body)
    {
        using RecordingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        HttpRequestException error = await Should.ThrowAsync<HttpRequestException>(() => Sender(handler).SendAsync(
            new EmailMessage("learner@example.com", "Recovery", "012345"), CancellationToken.None));
        AssertSanitized(error);
    }

    [Fact]
    public async Task AUTH_15_Resend_Sanitizes_Transport_Errors_And_Timeouts()
    {
        using RecordingHandler transport = new((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException(PrivateContent)));
        AssertSanitized(await Should.ThrowAsync<HttpRequestException>(() => Sender(transport).SendAsync(
            new EmailMessage("learner@example.com", "Recovery", "012345"), CancellationToken.None)));
        using RecordingHandler timeout = new((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException(PrivateContent)));
        AssertSanitized(await Should.ThrowAsync<TimeoutException>(() => Sender(timeout).SendAsync(
            new EmailMessage("learner@example.com", "Recovery", "012345"), CancellationToken.None)));
    }

    [Fact]
    public async Task AUTH_15_Resend_Preserves_Caller_Cancellation()
    {
        using CancellationTokenSource cancellation = new();
        using RecordingHandler handler = new((_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        await Should.ThrowAsync<OperationCanceledException>(() => Sender(handler).SendAsync(
            new EmailMessage("learner@example.com", "Recovery", "012345"), cancellation.Token));
    }

    [Theory]
    [InlineData("Resend", typeof(ResendEmailSender))]
    [InlineData("rEsEnD", typeof(ResendEmailSender))]
    [InlineData("Mailpit", typeof(MailpitEmailSender))]
    [InlineData("InMemory", typeof(InMemoryEmailSender))]
    public void AUTH_15_Email_Provider_Selection_Keeps_Local_Adapters_And_Only_Requires_Resend_Credentials_When_Selected(string selected, Type implementation)
    {
        using ServiceProvider provider = Services(new()
        {
            ["Email:Provider"] = selected,
            ["Email:ResendApiKey"] = selected.Equals("Resend", StringComparison.OrdinalIgnoreCase) ? Key : null,
        }).BuildServiceProvider();
        provider.GetRequiredService<IEmailSender>().GetType().ShouldBe(implementation);
    }

    [Theory]
    [InlineData("Email:ResendApiKey", null)]
    [InlineData("Email:ResendApiKey", "")]
    [InlineData("Email:ResendApiKey", "not-a-resend-key")]
    [InlineData("Email:ResendApiKey", "re_")]
    [InlineData("Email:ResendApiKey", "re_has space")]
    [InlineData("Email:ResendApiKey", "re_line\nbreak")]
    [InlineData("Email:FromEmail", "no-reply@bandup.local")]
    [InlineData("Email:FromEmail", "Label <sender@example.com>")]
    [InlineData("Email:FromName", "Name\r\nBcc: private@example.com")]
    public void AUTH_15_Resend_Configuration_Rejects_Missing_Credentials_And_Invalid_Senders_Without_Exposing_Values(string setting, string? value)
    {
        using ServiceProvider provider = Services(new() { [setting] = value }).BuildServiceProvider();
        OptionsValidationException error = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EmailSettings>>().Value);
        error.Message.ShouldContain(setting);
        error.Message.ShouldNotContain(Key);
        if (!string.IsNullOrEmpty(value) && !value.Equals("re_", StringComparison.Ordinal))
        {
            error.Message.ShouldNotContain(value);
        }
    }

    [Fact]
    public void AUTH_15_Resend_Transport_Disables_Redirects_And_Redacts_The_Authorization_Header()
    {
        using ServiceProvider provider = Services().BuildServiceProvider();
        HttpClientFactoryOptions options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(ResendEmailSender.ClientName);
        options.ShouldRedactHeaderValue("Authorization").ShouldBeTrue();
        HttpMessageHandlerBuilder builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.Name = ResendEmailSender.ClientName;
        foreach (Action<HttpMessageHandlerBuilder> action in options.HttpMessageHandlerBuilderActions) { action(builder); }
        using HttpMessageHandler primary = builder.PrimaryHandler;
        primary.ShouldBeOfType<HttpClientHandler>().AllowAutoRedirect.ShouldBeFalse();
        using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ResendEmailSender.ClientName);
        client.BaseAddress.ShouldBe(new Uri("https://api.resend.com/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
    }

    private static IServiceCollection Services(Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["Cache:Provider"] = "InMemory", ["MessagePublisher:Provider"] = "InMemory",
            ["Email:Provider"] = "Resend", ["Email:ResendApiKey"] = Key,
            ["Email:FromEmail"] = "no-reply@bandup.example", ["Email:FromName"] = "BandUp Recovery",
        };
        if (overrides is not null)
        {
            foreach ((string name, string? value) in overrides) { configuration[name] = value; }
        }
        return new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
    }

    private static ResendEmailSender Sender(HttpMessageHandler handler)
    {
        IHttpClientFactory clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(ResendEmailSender.ClientName).Returns(_ => new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.resend.com/"),
        });
        return new ResendEmailSender(clients, Options.Create(new EmailSettings
        {
            Provider = "Resend", ResendApiKey = Key, FromEmail = "no-reply@bandup.example", FromName = "BandUp Recovery",
        }));
    }

    private static HttpResponseMessage Accepted() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"id\":\"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794\"}", Encoding.UTF8, "application/json"),
    };

    private static void AssertSanitized(Exception error)
    {
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("private@example.com");
        error.ToString().ShouldNotContain("012345");
        error.ToString().ShouldNotContain("re_private_provider_data");
        error.ToString().ShouldNotContain(Key);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }
}
