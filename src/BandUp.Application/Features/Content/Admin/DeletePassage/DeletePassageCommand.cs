using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.DeletePassage;

public sealed record DeletePassageCommand(Guid PassageId) : ICommand<bool>;
