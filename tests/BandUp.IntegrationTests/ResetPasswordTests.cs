using System.Collections.Concurrent;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Login;
using BandUp.Application.Features.Identity.Refresh;
using BandUp.Application.Features.Identity.ResetPassword;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Persistence;
using BandUp.Infrastructure.Security;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class ResetPasswordTests(BandUpTestEnvironment environment)
{
    private const string OldPassword = "OldPassword123!";
    private const string NewPassword = "NewPassword456!";
    private const string Code = "012345";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray());
    private static readonly IPasswordHasher Hasher = new BcryptPasswordHasher();
    private static readonly string OldHash = Hasher.Hash(OldPassword);

    [Fact]
    public async Task AUTH_15_Valid_Reset_Commits_Password_Consumption_And_Owned_Refresh_Revocation_Before_Notification()
    {
        Seed seed = await SeedAsync();
        ObservingSender sender = new();
        sender.BeforeSend = async message =>
        {
            await using BandUpDbContext verification = CreateContext();
            User user = (await verification.Users.FindAsync(seed.User.Id))!;
            Hasher.Verify(NewPassword, user.PasswordHash).ShouldBeTrue();
            (await verification.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!.ConsumedAt.ShouldNotBeNull();
            (await verification.RefreshTokens.Where(token => token.UserId == user.Id).ToListAsync())
                .ShouldAllBe(token => token.RevokedAt != null);
            message.TextBody.ShouldNotContain(NewPassword);
            message.TextBody.ShouldNotContain(Code);
        };
        await using BandUpApiFactory factory = Factory(sender, rateProvider: "Redis");
        (await ResetAsync(factory, seed.User.Email.ToUpperInvariant())).Value.ShouldBeOfType<ResetPasswordOutcome.Completed>();
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using BandUpDbContext context = CreateContext();
        var owned = await context.RefreshTokens.Where(token => token.UserId == seed.User.Id).ToListAsync();
        owned.Count.ShouldBe(3);
        owned.ShouldAllBe(token => token.RevokedAt != null);
        (await context.RefreshTokens.Where(token => token.UserId == seed.ForeignUserId).ToListAsync())
            .ShouldAllBe(token => token.RevokedAt == null);
        (await context.PasswordResetChallenges.AnyAsync(challenge => challenge.UserId == seed.User.Id &&
            challenge.ConsumedAt == null && challenge.InvalidatedAt == null)).ShouldBeFalse();
        (await LoginAsync(factory, seed.User.Email, OldPassword)).Error!.Code.ShouldBe(Error.InvalidCredentialsCode);
        (await LoginAsync(factory, seed.User.Email, NewPassword)).IsSuccess.ShouldBeTrue();
        (await ResetAsync(factory, seed.User.Email)).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        sender.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AUTH_15_Incorrect_Code_Counters_Commit_Across_Requests_And_The_Fifth_Guess_Exhausts_The_Code()
    {
        Seed seed = await SeedAsync();
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        for (int count = 1; count <= 5; count++)
        {
            (await ResetAsync(factory, seed.User.Email, code: "999999")).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
            await using BandUpDbContext check = CreateContext();
            (await check.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!.FailedAttempts.ShouldBe(count);
        }
        (await ResetAsync(factory, seed.User.Email)).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        await AssertOriginalStateAsync(seed, failedAttempts: 5);
        await AssertNoNotificationAsync(factory, sender);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("invalidated")]
    public async Task AUTH_15_Ineligible_And_Closed_Recovery_States_Reject_Without_Changing_Password_Or_Sessions(string state)
    {
        Seed seed = await SeedAsync(active: state != "inactive", withChallenge: state != "missing", expired: state == "expired");
        if (state is "consumed" or "invalidated")
        {
            await using BandUpDbContext prepare = CreateContext();
            PasswordResetChallenge challenge = (await prepare.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!;
            if (state == "consumed")
            {
                challenge.Consume(TimeProvider.System.GetUtcNow());
            }
            else
            {
                challenge.Invalidate(TimeProvider.System.GetUtcNow());
            }
            await prepare.SaveChangesAsync();
        }
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        string email = state == "unknown" ? $"unknown-{Guid.CreateVersion7():N}@example.com" : seed.User.Email;
        (await ResetAsync(factory, email)).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        await using BandUpDbContext verification = CreateContext();
        (await verification.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
        (await verification.RefreshTokens.CountAsync(token => token.UserId == seed.User.Id && token.RevokedAt == null)).ShouldBe(2);
        await AssertNoNotificationAsync(factory, sender);
    }

    [Fact]
    public async Task AUTH_15_A_Superseded_Code_Cannot_Reset_The_Password()
    {
        Seed seed = await SeedAsync();
        await using (BandUpDbContext prepare = CreateContext())
        {
            await using var transaction = await prepare.Database.BeginTransactionAsync();
            Guid id = Guid.CreateVersion7();
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            await new EfPasswordResetChallengeStore(prepare).ReplaceAsync(PasswordResetChallenge.Issue(
                id, seed.User.Id, Protector().Protect(id, seed.User.Id, "654321"), now, now.AddMinutes(10), 5), CancellationToken.None);
            await prepare.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        (await ResetAsync(factory, seed.User.Email)).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        await using BandUpDbContext check = CreateContext();
        (await check.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
        (await check.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!.ConsumedAt.ShouldBeNull();
        await AssertNoNotificationAsync(factory, sender);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AUTH_15_Reset_Save_And_Commit_Failure_Roll_Back_All_State_And_Discard_Notification(bool failSave)
    {
        Seed seed = await SeedAsync();
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender, services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new FailingUnitOfWork(provider.GetRequiredService<BandUpDbContext>(), failSave));
        });
        await Should.ThrowAsync<InvalidOperationException>(() => ResetAsync(factory, seed.User.Email));
        await AssertOriginalStateAsync(seed, failedAttempts: 0);
        await AssertNoNotificationAsync(factory, sender);
    }

    [Fact]
    public async Task AUTH_15_Concurrent_Uses_Of_One_Code_Allow_Only_One_Password_Change_And_Notification()
    {
        Seed seed = await SeedAsync();
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        const string secondPassword = "AnotherPassword789!";
        Result<ResetPasswordOutcome>[] outcomes = await Task.WhenAll(
            ResetAsync(factory, seed.User.Email), ResetAsync(factory, seed.User.Email, password: secondPassword));
        outcomes.Count(result => result.Value is ResetPasswordOutcome.Completed).ShouldBe(1);
        outcomes.Count(result => result.Value is ResetPasswordOutcome.Rejected).ShouldBe(1);
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        sender.Messages.Count.ShouldBe(1);
        await using BandUpDbContext check = CreateContext();
        string hash = (await check.Users.FindAsync(seed.User.Id))!.PasswordHash;
        Hasher.Verify(outcomes[0].Value is ResetPasswordOutcome.Completed ? NewPassword : secondPassword, hash).ShouldBeTrue();
        Hasher.Verify(OldPassword, hash).ShouldBeFalse();
        (await check.RefreshTokens.Where(token => token.UserId == seed.User.Id).ToListAsync()).ShouldAllBe(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task AUTH_15_Reset_And_Concurrent_Refresh_Leave_No_Pre_Reset_Refresh_Session_Usable()
    {
        Seed seed = await SeedAsync();
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        Task<Result<ResetPasswordOutcome>> reset = ResetAsync(factory, seed.User.Email);
        Task<Result<RefreshOutcome>> refresh = RefreshAsync(factory, seed.RawRefreshToken);
        await Task.WhenAll(reset, refresh);
        (await reset).Value.ShouldBeOfType<ResetPasswordOutcome.Completed>();
        (await refresh).IsSuccess.ShouldBeTrue();
        await using BandUpDbContext check = CreateContext();
        (await check.RefreshTokens.Where(token => token.UserId == seed.User.Id).ToListAsync()).ShouldAllBe(token => token.RevokedAt != null);
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private BandUpApiFactory Factory(ObservingSender sender, Action<IServiceCollection>? configure = null, string rateProvider = "InMemory") =>
        new(environment, overrides: new Dictionary<string, string?>
        {
            ["PasswordReset:CodeProtectionKey"] = Key,
            ["PasswordReset:RateLimitProvider"] = rateProvider,
            ["Email:Provider"] = "InMemory",
        }, configureServices: services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            configure?.Invoke(services);
        });

    private static async Task<Result<ResetPasswordOutcome>> ResetAsync(
        BandUpApiFactory factory, string email, string code = Code, string password = NewPassword)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync<ResetPasswordCommand, ResetPasswordOutcome>(
            new ResetPasswordCommand(email, code, password, "203.0.113.7"), CancellationToken.None);
    }

    private static async Task<Result<LoginResult>> LoginAsync(BandUpApiFactory factory, string email, string password)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync<LoginCommand, LoginResult>(
            new LoginCommand(email, password, "fake-captcha-token"), CancellationToken.None);
    }

    private static async Task<Result<RefreshOutcome>> RefreshAsync(BandUpApiFactory factory, string rawToken)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync<RefreshCommand, RefreshOutcome>(
            new RefreshCommand(rawToken), CancellationToken.None);
    }

    private async Task AssertOriginalStateAsync(Seed seed, int failedAttempts)
    {
        await using BandUpDbContext check = CreateContext();
        (await check.Users.FindAsync(seed.User.Id))!.PasswordHash.ShouldBe(OldHash);
        PasswordResetChallenge challenge = (await check.PasswordResetChallenges.FindAsync(seed.Challenge.Id))!;
        challenge.ConsumedAt.ShouldBeNull();
        challenge.InvalidatedAt.ShouldBeNull();
        challenge.FailedAttempts.ShouldBe(failedAttempts);
        (await check.RefreshTokens.CountAsync(token => token.UserId == seed.User.Id && token.RevokedAt == null)).ShouldBe(2);
    }

    private static async Task AssertNoNotificationAsync(BandUpApiFactory factory, ObservingSender sender)
    {
        factory.Services.GetRequiredService<EmailDeliveryQueue>().TryEnqueue(new EmailMessage("barrier@example.com", "Barrier", "done"), null);
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        sender.Messages.Single().Subject.ShouldBe("Barrier");
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<Seed> SeedAsync(bool active = true, bool withChallenge = true, bool expired = false)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"reset-{Guid.CreateVersion7():N}@example.com", "Reset", OldHash, now.AddDays(-1));
        User foreign = User.Create($"foreign-{Guid.CreateVersion7():N}@example.com", "Foreign", OldHash, now.AddDays(-1));
        if (!active)
        {
            user.Deactivate();
        }
        Guid challengeId = Guid.CreateVersion7();
        PasswordResetChallenge challenge = PasswordResetChallenge.Issue(challengeId, user.Id,
            Protector().Protect(challengeId, user.Id, Code), now.AddMinutes(expired ? -11 : -2), now.AddMinutes(expired ? -1 : 8), 5);
        var tokens = new JwtTokenService(Options.Create(new AuthSettings { SigningKey = "test-signing-key-not-used-for-issued-jwts" }));
        string raw = tokens.GenerateRefreshToken();
        RefreshToken revoked = RefreshToken.Issue(user.Id, Guid.CreateVersion7().ToString("N"), now.AddDays(-1), now.AddDays(7), null);
        revoked.Revoke(now.AddMinutes(-1));
        await using BandUpDbContext context = CreateContext();
        context.Users.AddRange(user, foreign);
        if (withChallenge)
        {
            context.PasswordResetChallenges.Add(challenge);
        }
        context.RefreshTokens.AddRange(
            RefreshToken.Issue(user.Id, tokens.HashRefreshToken(raw), now, now.AddDays(7), null),
            RefreshToken.Issue(user.Id, Guid.CreateVersion7().ToString("N"), now.AddDays(-8), now.AddDays(-1), null), revoked,
            RefreshToken.Issue(foreign.Id, Guid.CreateVersion7().ToString("N"), now, now.AddDays(7), null));
        await context.SaveChangesAsync();
        return new Seed(user, challenge, raw, foreign.Id);
    }

    private static PasswordResetCodeProtector Protector() => new(Options.Create(new PasswordResetSettings { CodeProtectionKey = Key }));
    private sealed record Seed(User User, PasswordResetChallenge Challenge, string RawRefreshToken, Guid ForeignUserId);

    private sealed class ObservingSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Messages { get; } = new();
        public TaskCompletionSource<EmailMessage> Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<EmailMessage, Task>? BeforeSend { get; set; }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            try
            {
                if (BeforeSend is not null)
                {
                    await BeforeSend(message);
                }
                Messages.Enqueue(message);
                Delivered.TrySetResult(message);
            }
            catch (Exception exception)
            {
                Delivered.TrySetException(exception);
                throw;
            }
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
