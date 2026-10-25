namespace BandUp.Contracts.Messages;

/// <summary>
/// Diagnostic message. The one message in this step that carries user-supplied text; business
/// messages carry ids only.
/// </summary>
public sealed record PingRequested(Guid PingId, string Message, DateTimeOffset RequestedAt);
