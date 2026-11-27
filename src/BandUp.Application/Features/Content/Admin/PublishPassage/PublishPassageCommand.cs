using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.PublishPassage;

public sealed record PublishPassageCommand(Guid PassageId, bool ReviewConfirmed) : ICommand<bool>;

public sealed record PublishPassageInput(bool ReviewConfirmed);
