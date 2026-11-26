using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace BandUp.Api.Auth;

/// <summary>
/// Owns cookie names, issuance and deletion for login, refresh, logout and password recovery. Deletion uses the
/// same host/path scope and security policy as issuance.
/// </summary>
public sealed class AuthCookieService(IOptions<AuthSettings> options)
{
    public string? ReadRefreshToken(HttpRequest request) => request.Cookies[options.Value.RefreshCookieName];

    public void SetSessionCookies(
        IResponseCookies cookies,
        AccessToken accessToken,
        string rawRefreshToken,
        DateTimeOffset refreshTokenExpiresAt)
    {
        AuthSettings settings = options.Value;

        cookies.Append(settings.AccessCookieName, accessToken.Value, CreateOptions(accessToken.ExpiresAt));
        cookies.Append(settings.RefreshCookieName, rawRefreshToken, CreateOptions(refreshTokenExpiresAt));
    }

    public void ClearSessionCookies(IResponseCookies cookies)
    {
        AuthSettings settings = options.Value;
        cookies.Delete(settings.AccessCookieName, CreateOptions());
        cookies.Delete(settings.RefreshCookieName, CreateOptions());
    }

    private CookieOptions CreateOptions(DateTimeOffset? expiresAt = null) => new()
    {
        Path = "/",
        HttpOnly = true,
        Secure = options.Value.RequireSecureCookies,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Expires = expiresAt,
    };
}
