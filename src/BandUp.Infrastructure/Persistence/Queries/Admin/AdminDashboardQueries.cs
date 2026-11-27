using BandUp.Application.Features.Dashboard.Admin;
using BandUp.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence.Queries.Admin;

internal sealed class AdminDashboardQueries(BandUpDbContext context) : IAdminDashboardQueries
{
    public async Task<AdminDashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        int publishedPassages = await context.Passages.CountAsync(
            passage => passage.Status == PassageStatus.Published, cancellationToken);
        int draftPassages = await context.Passages.CountAsync(
            passage => passage.Status == PassageStatus.Draft, cancellationToken);
        int totalLessons = await context.Lessons.CountAsync(cancellationToken);
        // Includes every role and deactivated accounts, whose records are deliberately retained.
        int totalUsers = await context.Users.CountAsync(cancellationToken);

        return new AdminDashboardDto(publishedPassages, draftPassages, totalLessons, totalUsers);
    }
}
