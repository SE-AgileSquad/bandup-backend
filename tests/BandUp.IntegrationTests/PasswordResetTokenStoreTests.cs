using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class PasswordResetTokenStoreTests(BandUpTestEnvironment environment)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_17_Reset_Token_Invalidation_Is_Transactional(bool commit)
    {
        var token = await SeedAsync();
        await using BandUpDbContext db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await new EfPasswordResetTokenStore(db).InvalidateAllForUserAsync(token.UserId, Now.AddSeconds(1), CancellationToken.None);
        if (commit) { await transaction.CommitAsync(); }
        else { await transaction.RollbackAsync(); }
        await using BandUpDbContext check = Context();
        (await check.PasswordResetTokens.FindAsync(token.Id))!.InvalidatedAt.ShouldBe(commit ? Now.AddSeconds(1) : null);
    }

    [Fact]
    public async Task AUTH_17_A_Waiting_Token_Lookup_Refreshes_Stale_Consumption_And_Xmin()
    {
        var token = await SeedAsync();
        await using BandUpDbContext first = Context();
        await using BandUpDbContext second = Context();
        var stale = await second.PasswordResetTokens.SingleAsync(candidate => candidate.Id == token.Id);
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var winning = (await new EfPasswordResetTokenStore(first).FindForUpdateAsync(token.TokenHash, CancellationToken.None))!;
        var waiting = new EfPasswordResetTokenStore(second).FindForUpdateAsync(token.TokenHash, CancellationToken.None);
        winning.Token.Consume(Now.AddSeconds(1));
        await first.SaveChangesAsync();
        await firstTransaction.CommitAsync();
        var observed = (await waiting.WaitAsync(TimeSpan.FromSeconds(10)))!;
        observed.Token.ShouldBeSameAs(stale);
        observed.Token.ConsumedAt.ShouldBe(Now.AddSeconds(1));
        observed.Token.IsUsable(Now.AddSeconds(2)).ShouldBeFalse();
        await secondTransaction.CommitAsync();
    }

    [Fact]
    public async Task AUTH_17_Replaced_Challenge_Cannot_Authorize_An_Older_Token_Even_Without_Explicit_Invalidation()
    {
        var token = await SeedAsync();
        await using (BandUpDbContext db = Context())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await new EfPasswordResetChallengeStore(db).ReplaceAsync(PasswordResetChallenge.Issue(Guid.CreateVersion7(), token.UserId,
                new string('c', 64), Now.AddSeconds(1), Now.AddMinutes(10), 5), CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using BandUpDbContext check = Context();
        await using var read = await check.Database.BeginTransactionAsync();
        (await new EfPasswordResetTokenStore(check).FindForUpdateAsync(token.TokenHash, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_17_Token_Xmin_Rejects_Stale_Consumption()
    {
        var token = await SeedAsync();
        await using BandUpDbContext first = Context();
        await using BandUpDbContext second = Context();
        var winning = (await first.PasswordResetTokens.FindAsync(token.Id))!;
        var stale = (await second.PasswordResetTokens.FindAsync(token.Id))!;
        winning.Consume(Now);
        stale.Consume(Now);
        await first.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private BandUpDbContext Context() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
    private async Task<PasswordResetToken> SeedAsync()
    {
        var user = User.Create($"grant-store-{Guid.CreateVersion7():N}@example.com", "Learner", "hash", Now.AddDays(-1));
        var challenge = PasswordResetChallenge.Issue(Guid.CreateVersion7(), user.Id, new string('a', 64), Now.AddMinutes(-1), Now.AddMinutes(9), 5);
        challenge.Consume(Now);
        var token = PasswordResetToken.Issue(challenge, Guid.CreateVersion7().ToString("N") + Guid.CreateVersion7().ToString("N"), Now, Now.AddMinutes(5));
        await using BandUpDbContext db = Context();
        db.Users.Add(user);
        db.PasswordResetChallenges.Add(challenge);
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }
}
