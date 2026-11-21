using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.GetAdminPassage;

public sealed record GetAdminPassageQuery(Guid PassageId) : IQuery<PassageDetails>;
