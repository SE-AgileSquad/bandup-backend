using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.GetMe;

/// <summary>
/// AUTH-08: the caller's own profile. Carries no parameters because the identity comes from
/// the ambient <c>ICurrentUser</c>; an anonymous caller fails before reaching the database.
/// </summary>
public sealed record GetMeQuery() : IQuery<MeDto>;
