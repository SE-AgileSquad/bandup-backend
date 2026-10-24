namespace BandUp.Application.Common;

/// <summary>
/// Page-size limits shared by <see cref="PageRequest"/> and <see cref="PagedResult{T}"/>.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}