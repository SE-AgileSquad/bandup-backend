using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.PublishPassage;

public sealed record PublishPassageCommand(Guid PassageId) : ICommand<bool>;
