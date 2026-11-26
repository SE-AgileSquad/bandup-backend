using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.RequestOtpLogin;
using BandUp.Application.Features.Identity.ResetPassword;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class OtpLoginHttpTests(BandUpTestEnvironment environment)
{
    private const string Code = "012345";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_16_Email_Login_Works_With_Me_Refresh_And_Logout_Without_Granting_Credits(bool secure)
    {
        await using BandUpApiFactory factory = new(environment, overrides: new()
        {
            ["Auth:RequireSecureCookies"] = secure.ToString(),
            ["Auth:AccessCookieName"] = "otp_at", ["Auth:RefreshCookieName"] = "otp_rt",
        });
        using HttpClient client = Client(factory, cookies: true, secure: secure);
        string email = $"otp-flow-{Guid.CreateVersion7():N}@example.com";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = "Password123!", displayName = "OTP Learner", captchaToken = "fake-captcha-token",
        });
        registration.StatusCode.ShouldBe(HttpStatusCode.Created);
        using JsonDocument registered = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        Guid userId = registered.RootElement.GetProperty("id").GetGuid();
        int accounts;
        int entries;
        await using (BandUpDbContext before = Context())
        {
            accounts = await before.CreditAccounts.CountAsync(account => account.UserId == userId);
            entries = await before.CreditLedger.CountAsync(entry => entry.UserId == userId);
        }
        using HttpResponseMessage requested = await RequestAsync(client, email.ToUpperInvariant());
        await AssertAcknowledgedAsync(requested);
        EmailMessage emailMessage = await WaitForEmailAsync(factory, message => message.To == email);
        string code = Regex.Match(emailMessage.TextBody, "[0-9]{6}").Value;
        code.Length.ShouldBe(6);
        using HttpResponseMessage verified = await VerifyAsync(client, email, code);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookies = verified.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value)).ToArray();
        cookies.Length.ShouldBe(2);
        foreach (var cookie in cookies)
        {
            cookie.HttpOnly.ShouldBeTrue();
            cookie.Secure.ShouldBe(secure);
            cookie.SameSite.ToString().ShouldBe("Strict");
            cookie.Path.ToString().ShouldBe("/");
            cookie.Domain.HasValue.ShouldBeFalse();
        }
        using JsonDocument login = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        login.RootElement.EnumerateObject().Select(property => property.Name).Order().ShouldBe(
            new[] { "creditBalance", "displayName", "email", "id", "role" });
        login.RootElement.GetProperty("id").GetGuid().ShouldBe(userId);
        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpResponseMessage refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpResponseMessage logout = await client.PostAsJsonAsync("/api/auth/logout", new { });
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using HttpResponseMessage denied = await client.GetAsync("/api/auth/me");
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using HttpResponseMessage replayed = await VerifyAsync(client, email, code);
        await AssertRejectedAsync(replayed);
        await using BandUpDbContext after = Context();
        (await after.CreditAccounts.CountAsync(account => account.UserId == userId)).ShouldBe(accounts);
        (await after.CreditLedger.CountAsync(entry => entry.UserId == userId)).ShouldBe(entries);
        (await after.OtpLoginChallenges.SingleAsync(challenge => challenge.UserId == userId)).ConsumedAt.ShouldNotBeNull();
        (await after.Users.FindAsync(userId))!.LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AUTH_16_Request_Hides_Account_State_And_Cooldown_Without_Changing_Cookies()
    {
        await using BandUpApiFactory factory = new(environment);
        var active = await SeedAsync(factory, "missing");
        var inactive = await SeedAsync(factory, "inactive");
        using HttpClient client = Client(factory);
        string? body = null;
        foreach (string email in new[] { active.User.Email, active.User.Email.ToUpperInvariant(), inactive.User.Email, $"unknown-{Guid.CreateVersion7():N}@example.com" })
        {
            using HttpResponseMessage response = await RequestAsync(client, email);
            await AssertAcknowledgedAsync(response);
            string json = await response.Content.ReadAsStringAsync();
            body ??= json;
            json.ShouldBe(body);
        }
        await DrainAsync(factory);
        var messages = factory.Services.GetRequiredService<InMemoryEmailSender>().Messages.Where(message => message.Subject != "Barrier").ToArray();
        messages.Length.ShouldBe(1);
        messages[0].To.ShouldBe(active.User.Email);
        await using BandUpDbContext db = Context();
        (await db.OtpLoginChallenges.CountAsync(challenge => challenge.UserId == active.User.Id)).ShouldBe(1);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("invalidated")]
    [InlineData("exhausted")]
    public async Task AUTH_16_Unusable_Codes_Return_The_Same_401_And_Preserve_Existing_Cookies(string state)
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory, state);
        using HttpClient client = Client(factory);
        client.DefaultRequestHeaders.Add("Cookie", "bu_at=stale; bu_rt=stale");
        using HttpResponseMessage response = await VerifyAsync(client,
            state == "unknown" ? $"unknown-{Guid.CreateVersion7():N}@example.com" : seed.User.Email, Code);
        await AssertRejectedAsync(response);
        await using BandUpDbContext db = Context();
        (await db.RefreshTokens.AnyAsync(token => token.UserId == seed.User.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_16_Five_Wrong_Http_Guesses_Commit_And_Block_The_Correct_Code()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        using HttpClient client = Client(factory);
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using HttpResponseMessage wrong = await VerifyAsync(client, seed.User.Email, "999999");
            await AssertRejectedAsync(wrong);
            await using BandUpDbContext check = Context();
            (await check.OtpLoginChallenges.FindAsync(seed.Challenge.Id))!.FailedAttempts.ShouldBe(attempt);
        }
        using HttpResponseMessage correct = await VerifyAsync(client, seed.User.Email, Code);
        await AssertRejectedAsync(correct);
    }

    [Fact]
    public async Task AUTH_16_Concurrent_Verifications_Create_Exactly_One_Session()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        using HttpClient client = Client(factory);
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => VerifyAsync(client, seed.User.Email, Code)));
        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(7);
            foreach (var rejected in responses.Where(response => response.StatusCode == HttpStatusCode.Unauthorized))
            {
                await AssertRejectedAsync(rejected);
            }
            await using BandUpDbContext db = Context();
            (await db.RefreshTokens.CountAsync(token => token.UserId == seed.User.Id)).ShouldBe(1);
        }
        finally { foreach (var response in responses) { response.Dispose(); } }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AUTH_16_Save_Or_Commit_Failure_Rolls_Back_Request_And_Verification(bool request, bool failSave)
    {
        await using BandUpApiFactory factory = new(environment, configureServices: services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new FailingUnitOfWork(provider.GetRequiredService<BandUpDbContext>(), failSave));
        });
        var seed = await SeedAsync(factory, request ? "missing" : "valid");
        using HttpClient client = Client(factory);
        using HttpResponseMessage response = request
            ? await RequestAsync(client, seed.User.Email) : await VerifyAsync(client, seed.User.Email, Code);
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        await DrainAsync(factory);
        factory.Services.GetRequiredService<InMemoryEmailSender>().Messages.Count(message => message.Subject != "Barrier").ShouldBe(0);
        await using BandUpDbContext db = Context();
        (await db.RefreshTokens.AnyAsync(token => token.UserId == seed.User.Id)).ShouldBeFalse();
        (await db.Users.FindAsync(seed.User.Id))!.LastLoginAt.ShouldBeNull();
        if (request)
        {
            (await db.OtpLoginChallenges.AnyAsync(challenge => challenge.UserId == seed.User.Id)).ShouldBeFalse();
        }
        else
        {
            (await db.OtpLoginChallenges.FindAsync(seed.Challenge.Id))!.ConsumedAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task AUTH_16_Password_Reset_Invalidates_Pending_Login_Code_And_Concurrent_Login_Sessions()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        await AddResetCodeAsync(factory, seed.User);
        using HttpClient client = Client(factory);
        Task<HttpResponseMessage> login = VerifyAsync(client, seed.User.Email, Code);
        Task<HttpResponseMessage> reset = client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = seed.User.Email, code = Code, newPassword = "NewPassword456!",
        });
        await Task.WhenAll(login, reset);
        using HttpResponseMessage loginResult = await login;
        using HttpResponseMessage resetResult = await reset;
        resetResult.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized }.ShouldContain(loginResult.StatusCode);
        using HttpResponseMessage reuse = await VerifyAsync(client, seed.User.Email, Code);
        await AssertRejectedAsync(reuse);
        await using BandUpDbContext db = Context();
        var challenge = (await db.OtpLoginChallenges.FindAsync(seed.Challenge.Id))!;
        (challenge.ConsumedAt is not null || challenge.InvalidatedAt is not null).ShouldBeTrue();
        (await db.RefreshTokens.Where(token => token.UserId == seed.User.Id).ToListAsync()).ShouldAllBe(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task AUTH_16_Login_And_Reset_Codes_Are_Independent_And_New_Request_Supersedes_Old_Login_Code()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        await AddResetCodeAsync(factory, seed.User, "654321");
        using HttpClient client = Client(factory);
        using HttpResponseMessage crossPurpose = await VerifyAsync(client, seed.User.Email, "654321");
        await AssertRejectedAsync(crossPurpose);
        using HttpResponseMessage request = await RequestAsync(client, seed.User.Email);
        await AssertAcknowledgedAsync(request);
        await WaitForEmailAsync(factory, message => message.To == seed.User.Email);
        await using BandUpDbContext db = Context();
        (await db.OtpLoginChallenges.FindAsync(seed.Challenge.Id))!.InvalidatedAt.ShouldNotBeNull();
        (await db.OtpLoginChallenges.CountAsync(challenge => challenge.UserId == seed.User.Id && challenge.InvalidatedAt == null && challenge.ConsumedAt == null)).ShouldBe(1);
        (await db.PasswordResetChallenges.SingleAsync(challenge => challenge.UserId == seed.User.Id)).InvalidatedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("verify")]
    public async Task AUTH_16_Json_Validation_And_Csrf_Protect_The_New_Routes(string route)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = Client(factory);
        object malformed = route == "request" ? new { email = "invalid", captchaToken = "" } : new { email = "invalid", code = "12345" };
        using HttpResponseMessage invalid = await client.PostAsJsonAsync("/api/auth/otp/" + route, malformed);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using HttpRequestMessage csrf = new(HttpMethod.Post, "/api/auth/otp/" + route)
        {
            Content = JsonContent.Create(route == "request" ? (object)new { email = "valid@example.com", captchaToken = "fake-captcha-token" } : new { email = "valid@example.com", code = Code }),
        };
        csrf.Headers.Add("Cookie", "bu_rt=stale");
        using HttpClient withoutHeader = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpResponseMessage denied = await withoutHeader.SendAsync(csrf);
        denied.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        denied.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_16_OpenApi_Exposes_Public_Fields_And_Documents_Expected_Responses()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = Client(factory);
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        foreach (string route in new[] { "request", "verify" })
        {
            var operation = document.RootElement.GetProperty("paths").GetProperty("/api/auth/otp/" + route).GetProperty("post");
            var responses = operation.GetProperty("responses");
            foreach (string status in new[] { route == "request" ? "202" : "200", "400", "415", "429", "500", route == "request" ? "422" : "401" })
            {
                responses.TryGetProperty(status, out _).ShouldBeTrue();
            }
            string schemaName = route == "request" ? "RequestOtpLoginRequest" : "VerifyOtpLoginRequest";
            var properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName).GetProperty("properties");
            properties.TryGetProperty("clientIp", out _).ShouldBeFalse();
            properties.TryGetProperty("userAgent", out _).ShouldBeFalse();
            properties.EnumerateObject().Count().ShouldBe(2);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AUTH_16_Quota_Limits_And_Outages_Map_Without_Changing_Cookies(bool verify, bool unavailable)
    {
        await using BandUpApiFactory factory = new(environment, configureServices: services =>
        {
            services.RemoveAll<IOtpLoginRateLimiter>();
            services.AddSingleton<IOtpLoginRateLimiter>(new StubLimiter(unavailable));
        });
        using HttpClient client = Client(factory);
        using HttpResponseMessage response = verify
            ? await VerifyAsync(client, "valid@example.com", Code) : await RequestAsync(client, "valid@example.com");
        response.StatusCode.ShouldBe(unavailable ? HttpStatusCode.InternalServerError : HttpStatusCode.TooManyRequests);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe(unavailable ? "INTERNAL_ERROR" : Error.OtpLoginRateLimitedCode);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("provider-secret-marker");
    }

    [Fact]
    public async Task AUTH_16_Reset_Committed_Before_Verification_Invalidates_The_Pending_Code()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        await AddResetCodeAsync(factory, seed.User);
        using HttpClient client = Client(factory);
        using HttpResponseMessage reset = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = seed.User.Email, code = Code, newPassword = "NewPassword456!",
        });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using HttpResponseMessage verify = await VerifyAsync(client, seed.User.Email, Code);
        await AssertRejectedAsync(verify);
        await using BandUpDbContext db = Context();
        (await db.OtpLoginChallenges.FindAsync(seed.Challenge.Id))!.InvalidatedAt.ShouldNotBeNull();
        (await db.RefreshTokens.AnyAsync(token => token.UserId == seed.User.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_16_Background_Delivery_Failure_Still_Returns_A_Generic_Acknowledgement()
    {
        await using BandUpApiFactory factory = new(environment, configureServices: services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FailingSender>();
        });
        var seed = await SeedAsync(factory, "missing");
        using HttpClient client = Client(factory);
        using HttpResponseMessage request = await RequestAsync(client, seed.User.Email);
        await AssertAcknowledgedAsync(request);
        await factory.Services.GetRequiredService<IEmailSender>().ShouldBeOfType<FailingSender>().Attempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using BandUpDbContext db = Context();
        (await db.OtpLoginChallenges.CountAsync(challenge => challenge.UserId == seed.User.Id)).ShouldBe(1);
    }

    [Theory]
    [InlineData("OtpLogin:CodeProtectionKey", "")]
    [InlineData("OtpLogin:CodeProtectionKey", "invalid-base64-marker")]
    [InlineData("OtpLogin:RateLimitProvider", "unsupported-provider-marker")]
    public async Task AUTH_16_Api_Startup_Fails_With_Sanitized_Otp_Configuration_Errors(string key, string value)
    {
        await using BandUpApiFactory factory = new(environment, overrides: new() { [key] = value });
        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
        exception.Message.ShouldContain(key);
        if (value.Length > 0) { exception.Message.ShouldNotContain(value); }
    }

    private static HttpClient Client(BandUpApiFactory factory, bool cookies = false, bool secure = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = cookies, BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/otp/request", new { email, captchaToken = "fake-captcha-token" });
    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/api/auth/otp/verify", new { email, code });
    private BandUpDbContext Context() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<Seed> SeedAsync(BandUpApiFactory factory, string state = "valid")
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"otp-{Guid.CreateVersion7():N}@example.com", "Learner", "unchanged-password-hash", now.AddDays(-1));
        if (state == "inactive") { user.Deactivate(); }
        Guid id = Guid.CreateVersion7();
        var challenge = OtpLoginChallenge.Issue(id, user.Id,
            factory.Services.GetRequiredService<IOtpLoginCodeProtector>().Protect(id, user.Id, Code),
            now.AddMinutes(-11), now.AddMinutes(state == "expired" ? -1 : 10), 5);
        if (state == "consumed") { challenge.Consume(now); }
        if (state == "invalidated") { challenge.Invalidate(now); }
        if (state == "exhausted") { for (int attempt = 0; attempt < 5; attempt++) { challenge.RecordFailedAttempt(now); } }
        await using BandUpDbContext db = Context();
        db.Users.Add(user);
        if (state != "missing") { db.OtpLoginChallenges.Add(challenge); }
        await db.SaveChangesAsync();
        return new Seed(user, challenge);
    }

    private async Task AddResetCodeAsync(BandUpApiFactory factory, User user, string code = Code)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        Guid id = Guid.CreateVersion7();
        await using BandUpDbContext db = Context();
        db.PasswordResetChallenges.Add(PasswordResetChallenge.Issue(id, user.Id,
            factory.Services.GetRequiredService<IPasswordResetCodeProtector>().Protect(id, user.Id, code), now, now.AddMinutes(10), 5));
        await db.SaveChangesAsync();
    }

    private static async Task AssertAcknowledgedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Count().ShouldBe(1);
        json.RootElement.GetProperty("message").GetString().ShouldBe(new RequestOtpLoginOutcome().Message);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe(Error.InvalidOtpLoginCode);
    }

    private static async Task<EmailMessage> WaitForEmailAsync(BandUpApiFactory factory, Func<EmailMessage, bool> predicate)
    {
        var inbox = factory.Services.GetRequiredService<InMemoryEmailSender>();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            var message = inbox.Messages.FirstOrDefault(predicate);
            if (message is not null) { return message; }
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task DrainAsync(BandUpApiFactory factory)
    {
        factory.Services.GetRequiredService<EmailDeliveryQueue>().TryEnqueue(new EmailMessage("barrier@example.com", "Barrier", "done"), null).ShouldBeTrue();
        await WaitForEmailAsync(factory, message => message.Subject == "Barrier");
    }

    private sealed record Seed(User User, OtpLoginChallenge Challenge);

    private sealed class StubLimiter(bool unavailable) : IOtpLoginRateLimiter
    {
        public Task<OtpLoginRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken) => AcquireAsync();
        public Task<OtpLoginRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken) => AcquireAsync();
        private Task<OtpLoginRateLimitDecision> AcquireAsync() => unavailable
            ? Task.FromException<OtpLoginRateLimitDecision>(new InvalidOperationException("provider-secret-marker"))
            : Task.FromResult(OtpLoginRateLimitDecision.ClientRateLimited);
    }

    private sealed class FailingSender : IEmailSender
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Attempted.TrySetResult();
            return Task.FromException(new InvalidOperationException("provider-secret-marker"));
        }
    }

    private sealed class FailingUnitOfWork(BandUpDbContext context, bool failSave) : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner = new(context);
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => _inner.BeginTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => _inner.RollbackTransactionAsync(cancellationToken);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => failSave
            ? Task.FromException<int>(new InvalidOperationException("save failed")) : _inner.SaveChangesAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) => !failSave
            ? Task.FromException(new InvalidOperationException("commit failed")) : _inner.CommitTransactionAsync(cancellationToken);
    }
}
