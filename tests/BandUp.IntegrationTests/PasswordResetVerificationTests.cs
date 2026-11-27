using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.Infrastructure.Security;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class PasswordResetVerificationTests(BandUpTestEnvironment environment)
{
    private const string Code = "012345";
    private const string NewPassword = "NewPassword456!";
    private static readonly IPasswordHasher Hasher = new BcryptPasswordHasher();
    private static readonly string OldHash = Hasher.Hash("OldPassword123!");

    [Fact]
    public async Task AUTH_17_Verification_Only_Authorizes_Reset_And_Does_Not_Change_Password_Sessions_Or_Cookies()
    {
        TestLogSink logs = new();
        await using BandUpApiFactory factory = new(environment, logSink: logs);
        var seed = await SeedAsync(factory);
        using HttpClient client = Client(factory);
        string raw = await RecoveryTestFlow.VerifyAsync(client, seed.User.Email.ToUpperInvariant(), Code);
        using (HttpResponseMessage me = await client.GetAsync("/api/auth/me")) { me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized); }
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + raw);
        using (HttpResponseMessage me = await client.GetAsync("/api/auth/me")) { me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized); }
        await using (BandUpDbContext db = Context())
        {
            (await db.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
            (await db.Users.FindAsync(seed.User.Id))!.LastLoginAt.ShouldBeNull();
            (await db.RefreshTokens.SingleAsync(token => token.UserId == seed.User.Id)).RevokedAt.ShouldBeNull();
            var token = await db.PasswordResetTokens.SingleAsync(token => token.UserId == seed.User.Id);
            token.TokenHash.ShouldNotBe(raw);
            token.ConsumedAt.ShouldBeNull();
            (await db.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!.ConsumedAt.ShouldNotBeNull();
        }
        using HttpResponseMessage replay = await client.PostAsJsonAsync("/api/auth/verify-reset-code", new { email = seed.User.Email, code = Code });
        replay.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using HttpResponseMessage reset = await ResetAsync(client, raw);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        string rendered = string.Join('\n', logs.Events.Select(entry => entry.RenderMessage() + " " + string.Join(' ', entry.Properties)));
        rendered.ShouldNotContain(raw);
        rendered.ShouldNotContain(NewPassword);
        rendered.ShouldNotContain(seed.User.Email);
    }

    [Fact]
    public async Task AUTH_17_Concurrent_Verification_Issues_Exactly_One_Reset_Token()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        using HttpClient client = Client(factory);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/auth/verify-reset-code", new { email = seed.User.Email, code = Code })));
        try
        {
            results.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            results.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(7);
            await using BandUpDbContext db = Context();
            (await db.PasswordResetTokens.CountAsync(token => token.UserId == seed.User.Id)).ShouldBe(1);
            (await db.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
        }
        finally { foreach (var result in results) { result.Dispose(); } }
    }

    [Fact]
    public async Task AUTH_17_Concurrent_Uses_Of_One_Reset_Token_Change_The_Password_Once()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        using HttpClient client = Client(factory);
        string raw = await RecoveryTestFlow.VerifyAsync(client, seed.User.Email, Code);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ResetAsync(client, raw)));
        try
        {
            results.Count(response => response.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
            results.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(5);
            await using BandUpDbContext db = Context();
            (await db.PasswordResetTokens.SingleAsync(token => token.UserId == seed.User.Id)).ConsumedAt.ShouldNotBeNull();
            Hasher.Verify(NewPassword, (await db.Users.FindAsync(seed.User.Id))!.PasswordHash).ShouldBeTrue();
            (await db.RefreshTokens.SingleAsync(token => token.UserId == seed.User.Id)).RevokedAt.ShouldNotBeNull();
        }
        finally { foreach (var result in results) { result.Dispose(); } }
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("consumed")]
    [InlineData("invalidated")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    public async Task AUTH_17_Unusable_Reset_Tokens_Return_One_Generic_Error_Without_Side_Effects(string state)
    {
        await using BandUpApiFactory factory = new(environment);
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset issued = state == "expired" ? now.AddMinutes(-6) : state == "future" ? now.AddMinutes(2) : now;
        var seed = await SeedAsync(factory, createdAt: issued.AddMinutes(-1));
        string raw = factory.Services.GetRequiredService<IPasswordResetTokenProtector>().GenerateToken();
        await using (BandUpDbContext db = Context())
        {
            var challenge = (await db.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!;
            challenge.Consume(issued);
            var token = PasswordResetToken.Issue(challenge, factory.Services.GetRequiredService<IPasswordResetTokenProtector>().HashToken(raw), issued, issued.AddMinutes(5));
            if (state == "consumed") { token.Consume(now); }
            if (state == "invalidated") { token.Invalidate(now); }
            if (state == "inactive") { (await db.Users.FindAsync(seed.User.Id))!.Deactivate(); }
            db.PasswordResetTokens.Add(token);
            await db.SaveChangesAsync();
        }
        using HttpClient client = Client(factory);
        client.DefaultRequestHeaders.Add("Cookie", "bu_rt=existing-cookie");
        using HttpResponseMessage response = await ResetAsync(client, state == "unknown" ? new string('Z', 43) : raw);
        await AssertTokenRejectedAsync(response);
        await using BandUpDbContext check = Context();
        (await check.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
        (await check.RefreshTokens.SingleAsync(token => token.UserId == seed.User.Id)).RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AUTH_17_Save_And_Commit_Failure_Roll_Back_Each_Stage_And_Permit_Retry(bool reset, bool failSave)
    {
        await using BandUpApiFactory normal = new(environment);
        var seed = await SeedAsync(normal);
        using HttpClient healthy = Client(normal);
        string? raw = reset ? await RecoveryTestFlow.VerifyAsync(healthy, seed.User.Email, Code) : null;
        await using BandUpApiFactory failing = new(environment, configureServices: services =>
            services.AddScoped<IUnitOfWork>(provider => new FailingUnitOfWork(provider.GetRequiredService<BandUpDbContext>(), failSave)));
        using HttpClient client = Client(failing);
        using HttpResponseMessage response = reset ? await ResetAsync(client, raw!) :
            await client.PostAsJsonAsync("/api/auth/verify-reset-code", new { email = seed.User.Email, code = Code });
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("resetToken");
        await using (BandUpDbContext db = Context())
        {
            (await db.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
            (await db.RefreshTokens.SingleAsync(token => token.UserId == seed.User.Id)).RevokedAt.ShouldBeNull();
            if (reset) { (await db.PasswordResetTokens.SingleAsync(token => token.UserId == seed.User.Id)).ConsumedAt.ShouldBeNull(); }
            else
            {
                (await db.PasswordResetTokens.AnyAsync(token => token.UserId == seed.User.Id)).ShouldBeFalse();
                (await db.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!.ConsumedAt.ShouldBeNull();
            }
        }
        raw ??= await RecoveryTestFlow.VerifyAsync(healthy, seed.User.Email, Code);
        using HttpResponseMessage retried = await ResetAsync(healthy, raw);
        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_17_Only_An_Admitted_Resend_Invalidates_Existing_Reset_Authorization(bool admitted)
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory, createdAt: TimeProvider.System.GetUtcNow().AddSeconds(admitted ? -120 : -1));
        using HttpClient client = Client(factory);
        string raw = await RecoveryTestFlow.VerifyAsync(client, seed.User.Email, Code);
        using HttpResponseMessage request = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = seed.User.Email });
        request.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using HttpResponseMessage reset = await ResetAsync(client, raw);
        if (admitted) { await AssertTokenRejectedAsync(reset); }
        else { reset.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
    }

    [Fact]
    public async Task AUTH_17_Weak_Password_Validation_Does_Not_Burn_An_Authorized_Token_And_Old_Body_Cannot_Bypass_Verification()
    {
        await using BandUpApiFactory factory = new(environment);
        var seed = await SeedAsync(factory);
        using HttpClient client = Client(factory);
        using HttpResponseMessage oldBody = await client.PostAsJsonAsync("/api/auth/reset-password", new { email = seed.User.Email, code = Code, newPassword = NewPassword });
        oldBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string raw = await RecoveryTestFlow.VerifyAsync(client, seed.User.Email, Code);
        using HttpResponseMessage weak = await client.PostAsJsonAsync("/api/auth/reset-password", new { resetToken = raw, newPassword = "weak" });
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using HttpResponseMessage retried = await ResetAsync(client, raw);
        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static HttpClient Client(BandUpApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string raw) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new { resetToken = raw, newPassword = NewPassword });
    private BandUpDbContext Context() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<Seed> SeedAsync(BandUpApiFactory factory, DateTimeOffset? createdAt = null)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset created = createdAt ?? now.AddMinutes(-2);
        User user = User.Create($"three-step-{Guid.CreateVersion7():N}@example.com", "Learner", OldHash, now.AddDays(-1));
        Guid id = Guid.CreateVersion7();
        var challenge = PasswordResetChallenge.Issue(id, user.Id,
            factory.Services.GetRequiredService<IPasswordResetCodeProtector>().Protect(id, user.Id, Code), created, created.AddMinutes(10), 5);
        await using BandUpDbContext db = Context();
        db.Users.Add(user);
        db.PasswordResetChallenges.Add(challenge);
        db.RefreshTokens.Add(RefreshToken.Issue(user.Id, Guid.CreateVersion7().ToString("N"), now, now.AddDays(7), null));
        await db.SaveChangesAsync();
        return new(user, challenge);
    }

    private static async Task AssertTokenRejectedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("INVALID_PASSWORD_RESET_TOKEN");
    }

    private sealed record Seed(User User, PasswordResetChallenge Challenge);
    private sealed class FailingUnitOfWork(BandUpDbContext db, bool failSave) : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner = new(db);
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => _inner.BeginTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => _inner.RollbackTransactionAsync(cancellationToken);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => failSave
            ? Task.FromException<int>(new InvalidOperationException("save failed")) : _inner.SaveChangesAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) => !failSave
            ? Task.FromException(new InvalidOperationException("commit failed")) : _inner.CommitTransactionAsync(cancellationToken);
    }
}
