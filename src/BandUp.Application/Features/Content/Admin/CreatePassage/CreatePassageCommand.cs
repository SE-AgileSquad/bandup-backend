using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.CreatePassage;

public sealed record CreatePassageCommand(PassageInput Input) : ICommand<Guid>;
