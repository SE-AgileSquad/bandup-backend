using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace BandUp.Api.Auth;

/// <summary>
/// The only place the auth cookies are written. Register and login produce tokens; this
/// service puts them in the two HttpOnly cookies the spec fixes (AUTH-06). Centralised so
/// the attributes never drift between endpoints, and so tests have one seam to inspect.
/// </summary>
public sealed class AuthCookieService(IOptions<AuthSettings> options)
{
    public void SetSessionCookies(
        IResponseCookies cookies,
        AccessToken accessToken,
        string rawRefreshToken)
    {
        AuthSettings settings = options.Value;

        CookieOptions access = new()
        {
            HttpOnly = true,
            Secure = settings.RequireSecureCookies,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
        };

        CookieOptions refresh = new()
        {
            HttpOnly = true,
            Secure = settings.RequireSecureCookies,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
        };

        cookies.Append(settings.AccessCookieName, accessToken.Value, access);
        cookies.Append(settings.RefreshCookieName, rawRefreshToken, refresh);
    }
}
