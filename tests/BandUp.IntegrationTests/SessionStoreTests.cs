using System.Diagnostics;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Login;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Persistence;
using BandUp.Infrastructure.Security;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using StackExchange.Redis;
using Role = BandUp.Domain.Identity.Role;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class SessionStoreTests(BandUpTestEnvironment environment)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AUTH_09_Locked_Read_Sees_Rotation_After_Commit_Or_Original_After_Rollback(bool commit)
    {
        RefreshToken original = await SeedTokenAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        IRefreshTokenStore firstStore = new EfRefreshTokenStore(first);
        IRefreshTokenStore secondStore = new EfRefreshTokenStore(second);
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();

        // Deliberately leave a stale tracked row in the losing connection.
        RefreshToken stale = await second.RefreshTokens.SingleAsync(token => token.Id == original.Id);
        RefreshSession winning = (await firstStore.FindSessionForUpdateAsync(original.TokenHash, CancellationToken.None))!;
        Task<RefreshSession?> waiting = secondStore.FindSessionForUpdateAsync(original.TokenHash, CancellationToken.None);

        await WaitUntilBlockedAsync(second, waiting);

        User user = await first.Users.SingleAsync(user => user.Id == original.UserId);
        user.ChangeRole(Role.Admin);
        RefreshToken successor = NewToken(original.UserId);
        winning.Token.Rotate(successor);
        await firstStore.AddAsync(successor, CancellationToken.None);
        await first.SaveChangesAsync();

        if (commit)
        {
            await firstTransaction.CommitAsync();
        }
        else
        {
            await firstTransaction.RollbackAsync();
        }

        RefreshSession observed = (await waiting.WaitAsync(TimeSpan.FromSeconds(10)))!;
        observed.Token.ShouldBeSameAs(stale);
        observed.Token.RevokedAt.ShouldBe(commit ? Now : null);
        observed.Token.ReplacedById.ShouldBe(commit ? successor.Id : null);
        observed.User.Role.ShouldBe(commit ? Role.Admin : Role.User);

        if (commit)
        {
            // A later reuse handler can revoke the winner's successor in this transaction.
            await secondStore.RevokeAllForUserAsync(original.UserId, Now.AddMinutes(1), CancellationToken.None);
            await second.SaveChangesAsync();
        }

        await secondTransaction.CommitAsync();

        await using BandUpDbContext verification = CreateContext();
        RefreshToken? savedSuccessor = await verification.RefreshTokens.FindAsync(successor.Id);

        if (commit)
        {
            savedSuccessor.ShouldNotBeNull().RevokedAt.ShouldBe(Now.AddMinutes(1));
        }
        else
        {
            savedSuccessor.ShouldBeNull();
        }
    }

    [Fact]
    public async Task AUTH_09_Revoke_All_Waits_For_Insertion_And_Includes_Expired_Unrevoked_Rows()
    {
        RefreshToken original = await SeedTokenAsync();
        RefreshToken foreign = await SeedTokenAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        IRefreshTokenStore firstStore = new EfRefreshTokenStore(first);
        IRefreshTokenStore secondStore = new EfRefreshTokenStore(second);
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();

        RefreshToken expired = RefreshToken.Issue(
            original.UserId, Guid.CreateVersion7().ToString("N"), Now.AddDays(-8), Now.AddDays(-1), null);
        await firstStore.AddAsync(expired, CancellationToken.None);
        Task waiting = secondStore.RevokeAllForUserAsync(original.UserId, Now, CancellationToken.None);
        await WaitUntilBlockedAsync(second, waiting);
        await first.SaveChangesAsync();
        await firstTransaction.CommitAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await second.SaveChangesAsync();
        await secondTransaction.CommitAsync();

        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> owned = await verification.RefreshTokens
            .Where(token => token.UserId == original.UserId).ToListAsync();
        owned.Count.ShouldBe(2);
        owned.ShouldAllBe(token => token.RevokedAt == Now);
        (await verification.RefreshTokens.FindAsync(foreign.Id))!.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_10_Revocation_Waits_For_The_Same_User_Lock()
    {
        RefreshToken token = await SeedTokenAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();
        IRefreshTokenStore firstStore = new EfRefreshTokenStore(first);
        IRefreshTokenStore secondStore = new EfRefreshTokenStore(second);

        await firstStore.FindSessionForUpdateAsync(token.TokenHash, CancellationToken.None);
        Task waiting = secondStore.RevokeForUserAsync(token.UserId, token.TokenHash, Now, CancellationToken.None);
        await WaitUntilBlockedAsync(second, waiting);
        await firstTransaction.RollbackAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await second.SaveChangesAsync();
        await secondTransaction.CommitAsync();

        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(token.Id))!.RevokedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task AUTH_10_Revocation_Is_Owner_Scoped_And_Idempotent()
    {
        RefreshToken owned = await SeedTokenAsync();
        RefreshToken foreign = await SeedTokenAsync();
        await using BandUpDbContext context = CreateContext();
        IRefreshTokenStore store = new EfRefreshTokenStore(context);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        await store.RevokeForUserAsync(owned.UserId, foreign.TokenHash, Now, CancellationToken.None);
        await store.RevokeForUserAsync(owned.UserId, "missing", Now, CancellationToken.None);
        await store.RevokeForUserAsync(owned.UserId, owned.TokenHash, Now, CancellationToken.None);
        await context.SaveChangesAsync();
        await store.RevokeForUserAsync(owned.UserId, owned.TokenHash, Now.AddMinutes(1), CancellationToken.None);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(owned.Id))!.RevokedAt.ShouldBe(Now);
        (await verification.RefreshTokens.FindAsync(foreign.Id))!.RevokedAt.ShouldBeNull();
        // Revoked rows remain available for reuse detection.
        (await store.FindByTokenHashAsync(owned.TokenHash, CancellationToken.None))!.RevokedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task AUTH_09_Different_Users_Do_Not_Block_Each_Other()
    {
        RefreshToken firstToken = await SeedTokenAsync();
        RefreshToken secondToken = await SeedTokenAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();
        await new EfRefreshTokenStore(first).FindSessionForUpdateAsync(firstToken.TokenHash, CancellationToken.None);

        RefreshSession? session = await new EfRefreshTokenStore(second)
            .FindSessionForUpdateAsync(secondToken.TokenHash, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        session.ShouldNotBeNull().User.Id.ShouldBe(secondToken.UserId);
    }

    [Fact]
    public async Task AUTH_09_Locking_And_Session_Writes_Require_An_Open_Transaction()
    {
        RefreshToken token = await SeedTokenAsync();
        await using BandUpDbContext context = CreateContext();
        IRefreshTokenStore store = new EfRefreshTokenStore(context);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.FindSessionForUpdateAsync(token.TokenHash, CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.AddAsync(NewToken(token.UserId), CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.RevokeAllForUserAsync(token.UserId, Now, CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.RevokeForUserAsync(token.UserId, token.TokenHash, Now, CancellationToken.None));
    }

    [Fact]
    public async Task AUTH_09_Locked_Read_Rejects_A_Repeatable_Read_Snapshot()
    {
        RefreshToken token = await SeedTokenAsync();
        await using BandUpDbContext context = CreateContext();
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new EfRefreshTokenStore(context).FindSessionForUpdateAsync(token.TokenHash, CancellationToken.None));
    }

    [Fact]
    public async Task AUTH_06_Login_Uses_The_Shared_Configured_Issuance_Policy()
    {
        const string password = "validPassword1";
        await using BandUpDbContext seed = CreateContext();
        User user = User.Create(
            $"session-login-{Guid.CreateVersion7():N}@example.com", "Session login",
            new BcryptPasswordHasher().Hash(password), Now);
        seed.Users.Add(user);
        await seed.SaveChangesAsync();
        await using BandUpApiFactory factory = new(environment, overrides: new()
        {
            ["Auth:RefreshTokenDays"] = "3",
            ["Auth:AccessTokenMinutes"] = "5",
        });
        using IServiceScope scope = factory.Services.CreateScope();
        Result<LoginResult> result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync<LoginCommand, LoginResult>(
                new LoginCommand(user.Email, password, "fake-captcha-token"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await using BandUpDbContext verification = CreateContext();
        RefreshToken saved = await verification.RefreshTokens.SingleAsync(token => token.UserId == user.Id);
        (saved.ExpiresAt - saved.CreatedAt).ShouldBe(TimeSpan.FromDays(3));
        result.Value.RefreshTokenExpiresAt.ShouldBe(saved.ExpiresAt, TimeSpan.FromMilliseconds(1));
        result.Value.AccessToken.ExpiresAt.ShouldBe(saved.CreatedAt.AddMinutes(5), TimeSpan.FromMilliseconds(1));
        ITokenService tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        saved.TokenHash.ShouldBe(tokens.HashRefreshToken(result.Value.RefreshToken));
    }

    [Fact]
    public async Task AUTH_10_Redis_Revocation_Round_Trips_With_A_Bounded_Ttl()
    {
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(environment.RedisConnectionString);
        CachedAccessTokenRevocationStore store = new(
            new RedisCacheService(connection), TimeProvider.System,
            NullLogger<CachedAccessTokenRevocationStore>.Instance);
        Guid tokenId = Guid.CreateVersion7();
        await store.RevokeAsync(tokenId, TimeProvider.System.GetUtcNow().AddMinutes(1), CancellationToken.None);

        (await store.IsRevokedAsync(tokenId, CancellationToken.None)).ShouldBeTrue();
        TimeSpan? ttl = await connection.GetDatabase().KeyTimeToLiveAsync($"bandup:auth:revoked-jti:{tokenId:N}");
        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeGreaterThan(TimeSpan.FromSeconds(80));
        ttl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(90));
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<RefreshToken> SeedTokenAsync()
    {
        await using BandUpDbContext context = CreateContext();
        User user = User.Create($"session-store-{Guid.CreateVersion7():N}@example.com", "Session store", "test-hash", Now);
        RefreshToken token = NewToken(user.Id);
        context.Users.Add(user);
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();
        return token;
    }

    private static RefreshToken NewToken(Guid userId) =>
        RefreshToken.Issue(userId, Guid.CreateVersion7().ToString("N"), Now, Now.AddDays(7), null);

    private async Task WaitUntilBlockedAsync(BandUpDbContext context, Task operation)
    {
        int processId = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        await using NpgsqlConnection observer = new(environment.MigratedConnectionString);
        await observer.OpenAsync();
        await using NpgsqlCommand command = observer.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1 AND wait_event_type = 'Lock')";
        command.Parameters.AddWithValue(processId);
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (operation.IsCompleted)
            {
                await operation;
                throw new InvalidOperationException("Session operation completed while another transaction held the user lock.");
            }

            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("PostgreSQL did not report the expected session lock wait.");
    }
}
