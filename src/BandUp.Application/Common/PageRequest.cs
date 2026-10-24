namespace BandUp.Application.Common;

/// <summary>
/// Paged query input. Clamping happens here so no handler re-implements the limits.
/// </summary>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; } = Math.Max(Page, 1);

    public int PageSize { get; } = Math.Clamp(PageSize, 1, PageRequest.MaxPageSize);

    public int Skip => (this.Page - 1) * this.PageSize;
}