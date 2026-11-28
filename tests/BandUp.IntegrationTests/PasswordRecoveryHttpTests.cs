using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Worker;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class PasswordRecoveryHttpTests(BandUpTestEnvironment environment)
{
    private const string OldPassword = "OldPassword123!";
    private const string NewPassword = "NewPassword456!";
    private const string Code = "012345";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AUTH_15_Http_Recovery_Changes_Password_Revokes_Refresh_And_Clears_The_Cookie_Jar(bool secure, bool customNames)
    {
        TestLogSink logs = new();
        await using BandUpApiFactory factory = Factory(secure, customNames, logs: logs);
        using HttpClient browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
        });
        browser.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        string email = $"recovery-http-{Guid.CreateVersion7():N}@example.com";
        using HttpResponseMessage registration = await browser.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = OldPassword, displayName = "Recovery", captchaToken = "fake-captcha-token",
        });
        registration.StatusCode.ShouldBe(HttpStatusCode.Created);
        using HttpResponseMessage login = await LoginAsync(browser, email, OldPassword);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        AuthSettings settings = factory.Services.GetRequiredService<IOptions<AuthSettings>>().Value;
        Dictionary<string, SetCookieHeaderValue> initial = Cookies(login);
        using HttpResponseMessage forgot = await browser.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        await AssertAcknowledgementAsync(forgot);
        EmailMessage codeEmail = await WaitForEmailAsync(factory, message => message.To == email);
        string code = Regex.Match(codeEmail.TextBody, "[0-9]{6}").Value;
        code.Length.ShouldBe(6);
        string resetToken = await RecoveryTestFlow.VerifyAsync(browser, email, code);
        using HttpResponseMessage reset = await browser.PostAsJsonAsync("/api/auth/reset-password", new { resetToken, newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reset.Content.ReadAsStringAsync()).ShouldBeEmpty();
        AssertDeletedCookies(reset, settings, secure);
        using HttpResponseMessage me = await browser.GetAsync("/api/auth/me");
        await AssertProblemAsync(me, HttpStatusCode.Unauthorized, Error.UnauthorizedCode);
        EmailMessage notification = await WaitForEmailAsync(factory, message => message.To == email && message.Subject.Contains("changed"));
        notification.TextBody.ShouldNotContain(NewPassword);
        notification.TextBody.ShouldNotContain(code);

        using HttpClient copied = ManualClient(factory, secure);
        using HttpRequestMessage oldRefresh = new(HttpMethod.Post, "/api/auth/refresh") { Content = JsonContent.Create(new { }) };
        oldRefresh.Headers.Add("Cookie", $"{settings.RefreshCookieName}={initial[settings.RefreshCookieName].Value}");
        using HttpResponseMessage deniedRefresh = await copied.SendAsync(oldRefresh);
        await AssertProblemAsync(deniedRefresh, HttpStatusCode.Unauthorized, Error.InvalidRefreshTokenCode);
        using HttpRequestMessage oldAccess = new(HttpMethod.Get, "/api/auth/me");
        oldAccess.Headers.Add("Cookie", $"{settings.AccessCookieName}={initial[settings.AccessCookieName].Value}");
        using HttpResponseMessage accessUntilExpiry = await copied.SendAsync(oldAccess);
        accessUntilExpiry.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage oldLogin = await LoginAsync(browser, email, OldPassword);
        await AssertProblemAsync(oldLogin, HttpStatusCode.Unauthorized, Error.InvalidCredentialsCode);
        using HttpResponseMessage newLogin = await LoginAsync(browser, email, NewPassword);
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpResponseMessage reused = await browser.PostAsJsonAsync("/api/auth/reset-password", new { resetToken, newPassword = OldPassword });
        await AssertProblemAsync(reused, HttpStatusCode.UnprocessableEntity, Error.InvalidPasswordResetTokenCode);
        AssertNoCookieChange(reused);
        using HttpResponseMessage stillSignedIn = await browser.GetAsync("/api/auth/me");
        stillSignedIn.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await logs.WaitForAsync(entry => entry.RenderMessage().Contains("reset-password"))).ShouldNotBeNull();
        string renderedLogs = string.Join('\n', logs.Events.Select(entry => entry.RenderMessage() + " " + string.Join(' ', entry.Properties)));
        renderedLogs.ShouldNotContain(email);
        renderedLogs.ShouldNotContain(OldPassword);
        renderedLogs.ShouldNotContain(NewPassword);
        renderedLogs.ShouldNotContain(code);
        renderedLogs.ShouldNotContain(initial[settings.AccessCookieName].Value.ToString());
        renderedLogs.ShouldNotContain(initial[settings.RefreshCookieName].Value.ToString());
    }

    [Fact]
    public async Task AUTH_15_Anonymous_Http_Recovery_Delivers_Through_A_Real_Local_Mailpit_Inbox()
    {
        await using IContainer inbox = new ContainerBuilder("axllent/mailpit:v1.27.0")
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025)))
            .Build();
        await inbox.StartAsync();
        string baseUrl = $"http://{inbox.Hostname}:{inbox.GetMappedPublicPort(8025)}/";
        await using BandUpApiFactory factory = new(environment, overrides: new()
        {
            ["Email:Provider"] = "Mailpit", ["Email:MailpitBaseUrl"] = baseUrl,
            ["PasswordReset:RateLimitProvider"] = "Redis",
        });
        using HttpClient client = ManualClient(factory);
        using HttpClient inboxClient = new() { BaseAddress = new Uri(baseUrl) };
        string email = $"mailpit-http-{Guid.CreateVersion7():N}@example.com";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = OldPassword, displayName = "Local inbox", captchaToken = "fake-captcha-token",
        });
        registration.StatusCode.ShouldBe(HttpStatusCode.Created);
        using HttpResponseMessage forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        await AssertAcknowledgementAsync(forgot);
        string code = Regex.Match(await WaitForMailpitAsync(inboxClient, "reset"), "[0-9]{6}").Value;
        code.Length.ShouldBe(6);
        string resetToken = await RecoveryTestFlow.VerifyAsync(client, email, code);
        using HttpResponseMessage reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { resetToken, newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await WaitForMailpitAsync(inboxClient, "changed")).ShouldNotContain(NewPassword);
        using HttpResponseMessage login = await LoginAsync(client, email, NewPassword);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AUTH_15_Forgot_Unknown_Inactive_And_Cooldown_Responses_Are_Identical_And_Preserve_Cookies()
    {
        await using BandUpApiFactory factory = Factory();
        Seed active = await SeedAsync(factory);
        Seed inactive = await SeedAsync(factory, "inactive");
        using HttpClient client = ManualClient(factory);
        string? acknowledgement = null;
        foreach (string email in new[] { active.User.Email, active.User.Email.ToUpperInvariant(), inactive.User.Email, $"unknown-{Guid.CreateVersion7():N}@example.com" })
        {
            using HttpRequestMessage request = Post("forgot-password", new { email }, cookie: "bu_rt=stale-refresh");
            using HttpResponseMessage response = await client.SendAsync(request);
            await AssertAcknowledgementAsync(response);
            string body = await response.Content.ReadAsStringAsync();
            acknowledgement ??= body;
            body.ShouldBe(acknowledgement);
        }
        await DrainEmailsAsync(factory);
        var messages = factory.Services.GetRequiredService<InMemoryEmailSender>().Messages;
        messages.Count(message => message.Subject != "Barrier").ShouldBe(1);
        messages.Single(message => message.Subject != "Barrier").To.ShouldBe(active.User.Email);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("invalidated")]
    [InlineData("exhausted")]
    [InlineData("wrong")]
    public async Task AUTH_15_Reset_Rejections_Are_Generic_And_Do_Not_Clear_Cookies(string state)
    {
        await using BandUpApiFactory factory = Factory();
        Seed seed = await SeedAsync(factory, state);
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post("verify-reset-code", new
        {
            email = state == "unknown" ? $"unknown-{Guid.CreateVersion7():N}@example.com" : seed.User.Email,
            code = state == "wrong" ? "999999" : Code, newPassword = NewPassword,
        }, cookie: "bu_at=expired-or-malformed; bu_rt=stale-refresh");
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, Error.InvalidPasswordResetCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(Error.InvalidPasswordReset().Message);
        problem.RootElement.EnumerateObject().Select(property => property.Name).ShouldNotContain("attempts");
        AssertNoCookieChange(response);
        await using BandUpDbContext verification = CreateContext();
        (await verification.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe("unchanged-hash");
    }

    [Fact]
    public async Task AUTH_15_Http_422_Commits_Incorrect_Attempts_And_Exhausts_The_Code()
    {
        await using BandUpApiFactory factory = Factory();
        Seed seed = await SeedAsync(factory);
        using HttpClient client = ManualClient(factory);
        for (int count = 1; count <= 5; count++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/verify-reset-code", new
            {
                email = seed.User.Email, code = "999999", newPassword = NewPassword,
            });
            await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, Error.InvalidPasswordResetCode);
            await using BandUpDbContext verification = CreateContext();
            (await verification.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!.FailedAttempts.ShouldBe(count);
        }
        using HttpResponseMessage correctAfterExhaustion = await client.PostAsJsonAsync("/api/auth/verify-reset-code", new
        {
            email = seed.User.Email, code = Code, newPassword = NewPassword,
        });
        await AssertProblemAsync(correctAfterExhaustion, HttpStatusCode.UnprocessableEntity, Error.InvalidPasswordResetCode);
    }

    [Theory]
    [InlineData("verify-reset-code", "bu_at=stale")]
    [InlineData("verify-reset-code", "bu_rt=stale")]
    [InlineData("forgot-password", "bu_at=stale")]
    [InlineData("forgot-password", "bu_rt=stale")]
    [InlineData("reset-password", "bu_at=stale")]
    [InlineData("reset-password", "bu_rt=stale")]
    public async Task AUTH_15_Recovery_With_Either_Session_Cookie_Requires_The_Csrf_Header(string route, string cookie)
    {
        await using BandUpApiFactory factory = Factory();
        using HttpClient client = ManualClient(factory);
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        using HttpRequestMessage request = Post(route, ValidBody(), cookie, csrf: false);
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, Error.ValidationCode);
        AssertNoCookieChange(response);
    }

    [Theory]
    [InlineData("verify-reset-code", "{}", "application/json", 400)]
    [InlineData("verify-reset-code", "{\"email\":\"valid@example.com\",\"code\":\"012345 \"}", "application/json", 400)]
    [InlineData("verify-reset-code", "{}", "text/plain", 415)]
    [InlineData("forgot-password", "{}", "application/json", 400)]
    [InlineData("forgot-password", "{\"email\":null}", "application/json", 400)]
    [InlineData("forgot-password", "{\"email\":\"invalid\"}", "application/json", 400)]
    [InlineData("forgot-password", "null", "application/json", 400)]
    [InlineData("forgot-password", "{", "application/json", 400)]
    [InlineData("forgot-password", "{}", "text/plain", 415)]
    [InlineData("reset-password", "{}", "application/json", 400)]
    [InlineData("reset-password", "{\"email\":\"valid@example.com\",\"code\":123456,\"newPassword\":\"ValidPassword123!\"}", "application/json", 400)]
    [InlineData("reset-password", "{\"email\":\"valid@example.com\",\"code\":\"012345 \",\"newPassword\":\"ValidPassword123!\"}", "application/json", 400)]
    [InlineData("reset-password", "{\"email\":\"valid@example.com\",\"code\":\"۰۱۲۳۴۵\",\"newPassword\":\"ValidPassword123!\"}", "application/json", 400)]
    [InlineData("reset-password", "{\"email\":\"valid@example.com\",\"code\":\"012345\",\"newPassword\":null}", "application/json", 400)]
    [InlineData("reset-password", "{\"email\":\"valid@example.com\",\"code\":\"012345\",\"newPassword\":\"weak\"}", "application/json", 400)]
    [InlineData("reset-password", "null", "application/json", 400)]
    [InlineData("reset-password", "{", "application/json", 400)]
    [InlineData("reset-password", "{}", "text/plain", 415)]
    public async Task AUTH_15_Recovery_Rejects_Invalid_Json_And_Credentials_Without_Cookie_Changes(string route, string body, string contentType, int status)
    {
        await using BandUpApiFactory factory = Factory();
        using HttpClient client = ManualClient(factory);
        using HttpResponseMessage response = await client.PostAsync("/api/auth/" + route, new StringContent(body, Encoding.UTF8, contentType));
        if (status == 415)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
            (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        }
        else
        {
            await AssertProblemAsync(response, (HttpStatusCode)status, Error.ValidationCode);
        }
        AssertNoCookieChange(response);
    }

    [Theory]
    [InlineData("verify-reset-code", PasswordResetRateLimitDecision.ClientRateLimited, 429)]
    [InlineData("verify-reset-code", PasswordResetRateLimitDecision.AccountSuppressed, 422)]
    [InlineData("forgot-password", PasswordResetRateLimitDecision.ClientRateLimited, 429)]
    [InlineData("reset-password", PasswordResetRateLimitDecision.ClientRateLimited, 429)]
    [InlineData("forgot-password", PasswordResetRateLimitDecision.AccountSuppressed, 202)]
    [InlineData("reset-password", PasswordResetRateLimitDecision.AccountSuppressed, 422)]
    public async Task AUTH_15_Http_Distinguishes_Client_Throttling_From_Hidden_Account_Suppression(string route, PasswordResetRateLimitDecision decision, int status)
    {
        StubLimiter limiter = new() { Decision = decision };
        await using BandUpApiFactory factory = Factory(configure: services => services.AddSingleton<IPasswordResetRateLimiter>(limiter));
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(route, ValidBody(), "bu_rt=stale");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe((HttpStatusCode)status);
        if (status == 202)
        {
            await AssertAcknowledgementAsync(response);
        }
        else
        {
            await AssertProblemAsync(response, (HttpStatusCode)status, status == 429 ? Error.PasswordResetRateLimitedCode : route == "reset-password" ? Error.InvalidPasswordResetTokenCode : Error.InvalidPasswordResetCode);
        }
        response.Headers.Contains("Retry-After").ShouldBeFalse();
        AssertNoCookieChange(response);
    }

    [Theory]
    [InlineData("verify-reset-code")]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    public async Task AUTH_15_Recovery_Uses_The_Connection_Ip_And_Ignores_Untrusted_Forwarding_And_Body_Ip(string route)
    {
        StubLimiter limiter = new();
        await using BandUpApiFactory factory = Factory(configure: services =>
        {
            services.AddSingleton<IPasswordResetRateLimiter>(limiter);
            services.AddSingleton<IStartupFilter>(new ConnectionIpStartupFilter());
        });
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(route, ValidBody());
        request.Headers.Add("X-Forwarded-For", "198.51.100.1");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(route == "forgot-password" ? HttpStatusCode.Accepted : HttpStatusCode.UnprocessableEntity);
        limiter.LastIp.ShouldBe("203.0.113.19");
    }

    [Theory]
    [InlineData("verify-reset-code", true)]
    [InlineData("verify-reset-code", false)]
    [InlineData("forgot-password", true)]
    [InlineData("forgot-password", false)]
    [InlineData("reset-password", true)]
    [InlineData("reset-password", false)]
    public async Task AUTH_15_Http_Save_And_Commit_Failures_Return_Generic_500_Without_Cookie_Changes_Or_Email(string route, bool failSave)
    {
        await using BandUpApiFactory factory = Factory(configure: services => services.AddScoped<IUnitOfWork>(provider =>
            new FailingUnitOfWork(provider.GetRequiredService<BandUpDbContext>(), failSave)));
        Seed seed = await SeedAsync(factory);
        using HttpClient client = ManualClient(factory);
        string? resetToken = route == "reset-password" ? await SeedTokenAsync(factory, seed) : null;
        using HttpRequestMessage request = Post(route, new { email = seed.User.Email, code = Code, resetToken, newPassword = NewPassword }, "bu_rt=existing-session");
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, Error.InternalErrorCode);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("provider-secret-marker");
        AssertNoCookieChange(response);
        await using BandUpDbContext verification = CreateContext();
        (await verification.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe("unchanged-hash");
        var savedChallenge = (await verification.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!;
        if (route == "reset-password") { savedChallenge.ConsumedAt.ShouldNotBeNull(); }
        else { savedChallenge.ConsumedAt.ShouldBeNull(); }
        await DrainEmailsAsync(factory);
        factory.Services.GetRequiredService<InMemoryEmailSender>().Messages.Single().Subject.ShouldBe("Barrier");
    }

    [Theory]
    [InlineData("verify-reset-code")]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    public async Task AUTH_15_Quota_Outage_Returns_Generic_500_And_Preserves_Cookies(string route)
    {
        await using BandUpApiFactory factory = Factory(configure: services => services.AddSingleton<IPasswordResetRateLimiter>(new StubLimiter { Fail = true }));
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(route, ValidBody(), "bu_rt=existing-session");
        using HttpResponseMessage response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, Error.InternalErrorCode);
        AssertNoCookieChange(response);
    }

    [Theory]
    [InlineData("PasswordReset:CodeProtectionKey", "")]
    [InlineData("PasswordReset:CodeProtectionKey", "invalid-base64-marker")]
    [InlineData("PasswordReset:RateLimitProvider", "invalid-provider-marker")]
    public async Task AUTH_15_Api_Startup_Rejects_Missing_Or_Invalid_Recovery_Configuration(string setting, string value)
    {
        await using BandUpApiFactory factory = new(environment, overrides: new() { [setting] = value });
        OptionsValidationException exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
        exception.Message.ShouldContain(setting);
        if (value.Length > 0)
        {
            exception.Message.ShouldNotContain(value);
        }
    }

    [Fact]
    public async Task AUTH_15_Worker_Starts_Without_The_Api_Recovery_Protection_Key()
    {
        string database = await environment.CreateIsolatedMigratedDatabaseAsync(CancellationToken.None);
        Dictionary<string, string?> settings = environment.SettingsFor(database);
        settings["PasswordReset:CodeProtectionKey"] = string.Empty;
        settings["OtpLogin:CodeProtectionKey"] = string.Empty;
        using IHost host = WorkerHost.Build([], configure: builder => builder.Configuration.AddInMemoryCollection(settings));
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task AUTH_15_OpenApi_Describes_Recovery_Json_And_All_Response_Statuses()
    {
        await using BandUpApiFactory factory = Factory();
        using HttpClient client = ManualClient(factory);
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        foreach ((string route, string success, string[] errors) in new[]
        {
            ("forgot-password", "202", new[] { "400", "415", "429", "500" }),
            ("verify-reset-code", "200", new[] { "400", "415", "422", "429", "500" }),
            ("reset-password", "204", new[] { "400", "415", "422", "429", "500" }),
        })
        {
            JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/api/auth/" + route).GetProperty("post");
            operation.GetProperty("description").GetString()!.ShouldContain("X-Requested-With: bandup");
            operation.GetProperty("requestBody").GetProperty("required").GetBoolean().ShouldBeTrue();
            JsonElement schema = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
            string reference = schema.GetProperty("$ref").GetString()!.Split('/').Last();
            JsonElement properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference).GetProperty("properties");
            if (route != "reset-password") { properties.GetProperty("email").GetProperty("type").GetString().ShouldBe("string"); }
            if (route == "verify-reset-code") { properties.GetProperty("code").GetProperty("type").GetString().ShouldBe("string"); }
            if (route == "reset-password")
            {
                properties.GetProperty("resetToken").GetProperty("type").GetString().ShouldBe("string");
                properties.GetProperty("newPassword").GetProperty("type").GetString().ShouldBe("string");
            }
            JsonElement responses = operation.GetProperty("responses");
            if (success == "204")
            {
                responses.GetProperty(success).TryGetProperty("content", out _).ShouldBeFalse();
            }
            else
            {
                responses.GetProperty(success).GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
            }
            foreach (string error in errors)
            {
                responses.TryGetProperty(error, out _).ShouldBeTrue();
                if (error != "415")
                {
                    responses.GetProperty(error).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
                }
            }
        }
    }

    private BandUpApiFactory Factory(bool secure = false, bool customNames = false, Action<IServiceCollection>? configure = null, TestLogSink? logs = null) =>
        new(environment, overrides: new()
        {
            ["Auth:RequireSecureCookies"] = secure.ToString(),
            ["Auth:AccessCookieName"] = customNames ? "recovery_at" : "bu_at",
            ["Auth:RefreshCookieName"] = customNames ? "recovery_rt" : "bu_rt",
        }, configureServices: configure, logSink: logs);

    private static HttpClient ManualClient(BandUpApiFactory factory, bool secure = false)
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static object ValidBody() => new { email = "valid@example.com", code = Code, resetToken = new string('A', 43), newPassword = NewPassword, clientIp = "198.51.100.2" };

    private static HttpRequestMessage Post(string route, object body, string? cookie = null, bool csrf = true)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/" + route) { Content = JsonContent.Create(body) };
        if (cookie is not null) { request.Headers.Add("Cookie", cookie); }
        if (csrf) { request.Headers.Add("X-Requested-With", "bandup"); }
        return request;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password, captchaToken = "fake-captcha-token" });

    private async Task<Seed> SeedAsync(BandUpApiFactory factory, string state = "valid")
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"recovery-seed-{Guid.CreateVersion7():N}@example.com", "Recovery", "unchanged-hash", now.AddDays(-1));
        if (state == "inactive") { user.Deactivate(); }
        Guid id = Guid.CreateVersion7();
        PasswordResetChallenge challenge = PasswordResetChallenge.Issue(id, user.Id,
            factory.Services.GetRequiredService<IPasswordResetCodeProtector>().Protect(id, user.Id, Code),
            now.AddMinutes(-11), now.AddMinutes(state == "expired" ? -1 : 10), 5);
        if (state == "consumed") { challenge.Consume(now); }
        if (state == "invalidated") { challenge.Invalidate(now); }
        if (state == "exhausted")
        {
            for (int count = 0; count < 5; count++) { challenge.RecordFailedAttempt(now); }
        }
        await using BandUpDbContext context = CreateContext();
        context.Users.Add(user);
        if (state != "missing") { context.PasswordResetChallenges.Add(challenge); }
        await context.SaveChangesAsync();
        return new Seed(user, challenge);
    }

    private async Task<string> SeedTokenAsync(BandUpApiFactory factory, Seed seed)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        string raw = factory.Services.GetRequiredService<IPasswordResetTokenProtector>().GenerateToken();
        await using BandUpDbContext db = CreateContext();
        var challenge = (await db.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!;
        challenge.Consume(now);
        db.PasswordResetTokens.Add(PasswordResetToken.Issue(challenge,
            factory.Services.GetRequiredService<IPasswordResetTokenProtector>().HashToken(raw), now, now.AddMinutes(5)));
        await db.SaveChangesAsync();
        return raw;
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
    private sealed record Seed(User User, PasswordResetChallenge Challenge);

    private static async Task AssertAcknowledgementAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldBeNull();
        response.Headers.Contains("Retry-After").ShouldBeFalse();
        AssertNoCookieChange(response);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Count().ShouldBe(1);
        json.RootElement.GetProperty("message").GetString().ShouldBe(new ForgotPasswordOutcome().Message);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        json.RootElement.GetProperty("traceId").GetString().ShouldBe(response.Headers.GetValues("X-Request-Id").Single());
    }

    private static void AssertNoCookieChange(HttpResponseMessage response) => response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    private static Dictionary<string, SetCookieHeaderValue> Cookies(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Select(value => SetCookieHeaderValue.Parse(value)).ToDictionary(cookie => cookie.Name.ToString(), StringComparer.Ordinal);

    private static void AssertDeletedCookies(HttpResponseMessage response, AuthSettings settings, bool secure)
    {
        Dictionary<string, SetCookieHeaderValue> cookies = Cookies(response);
        cookies.Count.ShouldBe(2);
        foreach (string name in new[] { settings.AccessCookieName, settings.RefreshCookieName })
        {
            SetCookieHeaderValue cookie = cookies[name];
            cookie.Value.ToString().ShouldBeEmpty();
            cookie.Expires!.Value.ShouldBeLessThanOrEqualTo(DateTimeOffset.UnixEpoch);
            cookie.Path.ToString().ShouldBe("/");
            cookie.Domain.HasValue.ShouldBeFalse();
            cookie.HttpOnly.ShouldBeTrue();
            cookie.Secure.ShouldBe(secure);
            cookie.SameSite.ToString().ShouldBe("Strict");
        }
    }

    private static async Task<EmailMessage> WaitForEmailAsync(BandUpApiFactory factory, Func<EmailMessage, bool> predicate)
    {
        InMemoryEmailSender inbox = factory.Services.GetRequiredService<InMemoryEmailSender>();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            EmailMessage? message = inbox.Messages.FirstOrDefault(predicate);
            if (message is not null) { return message; }
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task DrainEmailsAsync(BandUpApiFactory factory)
    {
        factory.Services.GetRequiredService<EmailDeliveryQueue>().TryEnqueue(new EmailMessage("barrier@example.com", "Barrier", "done"), null).ShouldBeTrue();
        await WaitForEmailAsync(factory, message => message.Subject == "Barrier");
    }

    private static async Task<string> WaitForMailpitAsync(HttpClient inbox, string subject)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            using HttpResponseMessage response = await inbox.GetAsync("api/v1/message/latest", timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                using JsonDocument message = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (message.RootElement.GetProperty("Subject").GetString()!.Contains(subject, StringComparison.OrdinalIgnoreCase))
                {
                    return message.RootElement.GetProperty("Text").GetString()!;
                }
            }
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class ConnectionIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.19");
                return continuation(context);
            });
            next(app);
        };
    }

    private sealed class StubLimiter : IPasswordResetRateLimiter
    {
        public PasswordResetRateLimitDecision Decision { get; init; }
        public bool Fail { get; init; }
        public string? LastIp { get; private set; }
        public Task<PasswordResetRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken) => AcquireAsync(clientIp);
        public Task<PasswordResetRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken) => AcquireAsync(clientIp);
        public Task<PasswordResetRateLimitDecision> AcquireResetAsync(string tokenHash, string? clientIp, CancellationToken cancellationToken) => AcquireAsync(clientIp);
        private Task<PasswordResetRateLimitDecision> AcquireAsync(string? ip)
        {
            LastIp = ip;
            return Fail ? Task.FromException<PasswordResetRateLimitDecision>(new InvalidOperationException("provider-secret-marker")) : Task.FromResult(Decision);
        }
    }

    private sealed class FailingUnitOfWork(BandUpDbContext context, bool failSave) : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner = new(context);
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => _inner.BeginTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => _inner.RollbackTransactionAsync(cancellationToken);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => failSave
            ? Task.FromException<int>(new InvalidOperationException("provider-secret-marker")) : _inner.SaveChangesAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) => !failSave
            ? Task.FromException(new InvalidOperationException("provider-secret-marker")) : _inner.CommitTransactionAsync(cancellationToken);
    }
}
