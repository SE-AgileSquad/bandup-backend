namespace BandUp.Infrastructure.Logging;

/// <summary>Marks private work before SDK breadcrumbs, logs and metrics can enter shared buffers.</summary>
public static class WritingTelemetryBoundary
{
    private static readonly AsyncLocal<bool> PrivateWork = new();

    public static bool IsPrivate => PrivateWork.Value;

    public static IDisposable Enter()
    {
        var previous = PrivateWork.Value;
        PrivateWork.Value = true;
        return new Restore(previous);
    }

    public static bool HasPrivateTag(IReadOnlyDictionary<string, string> tags) =>
        tags.ContainsKey(BandUpLogProperties.PrivateWritingRequest) ||
        tags.ContainsKey(BandUpLogProperties.PrivateWritingPublication) ||
        tags.ContainsKey(BandUpLogProperties.PrivateWritingExpiration);

    public static bool IsWritingUrl(string? url)
    {
        string? path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url?.Split('?', '#')[0];
        return path is not null && (path.Equals("/api/writing", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/writing/", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => PrivateWork.Value = previous;
    }
}
