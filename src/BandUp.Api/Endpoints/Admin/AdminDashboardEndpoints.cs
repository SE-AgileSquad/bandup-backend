using BandUp.Application.Abstractions;
using BandUp.Application.Features.Dashboard.Admin;
using BandUp.Application.Features.Dashboard.Admin.GetAdminDashboard;

namespace BandUp.Api.Endpoints.Admin;

public sealed class AdminDashboardEndpoints : IEndpointGroup
{
    public string Prefix => "admin/dashboard";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AdminContentHttp.RequireAdmin(endpoints)
            .MapGet("", (IDispatcher dispatcher, HttpContext http) =>
                AdminContentHttp.ReadAsync<GetAdminDashboardQuery, AdminDashboardDto>(
                    new GetAdminDashboardQuery(), dispatcher, http))
            .WithName("Admin_GetDashboard")
            .Produces<AdminDashboardDto>();
    }
}
