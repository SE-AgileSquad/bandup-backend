using System.Security.Cryptography;
using System.Text;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Common;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Persistence;
using BandUp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class PasswordResetTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Repeat((byte)42, 32).ToArray();
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public void AUTH_17_Reset_Authorization_Has_Strict_Time_Boundaries(int seconds, bool usable) =>
        Token().IsUsable(Now.AddSeconds(seconds)).ShouldBe(usable);

    [Fact]
    public void AUTH_17_Authorization_Requires_Fresh_Code_Consumption_And_Uses_That_Challenges_Owner()
    {
        var challenge = Challenge();
        Should.Throw<DomainException>(() => PasswordResetToken.Issue(challenge, Hash, Now, Now.AddMinutes(5)));
        challenge.Consume(Now);
        var token = PasswordResetToken.Issue(challenge, Hash, Now, Now.AddMinutes(5));
        token.UserId.ShouldBe(challenge.UserId);
        token.ChallengeId.ShouldBe(challenge.Id);
        Should.Throw<DomainException>(() => PasswordResetToken.Issue(challenge, Hash, Now.AddSeconds(1), Now.AddMinutes(5)));
    }

    [Fact]
    public void AUTH_17_Consumed_Or_Invalidated_Tokens_Cannot_Be_Reused()
    {
        var consumed = Token();
        consumed.Consume(Now);
        Should.Throw<DomainException>(() => consumed.Consume(Now));
        consumed.Invalidate(Now.AddSeconds(1));
        consumed.InvalidatedAt.ShouldBeNull();
        var invalidated = Token();
        invalidated.Invalidate(Now);
        invalidated.Invalidate(Now.AddSeconds(1));
        invalidated.InvalidatedAt.ShouldBe(Now);
        Should.Throw<DomainException>(() => invalidated.Consume(Now));
    }

    [Fact]
    public void AUTH_17_Reset_Token_Rejects_Expired_Non_Utc_And_Malformed_Writes()
    {
        var token = Token();
        Should.Throw<DomainException>(() => token.Consume(Now.AddMinutes(5)));
        Should.Throw<DomainException>(() => token.Consume(Now.ToOffset(TimeSpan.FromHours(3))));
        Should.Throw<DomainException>(() => token.Invalidate(Now.AddSeconds(-1)));
        var challenge = Challenge();
        challenge.Consume(Now);
        Should.Throw<DomainException>(() => PasswordResetToken.Issue(challenge, "plaintext", Now, Now.AddMinutes(5)));
        Should.Throw<DomainException>(() => PasswordResetToken.Issue(challenge, Hash, Now, Now));
    }

    [Fact]
    public void AUTH_17_Reset_Tokens_Are_Random_256_Bit_Values_Protected_With_An_Independent_Purpose()
    {
        var protector = Protector();
        HashSet<string> issued = [];
        for (int index = 0; index < 50; index++)
        {
            string token = protector.GenerateToken();
            PasswordResetPolicy.IsResetToken(token).ShouldBeTrue();
            Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Length.ShouldBe(32);
            issued.Add(token).ShouldBeTrue();
        }
        string raw = new('A', 43);
        string expected = Convert.ToHexStringLower(HMACSHA256.HashData(Key,
            Encoding.UTF8.GetBytes("bandup:password-reset:authorization:v1\n" + raw)));
        protector.HashToken(raw).ShouldBe(expected);
        protector.HashToken(new string('B', 43)).ShouldNotBe(expected);
        var rotated = new PasswordResetTokenProtector(Options.Create(new PasswordResetSettings
        {
            CodeProtectionKey = Convert.ToBase64String(Enumerable.Repeat((byte)43, 32).ToArray()),
        }));
        rotated.HashToken(raw).ShouldNotBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-base64-marker")]
    [InlineData("YWJj")]
    public void AUTH_17_Reset_Token_Protection_Uses_Existing_Validated_Recovery_Key(string? key)
    {
        var error = Should.Throw<OptionsValidationException>(() => new PasswordResetTokenProtector(
            Options.Create(new PasswordResetSettings { CodeProtectionKey = key })));
        error.Message.ShouldContain("PasswordReset:CodeProtectionKey");
        if (!string.IsNullOrEmpty(key)) { error.Message.ShouldNotContain(key); }
    }

    [Fact]
    public async Task AUTH_17_Reset_Token_Store_Requires_Transaction_And_Migration_Matches_The_Model()
    {
        await using var db = new BandUpDbContext(new DbContextOptionsBuilder<BandUpDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var store = new EfPasswordResetTokenStore(db);
        await Should.ThrowAsync<InvalidOperationException>(() => store.FindForUpdateAsync(Hash, CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReplaceAsync(Token(), CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => store.InvalidateAllForUserAsync(Guid.CreateVersion7(), Now, CancellationToken.None));
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        string migration = db.Database.GetMigrations().Single(name => name.EndsWith("_AddPasswordResetTokens", StringComparison.Ordinal));
        string sql = db.GetService<IMigrator>().GenerateScript("20261126183723_AddOtpLoginChallenges", migration);
        sql.ShouldContain("CREATE TABLE identity.password_reset_tokens");
        sql.ShouldContain("ux_password_reset_token_open_user");
        sql.ShouldNotContain("DROP TABLE");
        sql.ShouldNotContain("xmin xid");
    }

    private static PasswordResetCodeProtector CodeProtector() => new(Options.Create(new PasswordResetSettings { CodeProtectionKey = Convert.ToBase64String(Key) }));
    private static PasswordResetTokenProtector Protector() => new(Options.Create(new PasswordResetSettings { CodeProtectionKey = Convert.ToBase64String(Key) }));
    private static PasswordResetChallenge Challenge()
    {
        Guid id = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        return PasswordResetChallenge.Issue(id, userId, CodeProtector().Protect(id, userId, "012345"), Now.AddMinutes(-1), Now.AddMinutes(9), 5);
    }
    private static PasswordResetToken Token()
    {
        var challenge = Challenge();
        challenge.Consume(Now);
        return PasswordResetToken.Issue(challenge, Hash, Now, Now.AddMinutes(5));
    }
}
