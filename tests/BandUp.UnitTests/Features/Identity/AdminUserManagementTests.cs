using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Admin.ChangeUserRole;
using BandUp.Application.Features.Identity.Admin.CreateAdminUser;
using BandUp.Application.Features.Identity.Admin.DeactivateUser;
using BandUp.Application.Features.Identity.Admin.ListAdminUsers;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class AdminUserManagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICurrentUser _caller = Substitute.For<ICurrentUser>();
    private readonly IRefreshTokenStore _refreshTokens = Substitute.For<IRefreshTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ICreditService _credits = Substitute.For<ICreditService>();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Guid _actorId = Guid.CreateVersion7();

    public AdminUserManagementTests()
    {
        _caller.UserId.Returns(_actorId);
        _caller.IsAuthenticated.Returns(true);
        _users.IsActiveSuperAdminAsync(_actorId, Arg.Any<CancellationToken>()).Returns(true);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed-password");
        _credits.OpenSignupAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.Admin)]
    [InlineData(Role.SuperAdmin)]
    public async Task Creation_Assigns_The_Requested_Role_And_Opens_The_Existing_Credit_Account(Role role)
    {
        User? created = null;
        await _users.AddAsync(Arg.Do<User>(user => created = user), Arg.Any<CancellationToken>());
        CreateAdminUserHandler handler = new(_users, _caller, _hasher, _credits, _clock);

        Result<Guid> result = await handler.HandleAsync(
            new CreateAdminUserCommand("new@example.test", "StrongPassword1", " New User ", role), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        created.ShouldNotBeNull();
        created.Id.ShouldBe(result.Value);
        created.Role.ShouldBe(role);
        created.DisplayName.ShouldBe("New User");
        created.PasswordHash.ShouldBe("hashed-password");
        created.CreatedAt.ShouldBe(Now);
        await _credits.Received(1).OpenSignupAccountAsync(result.Value, CancellationToken.None);
    }

    [Fact]
    public async Task Creation_Rejects_Duplicate_Email_Without_Adding_A_User_Or_Credits()
    {
        _users.EmailExistsAsync("existing@example.test", Arg.Any<CancellationToken>()).Returns(true);
        CreateAdminUserHandler handler = new(_users, _caller, _hasher, _credits, _clock);

        Result<Guid> result = await handler.HandleAsync(
            new CreateAdminUserCommand("existing@example.test", "StrongPassword1", "New", Role.Admin), CancellationToken.None);

        result.Error!.Code.ShouldBe(Error.EmailTakenCode);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _credits.DidNotReceive().OpenSignupAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task All_User_Management_Handlers_Reject_An_Actor_Without_The_Current_Super_Admin_Role()
    {
        _users.IsActiveSuperAdminAsync(_actorId, Arg.Any<CancellationToken>()).Returns(false);
        Guid targetId = Guid.CreateVersion7();

        Result<Guid> create = await new CreateAdminUserHandler(_users, _caller, _hasher, _credits, _clock)
            .HandleAsync(new CreateAdminUserCommand("new@example.test", "StrongPassword1", "New", Role.Admin), CancellationToken.None);
        Result<bool> change = await new ChangeUserRoleHandler(_users, _caller)
            .HandleAsync(new ChangeUserRoleCommand(targetId, Role.SuperAdmin), CancellationToken.None);
        Result<bool> deactivate = await new DeactivateUserHandler(_users, _caller, _refreshTokens, _clock)
            .HandleAsync(new DeactivateUserCommand(targetId), CancellationToken.None);
        Result<PagedResult<AdminUserDto>> list = await new ListAdminUsersHandler(_users, _caller)
            .HandleAsync(new ListAdminUsersQuery(new PageRequest()), CancellationToken.None);

        new[] { create.Error!.Code, change.Error!.Code, deactivate.Error!.Code, list.Error!.Code }
            .ShouldAllBe(code => code == Error.ForbiddenCode);
        await _users.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Ordinary_Admin_Can_Be_Demoted_Without_The_Last_Super_Admin_Guard()
    {
        User target = User.Create("teacher@example.test", "Teacher", "hash", Now);
        target.ChangeRole(Role.Admin);
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        Result<bool> result = await new ChangeUserRoleHandler(_users, _caller)
            .HandleAsync(new ChangeUserRoleCommand(target.Id, Role.User), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        target.Role.ShouldBe(Role.User);
        await _users.DidNotReceive().CountActiveSuperAdminsAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task Deactivation_Protects_The_Caller_And_The_Last_Super_Admin(bool self, int superAdmins)
    {
        User target = User.Create("super@example.test", "Super", "hash", Now);
        target.ChangeRole(Role.SuperAdmin);
        if (self)
        {
            _caller.UserId.Returns(target.Id);
            _users.IsActiveSuperAdminAsync(target.Id, Arg.Any<CancellationToken>()).Returns(true);
        }

        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.CountActiveSuperAdminsAsync(Arg.Any<CancellationToken>()).Returns(superAdmins);

        Result<bool> result = await new DeactivateUserHandler(_users, _caller, _refreshTokens, _clock)
            .HandleAsync(new DeactivateUserCommand(target.Id), CancellationToken.None);

        result.Error!.Code.ShouldBe(Error.ConflictCode);
        target.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivation_Preserves_The_User_And_Revokes_All_Refresh_Tokens()
    {
        User target = User.Create("learner@example.test", "Learner", "hash", Now);
        RefreshToken token = RefreshToken.Issue(target.Id, "token-hash", Now, Now.AddDays(7), null);
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _refreshTokens.ListUsableForUserAsync(target.Id, Arg.Any<CancellationToken>()).Returns(new[] { token });
        DeactivateUserHandler handler = new(_users, _caller, _refreshTokens, _clock);

        (await handler.HandleAsync(new DeactivateUserCommand(target.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(new DeactivateUserCommand(target.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        target.IsActive.ShouldBeFalse();
        token.RevokedAt.ShouldBe(Now);
        _users.DidNotReceive().Remove(Arg.Any<User>());
        await _refreshTokens.Received(1).ListUsableForUserAsync(target.Id, CancellationToken.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((Role)999)]
    public void Creation_Requires_A_Supported_Role(Role? role)
    {
        CreateAdminUserValidator validator = new();
        validator.Validate(new CreateAdminUserCommand("new@example.test", "StrongPassword1", "New", role))
            .IsValid.ShouldBeFalse();
    }
}
