using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// The transport-backed publisher, before the transaction-aware wrapper. Registered as the
/// inner <see cref="IMessagePublisher"/> so the wrapper can reach it without ambiguity.
/// </summary>
public interface IDirectMessagePublisher : IMessagePublisher;