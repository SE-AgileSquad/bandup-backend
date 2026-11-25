using System.Data;
using System.Diagnostics;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class PasswordResetStoreTests(BandUpTestEnvironment environment)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AUTH_15_Locked_Email_Lookup_Is_Case_Insensitive_And_Returns_Tracked_User_State()
    {
        User user = await SeedUserAsync();
        await using BandUpDbContext context = CreateContext();
        EfPasswordResetChallengeStore store = new(context);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

        (await store.FindForUpdateAsync($"missing-{Guid.CreateVersion7():N}@example.com", CancellationToken.None)).ShouldBeNull();
        var session = (await store.FindForUpdateAsync(user.Email.ToUpperInvariant(), CancellationToken.None))!;
        session.User.Id.ShouldBe(user.Id);
        session.Challenge.ShouldBeNull();
        session.User.ChangePassword("new-test-hash");
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using BandUpDbContext verification = CreateContext();
        (await verification.Users.FindAsync(user.Id))!.PasswordHash.ShouldBe("new-test-hash");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AUTH_15_Replacement_Invalidates_The_Previous_Code_Only_If_Committed(bool commit)
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        PasswordResetChallenge replacement = NewChallenge(original.UserId, Now.AddMinutes(1));
        await using BandUpDbContext context = CreateContext();
        EfPasswordResetChallengeStore store = new(context);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        PasswordResetChallenge tracked = (await store.FindForUpdateAsync(await EmailForAsync(original.UserId), CancellationToken.None))!.Challenge!;

        await store.ReplaceAsync(replacement, CancellationToken.None);
        tracked.InvalidatedAt.ShouldBe(replacement.CreatedAt);
        await context.SaveChangesAsync();
        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        await using BandUpDbContext verification = CreateContext();
        (await verification.PasswordResetChallenges.FindAsync(original.Id))!.InvalidatedAt
            .ShouldBe(commit ? replacement.CreatedAt : null);
        (await verification.PasswordResetChallenges.FindAsync(replacement.Id) is not null).ShouldBe(commit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AUTH_15_Waiting_Read_Refreshes_Stale_User_Challenge_And_Xmin_After_Commit_Or_Rollback(bool commit)
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        string email = await EmailForAsync(original.UserId);
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        User staleUser = await second.Users.SingleAsync(user => user.Id == original.UserId);
        PasswordResetChallenge stale = await second.PasswordResetChallenges.SingleAsync(challenge => challenge.Id == original.Id);
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();
        var winning = (await new EfPasswordResetChallengeStore(first).FindForUpdateAsync(email, CancellationToken.None))!;
        Task<BandUp.Application.Features.Identity.PasswordResetSession?> waiting =
            new EfPasswordResetChallengeStore(second).FindForUpdateAsync(email, CancellationToken.None);
        await WaitUntilBlockedAsync(second, waiting);

        winning.User.ChangePassword("updated-under-lock");
        winning.Challenge!.Consume(Now.AddSeconds(1));
        await first.SaveChangesAsync();
        if (commit)
        {
            await firstTransaction.CommitAsync();
        }
        else
        {
            await firstTransaction.RollbackAsync();
        }

        var observed = (await waiting.WaitAsync(TimeSpan.FromSeconds(10)))!;
        observed.User.ShouldBeSameAs(staleUser);
        observed.Challenge.ShouldBeSameAs(stale);
        observed.User.PasswordHash.ShouldBe(commit ? "updated-under-lock" : "test-hash");
        observed.Challenge!.ConsumedAt.ShouldBe(commit ? Now.AddSeconds(1) : null);
        observed.Challenge.IsUsable(Now.AddSeconds(2)).ShouldBe(!commit);
        if (!commit)
        {
            observed.Challenge.Consume(Now.AddSeconds(2));
            await second.SaveChangesAsync();
        }
        await secondTransaction.CommitAsync();
    }

    [Fact]
    public async Task AUTH_15_Invalidate_Others_Preserves_Staged_Password_Change_And_Consumption()
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        await using BandUpDbContext context = CreateContext();
        EfPasswordResetChallengeStore store = new(context);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        var session = (await store.FindForUpdateAsync(await EmailForAsync(original.UserId), CancellationToken.None))!;
        session.User.ChangePassword("changed-password");
        session.Challenge!.Consume(Now.AddSeconds(1));

        await store.InvalidateOthersAsync(session.User.Id, session.Challenge.Id, Now.AddSeconds(1), CancellationToken.None);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using BandUpDbContext verification = CreateContext();
        PasswordResetChallenge saved = (await verification.PasswordResetChallenges.FindAsync(original.Id))!;
        saved.ConsumedAt.ShouldBe(Now.AddSeconds(1));
        saved.InvalidatedAt.ShouldBeNull();
        (await verification.Users.FindAsync(original.UserId))!.PasswordHash.ShouldBe("changed-password");
    }

    [Fact]
    public async Task AUTH_15_Xmin_Rejects_An_Update_From_A_Stale_Context()
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        PasswordResetChallenge winning = await first.PasswordResetChallenges.SingleAsync(challenge => challenge.Id == original.Id);
        PasswordResetChallenge stale = await second.PasswordResetChallenges.SingleAsync(challenge => challenge.Id == original.Id);
        winning.RecordFailedAttempt(Now);
        stale.RecordFailedAttempt(Now);
        await first.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using BandUpDbContext verification = CreateContext();
        (await verification.PasswordResetChallenges.FindAsync(original.Id))!.FailedAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task AUTH_15_Database_Rejects_Two_Open_Challenges_For_One_User()
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        await using BandUpDbContext context = CreateContext();
        context.PasswordResetChallenges.Add(NewChallenge(original.UserId, Now.AddMinutes(1)));
        DbUpdateException failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task AUTH_15_Store_Rejects_Pending_Changes_Instead_Of_Discarding_Them()
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        string email = await EmailForAsync(original.UserId);
        await using BandUpDbContext context = CreateContext();
        EfPasswordResetChallengeStore store = new(context);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        var session = (await store.FindForUpdateAsync(email, CancellationToken.None))!;
        session.Challenge!.RecordFailedAttempt(Now);

        await Should.ThrowAsync<InvalidOperationException>(() => store.FindForUpdateAsync(email, CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReplaceAsync(
            NewChallenge(original.UserId, Now.AddMinutes(1)), CancellationToken.None));
        session.Challenge.FailedAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task AUTH_15_Recovery_And_Refresh_Share_The_Same_User_Lock()
    {
        PasswordResetChallenge original = await SeedChallengeAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();
        await new EfPasswordResetChallengeStore(first).FindForUpdateAsync(await EmailForAsync(original.UserId), CancellationToken.None);
        Task waiting = new EfRefreshTokenStore(second).RevokeAllForUserAsync(original.UserId, Now, CancellationToken.None);
        await WaitUntilBlockedAsync(second, waiting);
        await firstTransaction.RollbackAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AUTH_15_Locking_Requires_Read_Committed_And_Different_Users_Do_Not_Block()
    {
        User firstUser = await SeedUserAsync();
        User secondUser = await SeedUserAsync();
        await using BandUpDbContext first = CreateContext();
        await using BandUpDbContext second = CreateContext();
        await using IDbContextTransaction firstTransaction = await first.Database.BeginTransactionAsync();
        await using IDbContextTransaction secondTransaction = await second.Database.BeginTransactionAsync();
        await new EfPasswordResetChallengeStore(first).FindForUpdateAsync(firstUser.Email, CancellationToken.None);
        (await new EfPasswordResetChallengeStore(second).FindForUpdateAsync(secondUser.Email, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10))).ShouldNotBeNull();
        await using BandUpDbContext wrongIsolation = CreateContext();
        await using IDbContextTransaction repeatable = await wrongIsolation.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await Should.ThrowAsync<InvalidOperationException>(() => new EfPasswordResetChallengeStore(wrongIsolation)
            .FindForUpdateAsync(firstUser.Email, CancellationToken.None));
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<User> SeedUserAsync()
    {
        await using BandUpDbContext context = CreateContext();
        User user = User.Create($"reset-{Guid.CreateVersion7():N}@example.com", "Reset", "test-hash", Now);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<PasswordResetChallenge> SeedChallengeAsync()
    {
        User user = await SeedUserAsync();
        await using BandUpDbContext context = CreateContext();
        PasswordResetChallenge challenge = NewChallenge(user.Id, Now);
        context.PasswordResetChallenges.Add(challenge);
        await context.SaveChangesAsync();
        return challenge;
    }

    private async Task<string> EmailForAsync(Guid userId)
    {
        await using BandUpDbContext context = CreateContext();
        return await context.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync();
    }

    private static PasswordResetChallenge NewChallenge(Guid userId, DateTimeOffset createdAt) =>
        PasswordResetChallenge.Issue(Guid.CreateVersion7(), userId, new string('a', 64), createdAt, createdAt.AddMinutes(10), 5);

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
                throw new InvalidOperationException("Recovery operation bypassed the expected user lock.");
            }
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("PostgreSQL did not report the expected recovery lock wait.");
    }
}
