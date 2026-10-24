namespace BandUp.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(
            items,
            Math.Max(page, 1),
            Math.Clamp(pageSize, 1, PageRequest.MaxPageSize),
            Math.Max(totalCount, 0));

    public static PagedResult<T> Create(IReadOnlyList<T> items, PageRequest request, int totalCount) =>
        new(items, request.Page, request.PageSize, Math.Max(totalCount, 0));
}