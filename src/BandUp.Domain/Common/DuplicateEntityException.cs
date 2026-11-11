namespace BandUp.Domain.Common;

/// <summary>
/// Thrown by persistence when a unique index rejects a write. Unlike <see cref="DomainException"/>
/// this is an *expected* concurrency outcome (a race between two identical writes), not a broken
/// invariant — the API layer maps it to 409 instead of 500.
/// </summary>
public sealed class DuplicateEntityException(string message) : Exception(message);
