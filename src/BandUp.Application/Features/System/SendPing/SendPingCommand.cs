using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.System.SendPing;

public sealed record SendPingCommand(string Message) : ICommand<Guid>;
