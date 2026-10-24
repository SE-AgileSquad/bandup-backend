namespace BandUp.Api.Endpoints;

/// <summary>
/// A module's slice of the HTTP surface. Every group is discovered by assembly scan and
/// mapped in one place, so adding a module never means editing the composition root.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>
    /// Path segment appended to <c>/api</c>, for example <c>system</c>.
    /// </summary>
    string Prefix { get; }

    /// <summary>
    /// When true the group is only mapped if the diagnostics feature is enabled.
    /// </summary>
    bool RequiresDiagnostics { get; }

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}