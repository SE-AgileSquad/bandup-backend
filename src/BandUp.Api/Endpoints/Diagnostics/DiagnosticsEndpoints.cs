namespace BandUp.Api.Endpoints.Diagnostics;

/// <summary>
/// Demonstrates that unhandled exceptions reach Sentry and come back as the platform error
/// format. Mapped only when diagnostics are enabled and the environment is not Production.
/// </summary>
public sealed class DiagnosticsEndpoints : IEndpointGroup
{
    public string Prefix => "_debug";

    public bool RequiresDiagnostics => true;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/throw", Throw).WithName("Diagnostics_Throw");
    }

    private static IResult Throw() =>
        throw new InvalidOperationException("Diagnostics endpoint: deliberate failure.");
}