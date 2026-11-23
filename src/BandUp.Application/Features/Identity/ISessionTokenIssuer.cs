using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>
/// Creates a session using the configured access and refresh lifetimes. The caller adds the
/// refresh row to its transaction and exposes the raw values only after commit.
/// </summary>
public interface ISessionTokenIssuer
{
    IssuedSession Issue(User user, DateTimeOffset now, string? userAgent);
}
