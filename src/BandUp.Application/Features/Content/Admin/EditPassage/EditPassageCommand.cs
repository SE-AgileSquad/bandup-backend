using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.EditPassage;

public sealed record EditPassageCommand(Guid PassageId, PassageInput Input) : ICommand<bool>;
