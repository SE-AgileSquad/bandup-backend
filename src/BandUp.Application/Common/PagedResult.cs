namespace BandUp.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(
            items,
            Math.Max(page, 1),
            Math.Clamp(pageSize, 1, MaxPageSize),
            Math.Max(totalCount, 0));
}