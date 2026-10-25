namespace BandUp.Application.Features.System.GetServerTime;

public sealed record ServerTimeDto(DateTimeOffset UtcNow, string Environment, string Version);
