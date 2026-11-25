using System.Diagnostics;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Refresh;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class RefreshHandlerTests(BandUpTestEnvironment environment)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AUTH_09_Refresh_Commits_Rotation_With_Current_Role_And_Configured_Lifetimes()
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession original = await SeedAsync(factory);
        SeededSession foreign = await SeedAsync(factory);
        using IServiceScope scope = factory.Services.CreateScope();
        BandUpDbContext dispatchContext = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        // An earlier read in this scope must not hide a subsequent role change.
        await dispatchContext.Users.FindAsync(original.User.Id);
        await using (BandUpDbContext update = CreateContext())
        {
            User user = (await update.Users.FindAsync(original.User.Id))!;
            user.ChangeRole(Role.Admin);
            await update.SaveChangesAsync();
        }

        Result<RefreshOutcome> result = await SendAsync(scope, original.RawToken, "new browser");

        result.IsSuccess.ShouldBeTrue();
        RefreshOutcome.Issued issued = result.Value.ShouldBeOfType<RefreshOutcome.Issued>();
        issued.RefreshToken.ShouldNotBe(original.RawToken);
        issued.RefreshTokenExpiresAt.ShouldBe(Now.AddDays(3));
        issued.AccessToken.ExpiresAt.ShouldBe(Now.AddMinutes(5));
        JsonWebToken access = new JsonWebTokenHandler().ReadJsonWebToken(issued.AccessToken.Value);
        access.GetClaim(IdentityClaimTypes.Subject).Value.ShouldBe(original.User.Id.ToString());
        access.GetClaim(IdentityClaimTypes.Role).Value.ShouldBe("Admin");

        // Verify from another connection before disposing the dispatch scope.
        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> rows = await verification.RefreshTokens
            .Where(token => token.UserId == original.User.Id).ToListAsync();
        rows.Count.ShouldBe(2);
        RefreshToken old = rows.Single(token => token.Id == original.Token.Id);
        RefreshToken successor = rows.Single(token => token.Id != original.Token.Id);
        old.RevokedAt.ShouldBe(Now);
        old.ReplacedById.ShouldBe(successor.Id);
        successor.RevokedAt.ShouldBeNull();
        successor.ExpiresAt.ShouldBe(issued.RefreshTokenExpiresAt);
        successor.UserAgent.ShouldBe("new browser");
        ITokenService tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        successor.TokenHash.ShouldBe(tokens.HashRefreshToken(issued.RefreshToken));
        rows.ShouldAllBe(token => token.TokenHash != original.RawToken && token.TokenHash != issued.RefreshToken);
        (await verification.RefreshTokens.FindAsync(foreign.Token.Id))!.RevokedAt.ShouldBeNull();
        (await verification.Users.FindAsync(original.User.Id))!.LastLoginAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AUTH_09_Reuse_Commits_All_User_Revocations_Even_When_Expired_Or_Inactive(bool expired, bool inactive)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession original = await SeedAsync(factory, expired: expired, revoked: true, inactive: inactive);
        SeededSession foreign = await SeedAsync(factory);
        await using (BandUpDbContext seed = CreateContext())
        {
            seed.RefreshTokens.Add(NewToken(factory, original.User.Id, Now.AddDays(1)));
            seed.RefreshTokens.Add(NewToken(factory, original.User.Id, Now.AddDays(-1)));
            await seed.SaveChangesAsync();
        }

        using IServiceScope scope = factory.Services.CreateScope();
        Result<RefreshOutcome> result = await SendAsync(scope, original.RawToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<RefreshOutcome.RejectedAfterRevocation>();
        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> rows = await verification.RefreshTokens
            .Where(token => token.UserId == original.User.Id).ToListAsync();
        rows.Count.ShouldBe(3);
        rows.Single(token => token.Id == original.Token.Id).RevokedAt.ShouldBe(Now.AddMinutes(-1));
        rows.Where(token => token.Id != original.Token.Id).ShouldAllBe(token => token.RevokedAt == Now);
        (await verification.RefreshTokens.FindAsync(foreign.Token.Id))!.RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("inactive")]
    [InlineData("missing-user")]
    [InlineData("unknown")]
    [InlineData("missing")]
    public async Task AUTH_09_Invalid_Refresh_Does_Not_Issue_Or_Revoke_Sessions(string scenario)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession original = await SeedAsync(factory, expired: scenario == "expired", inactive: scenario == "inactive");
        SeededSession foreign = await SeedAsync(factory);

        if (scenario == "missing-user")
        {
            await using BandUpDbContext deletion = CreateContext();
            User user = (await deletion.Users.FindAsync(original.User.Id))!;
            deletion.Users.Remove(user);
            await deletion.SaveChangesAsync();
        }

        string? rawToken = scenario switch
        {
            "unknown" => "unknown-refresh-credential",
            "missing" => null,
            _ => original.RawToken,
        };
        using IServiceScope scope = factory.Services.CreateScope();
        Result<RefreshOutcome> result = await SendAsync(scope, rawToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.InvalidRefreshTokenCode);
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> owned = await verification.RefreshTokens
            .Where(token => token.UserId == original.User.Id).ToListAsync();
        owned.Count.ShouldBe(scenario == "missing-user" ? 0 : 1);
        owned.ShouldAllBe(token => token.RevokedAt == null && token.ReplacedById == null);
        (await verification.RefreshTokens.FindAsync(foreign.Token.Id))!.RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AUTH_09_Storage_And_Commit_Failures_Roll_Back_Rotation_Or_Reuse(bool reuse, bool failCommit)
    {
        StoreFailure failure = failCommit ? StoreFailure.None : reuse ? StoreFailure.AfterRevocation : StoreFailure.AfterInsertion;
        await using BandUpApiFactory factory = CreateFactory(failure: failure, failCommit: failCommit);
        SeededSession original = await SeedAsync(factory, revoked: reuse);
        RefreshToken other = NewToken(factory, original.User.Id, Now.AddDays(1));
        await using (BandUpDbContext seed = CreateContext())
        {
            seed.RefreshTokens.Add(other);
            await seed.SaveChangesAsync();
        }

        using IServiceScope scope = factory.Services.CreateScope();
        await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(scope, original.RawToken));

        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> rows = await verification.RefreshTokens
            .Where(token => token.UserId == original.User.Id).ToListAsync();
        rows.Count.ShouldBe(2);
        RefreshToken savedOriginal = rows.Single(token => token.Id == original.Token.Id);
        savedOriginal.RevokedAt.ShouldBe(reuse ? Now.AddMinutes(-1) : null);
        savedOriginal.ReplacedById.ShouldBeNull();
        rows.Single(token => token.Id == other.Id).RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_09_Concurrent_Refresh_Reuses_The_Old_Token_And_Revokes_The_Winners_Successor()
    {
        RefreshGate gate = new();
        await using BandUpApiFactory factory = CreateFactory(gate: gate);
        SeededSession original = await SeedAsync(factory);
        using IServiceScope firstScope = factory.Services.CreateScope();
        using IServiceScope secondScope = factory.Services.CreateScope();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        Task<Result<RefreshOutcome>> first = SendAsync(firstScope, original.RawToken, cancellationToken: timeout.Token);
        Task<Result<RefreshOutcome>>? second = null;

        try
        {
            await gate.FirstLocked.Task.WaitAsync(timeout.Token);
            second = SendAsync(secondScope, original.RawToken, cancellationToken: timeout.Token);
            await gate.SecondStarted.Task.WaitAsync(timeout.Token);
            await WaitUntilBlockedAsync(secondScope.ServiceProvider.GetRequiredService<BandUpDbContext>(), second, timeout.Token);
        }
        finally
        {
            gate.ReleaseFirst.TrySetResult();
        }

        Result<RefreshOutcome>[] results = await Task.WhenAll(first, second!).WaitAsync(timeout.Token);
        results[0].IsSuccess.ShouldBeTrue();
        RefreshOutcome.Issued issued = results[0].Value.ShouldBeOfType<RefreshOutcome.Issued>();
        results[1].IsSuccess.ShouldBeTrue();
        results[1].Value.ShouldBeOfType<RefreshOutcome.RejectedAfterRevocation>();

        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> rows = await verification.RefreshTokens
            .Where(token => token.UserId == original.User.Id).ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(token => token.RevokedAt == Now);
        string successorHash = factory.Services.GetRequiredService<ITokenService>().HashRefreshToken(issued.RefreshToken);
        rows.Single(token => token.TokenHash == successorHash).RevokedAt.ShouldBe(Now);
    }

    private BandUpApiFactory CreateFactory(
        RefreshGate? gate = null,
        StoreFailure failure = StoreFailure.None,
        bool failCommit = false) => new(environment, overrides: new()
        {
            ["Auth:AccessTokenMinutes"] = "5",
            ["Auth:RefreshTokenDays"] = "3",
        }, configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(new FixedTimeProvider());

            if (gate is not null || failure != StoreFailure.None)
            {
                services.AddScoped<IRefreshTokenStore>(provider =>
                {
                    BandUpDbContext context = provider.GetRequiredService<BandUpDbContext>();
                    return new ProbeRefreshTokenStore(new EfRefreshTokenStore(context), context, gate, failure);
                });
            }

            if (failCommit)
            {
                services.AddScoped<IUnitOfWork>(provider => new FailingCommitUnitOfWork(
                    new EfUnitOfWork(provider.GetRequiredService<BandUpDbContext>())));
            }
        });

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<SeededSession> SeedAsync(BandUpApiFactory factory, bool expired = false, bool revoked = false, bool inactive = false)
    {
        await using BandUpDbContext context = CreateContext();
        User user = User.Create($"refresh-handler-{Guid.CreateVersion7():N}@example.com", "Refresh handler", "test-hash", Now.AddDays(-2));

        if (inactive)
        {
            user.Deactivate();
        }

        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        string rawToken = tokens.GenerateRefreshToken();
        RefreshToken token = RefreshToken.Issue(
            user.Id, tokens.HashRefreshToken(rawToken), Now.AddDays(-1), expired ? Now.AddSeconds(-1) : Now.AddDays(7), null);

        if (revoked)
        {
            token.Revoke(Now.AddMinutes(-1));
        }

        context.Users.Add(user);
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();
        return new SeededSession(user, token, rawToken);
    }

    private static RefreshToken NewToken(BandUpApiFactory factory, Guid userId, DateTimeOffset expiresAt)
    {
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        return RefreshToken.Issue(userId, tokens.HashRefreshToken(tokens.GenerateRefreshToken()), Now.AddDays(-2), expiresAt, null);
    }

    private static Task<Result<RefreshOutcome>> SendAsync(
        IServiceScope scope,
        string? rawToken,
        string? userAgent = null,
        CancellationToken cancellationToken = default) =>
        scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync<RefreshCommand, RefreshOutcome>(
            new RefreshCommand(rawToken, userAgent), cancellationToken);

    private async Task WaitUntilBlockedAsync(BandUpDbContext context, Task operation, CancellationToken cancellationToken)
    {
        int processId = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        await using NpgsqlConnection observer = new(environment.MigratedConnectionString);
        await observer.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = observer.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1 AND wait_event_type = 'Lock')";
        command.Parameters.AddWithValue(processId);
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (operation.IsCompleted)
            {
                await operation;
                throw new InvalidOperationException("The second refresh did not wait for the first user's session lock.");
            }

            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("PostgreSQL did not report the expected refresh lock wait.");
    }

    private sealed record SeededSession(User User, RefreshToken Token, string RawToken);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private enum StoreFailure { None, AfterInsertion, AfterRevocation }

    private sealed class RefreshGate
    {
        public int Lookups;
        public TaskCompletionSource FirstLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ProbeRefreshTokenStore(
        IRefreshTokenStore inner,
        BandUpDbContext context,
        RefreshGate? gate,
        StoreFailure failure) : IRefreshTokenStore
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            inner.FindByTokenHashAsync(tokenHash, cancellationToken);

        public async Task<RefreshSession?> FindSessionForUpdateAsync(string tokenHash, CancellationToken cancellationToken)
        {
            int lookup = gate is null ? 0 : Interlocked.Increment(ref gate.Lookups);

            if (lookup == 2)
            {
                gate!.SecondStarted.TrySetResult();
            }

            RefreshSession? session = await inner.FindSessionForUpdateAsync(tokenHash, cancellationToken);

            if (lookup == 1)
            {
                gate!.FirstLocked.TrySetResult();
                await gate.ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            return session;
        }

        public Task<IReadOnlyList<RefreshToken>> ListUsableForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.ListUsableForUserAsync(userId, cancellationToken);

        public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
        {
            await inner.AddAsync(token, cancellationToken);

            if (failure == StoreFailure.AfterInsertion)
            {
                await context.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("Test failure after flushing token rotation.");
            }
        }

        public async Task RevokeAllForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
        {
            await inner.RevokeAllForUserAsync(userId, revokedAt, cancellationToken);

            if (failure == StoreFailure.AfterRevocation)
            {
                await context.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("Test failure after flushing session revocation.");
            }
        }

        public Task RevokeForUserAsync(Guid userId, string tokenHash, DateTimeOffset revokedAt, CancellationToken cancellationToken) =>
            inner.RevokeForUserAsync(userId, tokenHash, revokedAt, cancellationToken);
    }

    private sealed class FailingCommitUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => inner.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => inner.BeginTransactionAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Test failure before database commit.");
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => inner.RollbackTransactionAsync(cancellationToken);
    }
}
