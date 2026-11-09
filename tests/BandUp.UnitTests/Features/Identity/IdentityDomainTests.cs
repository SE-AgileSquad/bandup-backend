using BandUp.Domain.Common;
using BandUp.Domain.Identity;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

/// <summary>
/// The invariants the identity entities enforce on their own. The endpoint-level AUTH
/// scenarios arrive with the handlers; these guard the behaviour those handlers rely on.
/// </summary>
public sealed class IdentityDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void User_Starts_As_An_Active_Non_Admin()
    {
        User user = User.Create("a@example.com", "A", "hash", Now);

        user.Role.ShouldBe(Role.User);
        user.IsActive.ShouldBeTrue();
        user.Id.ShouldNotBe(Guid.Empty);
        user.CreatedAt.ShouldBe(Now);
        user.LastLoginAt.ShouldBeNull();
    }

    [Fact]
    public void User_Login_Updates_The_Timestamp()
    {
        User user = User.Create("a@example.com", "A", "hash", Now);

        user.RecordLogin(Now.AddHours(1));

        user.LastLoginAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void A_Deactivated_User_Cannot_Login()
    {
        User user = User.Create("a@example.com", "A", "hash", Now);
        user.Deactivate();

        Should.Throw<DomainException>(() => user.RecordLogin(Now));
    }

    [Fact]
    public void Refresh_Token_Is_Usable_Until_Expiry_Or_Revocation()
    {
        RefreshToken token = RefreshToken.Issue(
            Guid.CreateVersion7(),
            "hash",
            Now,
            Now.AddDays(7),
            null);

        token.IsUsable(Now).ShouldBeTrue();
        token.IsUsable(Now.AddDays(7)).ShouldBeFalse();

        token.Revoke(Now);
        token.IsUsable(Now).ShouldBeFalse();
    }

    [Fact]
    public void Rotating_A_Token_Links_It_To_Its_Successor()
    {
        RefreshToken original = RefreshToken.Issue(
            Guid.CreateVersion7(),
            "hash1",
            Now,
            Now.AddDays(7),
            null);

        RefreshToken successor = RefreshToken.Issue(
            original.UserId,
            "hash2",
            Now.AddMinutes(1),
            Now.AddDays(7),
            null);

        original.Rotate(successor);

        original.RevokedAt.ShouldBe(successor.CreatedAt);
        original.ReplacedById.ShouldBe(successor.Id);
        original.IsUsable(Now).ShouldBeFalse();
    }

    [Fact]
    public void Rotating_An_Already_Revoked_Token_Is_A_Programming_Error()
    {
        RefreshToken original = RefreshToken.Issue(
            Guid.CreateVersion7(),
            "hash1",
            Now,
            Now.AddDays(7),
            null);
        original.Revoke(Now);

        RefreshToken successor = RefreshToken.Issue(
            original.UserId,
            "hash2",
            Now.AddMinutes(1),
            Now.AddDays(7),
            null);

        Should.Throw<DomainException>(() => original.Rotate(successor));
    }
}