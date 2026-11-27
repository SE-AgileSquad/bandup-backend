using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Dashboard.Admin;
using BandUp.Application.Features.Dashboard.Admin.GetAdminDashboard;
using BandUp.Application.Features.Identity;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Dashboard;

public sealed class AdminDashboardTests
{
    private readonly IAdminDashboardQueries _dashboard = Substitute.For<IAdminDashboardQueries>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICurrentUser _caller = Substitute.For<ICurrentUser>();

    [Fact]
    public async Task An_Anonymous_Caller_Cannot_Read_Statistics()
    {
        GetAdminDashboardHandler handler = new(_dashboard, _users, _caller);

        Result<AdminDashboardDto> result = await handler.HandleAsync(new GetAdminDashboardQuery(), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        await _dashboard.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Caller_Without_Current_Admin_Access_Cannot_Read_Statistics()
    {
        _caller.UserId.Returns(Guid.CreateVersion7());
        _caller.IsAuthenticated.Returns(true);
        GetAdminDashboardHandler handler = new(_dashboard, _users, _caller);

        Result<AdminDashboardDto> result = await handler.HandleAsync(new GetAdminDashboardQuery(), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Forbidden);
        await _dashboard.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
    }
}
