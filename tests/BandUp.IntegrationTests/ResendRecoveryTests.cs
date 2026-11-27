using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class ResendRecoveryTests(BandUpTestEnvironment environment)
{
    private const string Key = "re_integration_test_not_a_real_key";
    private const string OldPassword = "OldPassword123!";
    private const string NewPassword = "NewPassword456!";

    [Fact]
    public async Task AUTH_15_Resend_Selection_Completes_The_Public_Recovery_Flow_With_An_Offline_Http_Transport()
    {
        using ProviderHandler provider = new();
        TestLogSink logs = new();
        await using BandUpApiFactory factory = Factory(provider, logs);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        string email = await RegisterAsync(client);
        using HttpResponseMessage forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        SentEmail first = await provider.WaitForAsync(1);
        first.Recipient.ShouldBe(email);
        string code = Regex.Match(first.Text, "[0-9]{6}").Value;
        code.Length.ShouldBe(6);
        (await forgot.Content.ReadAsStringAsync()).ShouldNotContain(code);
        await using (BandUpDbContext check = CreateContext())
        {
            var user = await check.Users.SingleAsync(user => user.Email == email);
            var challenge = await check.PasswordResetChallenges.SingleAsync(challenge => challenge.UserId == user.Id);
            challenge.CodeMac.ShouldNotBe(code);
            challenge.ConsumedAt.ShouldBeNull();
        }
        string resetToken = await RecoveryTestFlow.VerifyAsync(client, email, code);
        using HttpResponseMessage reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { resetToken, newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SentEmail notification = await provider.WaitForAsync(2);
        notification.Recipient.ShouldBe(email);
        notification.Subject.ShouldContain("changed");
        notification.Text.ShouldNotContain(NewPassword);
        notification.Text.ShouldNotContain(code);
        using HttpResponseMessage oldLogin = await LoginAsync(client, email, OldPassword);
        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using HttpResponseMessage newLogin = await LoginAsync(client, email, NewPassword);
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        string renderedLogs = Render(logs);
        renderedLogs.ShouldNotContain(resetToken);
        renderedLogs.ShouldNotContain(Key);
        renderedLogs.ShouldNotContain(email);
        renderedLogs.ShouldNotContain(code);
        renderedLogs.ShouldNotContain(NewPassword);
    }

    [Fact]
    public async Task AUTH_15_Resend_Delivery_Rejection_Preserves_The_Generic_Http_Acknowledgement_And_Committed_Challenge()
    {
        using ProviderHandler provider = new() { Status = HttpStatusCode.Forbidden };
        TestLogSink logs = new();
        await using BandUpApiFactory factory = Factory(provider, logs);
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);
        using HttpResponseMessage forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await logs.WaitForAsync(entry => entry.RenderMessage().Contains("Email delivery failed"))).ShouldNotBeNull();
        SentEmail attempted = await provider.WaitForAsync(1);
        using HttpResponseMessage suppressed = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        suppressed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await suppressed.Content.ReadAsStringAsync()).ShouldBe(await forgot.Content.ReadAsStringAsync());
        provider.Messages.Count.ShouldBe(1);
        await using BandUpDbContext check = CreateContext();
        var user = await check.Users.SingleAsync(user => user.Email == email);
        var challenge = await check.PasswordResetChallenges.SingleAsync(challenge => challenge.UserId == user.Id);
        challenge.ConsumedAt.ShouldBeNull();
        challenge.InvalidatedAt.ShouldBeNull();
        user.PasswordHash.ShouldNotBeNullOrWhiteSpace();
        string renderedLogs = Render(logs);
        renderedLogs.ShouldNotContain("private-provider-response");
        renderedLogs.ShouldNotContain(Key);
        renderedLogs.ShouldNotContain(email);
        renderedLogs.ShouldNotContain(Regex.Match(attempted.Text, "[0-9]{6}").Value);
    }

    [Theory]
    [InlineData("Email:ResendApiKey", "")]
    [InlineData("Email:ResendApiKey", "invalid-key-marker")]
    [InlineData("Email:FromEmail", "no-reply@bandup.local")]
    public async Task AUTH_15_Api_Rejects_Invalid_Selected_Resend_Configuration_Before_Accepting_Requests(string setting, string value)
    {
        Dictionary<string, string?> configuration = Configuration();
        configuration[setting] = value;
        await using BandUpApiFactory factory = new(environment, overrides: configuration);
        OptionsValidationException error = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
        error.Message.ShouldContain(setting);
        error.Message.ShouldNotContain(Key);
        if (value.Length > 0) { error.Message.ShouldNotContain(value); }
    }

    private BandUpApiFactory Factory(ProviderHandler provider, TestLogSink logs) => new(environment,
        overrides: Configuration(), logSink: logs, configureServices: services =>
            services.AddHttpClient(ResendEmailSender.ClientName).ConfigurePrimaryHttpMessageHandler(() => provider));

    private static Dictionary<string, string?> Configuration() => new()
    {
        ["Email:Provider"] = "Resend", ["Email:ResendApiKey"] = Key,
        ["Email:FromEmail"] = "no-reply@bandup.example", ["Email:FromName"] = "BandUp tests",
    };

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        string email = $"resend-{Guid.CreateVersion7():N}@example.com";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = OldPassword, displayName = "Resend test", captchaToken = "fake-captcha-token",
        });
        registration.StatusCode.ShouldBe(HttpStatusCode.Created);
        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password, captchaToken = "fake-captcha-token" });
    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
    private static string Render(TestLogSink logs) => string.Join('\n', logs.Events.Select(entry => entry.RenderMessage() + " " + string.Join(' ', entry.Properties)));
    private sealed record SentEmail(string Recipient, string Subject, string Text);

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public ConcurrentQueue<SentEmail> Messages { get; } = new();
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri.ShouldBe(new Uri("https://api.resend.com/emails"));
            request.Headers.Authorization!.Parameter.ShouldBe(Key);
            using JsonDocument payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Messages.Enqueue(new SentEmail(payload.RootElement.GetProperty("to")[0].GetString()!,
                payload.RootElement.GetProperty("subject").GetString()!, payload.RootElement.GetProperty("text").GetString()!));
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Status == HttpStatusCode.OK
                    ? "{\"id\":\"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794\"}"
                    : $"private-provider-response {Key}", Encoding.UTF8, "application/json"),
            };
        }

        public async Task<SentEmail> WaitForAsync(int count)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            while (Messages.Count < count) { await Task.Delay(20, timeout.Token); }
            return Messages.ElementAt(count - 1);
        }
    }
}
