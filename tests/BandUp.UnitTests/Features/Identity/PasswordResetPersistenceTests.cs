using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class PasswordResetPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AUTH_15_Store_Rejects_Missing_Transactions_Before_Connecting_To_Postgres()
    {
        await using BandUpDbContext context = CreateContext();
        EfPasswordResetChallengeStore store = new(context);
        PasswordResetChallenge challenge = PasswordResetChallenge.Issue(
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Now, Now.AddMinutes(10), 5);

        await Should.ThrowAsync<InvalidOperationException>(() => store.FindForUpdateAsync("test@example.com", CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReplaceAsync(challenge, CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => store.InvalidateOthersAsync(
            challenge.UserId, challenge.Id, Now, CancellationToken.None));
    }

    [Fact]
    public void AUTH_15_Recovery_Migration_Matches_The_Model_And_Uses_The_System_Xmin_Column()
    {
        using BandUpDbContext context = CreateContext();
        context.Database.HasPendingModelChanges().ShouldBeFalse();
        string migration = context.Database.GetMigrations().Single(id => id.EndsWith("_AddPasswordResetChallenges", StringComparison.Ordinal));
        string sql = context.GetService<IMigrator>().GenerateScript("20261111145952_AlignBillingWithSpec", migration);

        sql.ShouldContain("CREATE TABLE identity.password_reset_challenges");
        sql.ShouldContain("CREATE UNIQUE INDEX ux_password_reset_open_user");
        sql.ShouldContain("consumed_at IS NULL AND invalidated_at IS NULL");
        sql.ShouldNotContain("xmin xid");
        sql.ShouldNotContain("DROP TABLE");
        context.Model.FindEntityType(typeof(PasswordResetChallenge))!.FindProperty(nameof(PasswordResetChallenge.Version))!
            .IsConcurrencyToken.ShouldBeTrue();
    }

    private static BandUpDbContext CreateContext() => new(new DbContextOptionsBuilder<BandUpDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
}
