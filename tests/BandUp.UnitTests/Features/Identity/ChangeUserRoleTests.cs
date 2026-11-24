using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Admin.ChangeUserRole;
using BandUp.Domain.Identity;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class ChangeUserRoleTests
{
    [Theory]
    [InlineData(1, "The last active admin cannot be demoted.")]
    [InlineData(2, "Admins cannot demote themselves.")]
    public async Task Admin_Cannot_Demote_Their_Own_Account(int activeAdmins, string message)
    {
        User actor = User.Create("admin@example.test", "Admin", "test-hash", DateTimeOffset.UtcNow);
        actor.ChangeRole(Role.Admin);
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actor.Id);
        IUserRepository users = Substitute.For<IUserRepository>();
        users.IsActiveAdminAsync(actor.Id, CancellationToken.None).Returns(true);
        users.CountActiveAdminsAsync(CancellationToken.None).Returns(activeAdmins);
        users.GetByIdAsync(actor.Id, CancellationToken.None).Returns(actor);

        ChangeUserRoleHandler handler = new(users, currentUser);
        Result<bool> result = await handler.HandleAsync(new ChangeUserRoleCommand(actor.Id, Role.User),
            CancellationToken.None);

        result.Error!.Code.ShouldBe(Error.ConflictCode);
        result.Error.Message.ShouldBe(message);
        actor.Role.ShouldBe(Role.Admin);
    }
}
