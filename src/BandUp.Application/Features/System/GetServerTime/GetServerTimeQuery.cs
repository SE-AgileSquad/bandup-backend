using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.System.GetServerTime;

public sealed record GetServerTimeQuery : IQuery<ServerTimeDto>;
