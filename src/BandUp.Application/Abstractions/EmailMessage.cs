namespace BandUp.Application.Abstractions;

/// <summary>In-process delivery material, never a RabbitMQ business message or a log property.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody);
