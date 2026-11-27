namespace BandUp.Application.Features.Dashboard.Admin;

public interface IAdminDashboardQueries
{
    Task<AdminDashboardDto> GetAsync(CancellationToken cancellationToken);
}
