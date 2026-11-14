using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Reading.GetPassage;

public sealed record GetPassageQuery(Guid PassageId) : IQuery<PassageDto>;
