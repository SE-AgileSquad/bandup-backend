using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.CreatePassageRevision;

public sealed record CreatePassageRevisionCommand(Guid PassageId) : ICommand<Guid>;
