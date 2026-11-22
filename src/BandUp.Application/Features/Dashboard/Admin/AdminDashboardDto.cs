namespace BandUp.Application.Features.Dashboard.Admin;

public sealed record AdminDashboardDto(
    int PublishedPassages,
    int DraftPassages,
    int TotalLessons,
    int TotalUsers);
