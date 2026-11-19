using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;
using Shouldly;
using StackExchange.Redis;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AuthSessionHttpTests(BandUpTestEnvironment environment)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AUTH_09_10_Login_Refresh_Logout_Manage_The_Cookie_Jar_And_Token_Expiry(bool secure, bool customNames)
    {
        await using BandUpApiFactory factory = CreateFactory(secure: secure, customNames: customNames);
        AuthSettings settings = Settings(factory);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        string email = $"session-http-{Guid.CreateVersion7():N}@example.com";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Localdev123!",
            displayName = "Session HTTP",
            captchaToken = "fake-captcha-token",
        });
        registration.StatusCode.ShouldBe(HttpStatusCode.Created);
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Localdev123!",
            captchaToken = "fake-captcha-token",
        });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        Dictionary<string, SetCookieHeaderValue> initial = Cookies(login);
        await AssertIssuedCookiesAsync(factory, initial, secure);
        string loginJson = await login.Content.ReadAsStringAsync();
        loginJson.ShouldNotContain(initial[settings.AccessCookieName].Value.ToString());
        loginJson.ShouldNotContain(initial[settings.RefreshCookieName].Value.ToString());
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refresh.Content.ReadAsStringAsync()).ShouldBeEmpty();
        Dictionary<string, SetCookieHeaderValue> renewed = Cookies(refresh);
        await AssertIssuedCookiesAsync(factory, renewed, secure);
        renewed[settings.AccessCookieName].Value.ToString().ShouldNotBe(initial[settings.AccessCookieName].Value.ToString());
        renewed[settings.RefreshCookieName].Value.ToString().ShouldNotBe(initial[settings.RefreshCookieName].Value.ToString());
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage logout = await client.PostAsJsonAsync("/api/auth/logout", new { });
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await logout.Content.ReadAsStringAsync()).ShouldBeEmpty();
        AssertDeletedCookies(logout, settings, secure);
        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using HttpClient copied = ManualClient(factory, secure);
        using HttpRequestMessage me = new(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", $"{settings.AccessCookieName}={renewed[settings.AccessCookieName].Value}");
        using HttpResponseMessage denied = await copied.SendAsync(me);
        await AssertProblemAsync(denied, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("blacklisted")]
    [InlineData("malformed")]
    public async Task AUTH_09_Refresh_Works_Without_A_Valid_Access_Token(string accessState)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession session = await SeedAsync(factory);
        string? access = session.Access.Value;

        switch (accessState)
        {
            case "missing": access = null; break;
            case "expired": access = ExpiredAccess(factory, session.User.Id).Value; break;
            case "malformed": access = "not-a-jwt"; break;
            case "blacklisted":
                await factory.Services.GetRequiredService<IAccessTokenRevocationStore>()
                    .RevokeAsync(session.Access.TokenId, session.Access.ExpiresAt, CancellationToken.None);
                break;
        }

        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, "/api/auth/refresh", access, session.RawRefresh);
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        await AssertIssuedCookiesAsync(factory, Cookies(response), secure: false);
        await using BandUpDbContext verification = CreateContext();
        RefreshToken old = (await verification.RefreshTokens.FindAsync(session.Refresh.Id))!;
        old.RevokedAt.ShouldNotBeNull();
        old.ReplacedById.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("missing-all")]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("expired-revoked")]
    [InlineData("inactive")]
    [InlineData("body-token")]
    [InlineData("query-token")]
    public async Task AUTH_09_Refresh_Rejection_Clears_Cookies_And_Maps_Reuse_After_Commit(string scenario)
    {
        await using BandUpApiFactory factory = CreateFactory();
        bool reuse = scenario is "revoked" or "expired-revoked";
        SeededSession session = await SeedAsync(factory,
            expired: scenario is "expired" or "expired-revoked", revoked: reuse, inactive: scenario == "inactive");
        SeededSession foreign = await SeedAsync(factory);
        RefreshToken otherDevice = NewToken(factory, session.User.Id);
        await using (BandUpDbContext seed = CreateContext())
        {
            seed.RefreshTokens.Add(otherDevice);
            await seed.SaveChangesAsync();
        }

        string? presented = scenario switch
        {
            "missing" or "missing-all" or "body-token" or "query-token" => null,
            "unknown" => "unknown-refresh-cookie",
            _ => session.RawRefresh,
        };
        string url = "/api/auth/refresh" + (scenario == "query-token" ? "?refreshToken=" + session.RawRefresh : string.Empty);
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, url, scenario == "missing-all" ? null : session.Access.Value, presented);

        if (scenario == "body-token")
        {
            request.Content = JsonContent.Create(new { refreshToken = session.RawRefresh });
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
        AssertDeletedCookies(response, Settings(factory), secure: false);
        await using BandUpDbContext verification = CreateContext();
        List<RefreshToken> owned = await verification.RefreshTokens.Where(token => token.UserId == session.User.Id).ToListAsync();
        owned.Count.ShouldBe(2);
        owned.Single(token => token.Id == session.Refresh.Id).RevokedAt.ShouldBe(session.Refresh.RevokedAt);
        owned.Single(token => token.Id == otherDevice.Id).RevokedAt.HasValue.ShouldBe(reuse);
        (await verification.RefreshTokens.FindAsync(foreign.Refresh.Id))!.RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("blacklisted")]
    [InlineData("header-only")]
    [InlineData("malformed")]
    public async Task AUTH_10_Logout_Requires_Authentication_And_Does_Not_Clear_Cookies_On_Rejection(string scenario)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession session = await SeedAsync(factory);
        string? access = scenario switch
        {
            "missing" or "header-only" => null,
            "expired" => ExpiredAccess(factory, session.User.Id).Value,
            "malformed" => "not-a-jwt",
            _ => session.Access.Value,
        };

        if (scenario == "blacklisted")
        {
            await factory.Services.GetRequiredService<IAccessTokenRevocationStore>()
                .RevokeAsync(session.Access.TokenId, session.Access.ExpiresAt, CancellationToken.None);
        }

        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, "/api/auth/logout", access, session.RawRefresh);

        if (scenario == "header-only")
        {
            request.Headers.Authorization = new("Bearer", session.Access.Value);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(session.Refresh.Id))!.RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_10_Logout_Deletes_Cookies_With_Missing_Or_Foreign_Refresh_Credentials(bool foreignCookie)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession session = await SeedAsync(factory);
        SeededSession foreign = await SeedAsync(factory);
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, "/api/auth/logout", session.Access.Value, foreignCookie ? foreign.RawRefresh : null);
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        AssertDeletedCookies(response, Settings(factory), secure: false);
        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(session.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await verification.RefreshTokens.FindAsync(foreign.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await factory.Services.GetRequiredService<IAccessTokenRevocationStore>()
            .IsRevokedAsync(session.Access.TokenId, CancellationToken.None)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("refresh", "header")]
    [InlineData("refresh", "json")]
    [InlineData("logout", "header")]
    [InlineData("logout", "json")]
    public async Task AUTH_09_10_Csrf_Rejection_Does_Not_Change_The_Session_Or_Cookies(string operation, string missing)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession session = await SeedAsync(factory);
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, "/api/auth/" + operation, session.Access.Value, session.RawRefresh);

        if (missing == "header")
        {
            request.Headers.Remove("X-Requested-With");
        }
        else
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "text/plain");
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(session.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await factory.Services.GetRequiredService<IAccessTokenRevocationStore>()
            .IsRevokedAsync(session.Access.TokenId, CancellationToken.None)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("refresh", "commit")]
    [InlineData("logout", "commit")]
    [InlineData("logout", "redis")]
    public async Task AUTH_09_10_Infrastructure_Failures_Do_Not_Write_Replacement_Or_Deletion_Cookies(string operation, string failure)
    {
        await using BandUpApiFactory factory = CreateFactory(failure: failure);
        SeededSession session = await SeedAsync(factory);
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, "/api/auth/" + operation, session.Access.Value, session.RawRefresh);
        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        await using BandUpDbContext verification = CreateContext();
        RefreshToken saved = (await verification.RefreshTokens.FindAsync(session.Refresh.Id))!;
        saved.RevokedAt.ShouldBeNull();
        saved.ReplacedById.ShouldBeNull();
        (await verification.RefreshTokens.CountAsync(token => token.UserId == session.User.Id)).ShouldBe(1);
    }

    [Theory]
    [InlineData("refresh", false)]
    [InlineData("refresh", true)]
    [InlineData("logout", false)]
    [InlineData("logout", true)]
    public async Task AUTH_09_10_Malformed_Or_Missing_Json_Is_Rejected_Before_Session_Mutations(string operation, bool missing)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession session = await SeedAsync(factory);
        using HttpClient client = ManualClient(factory);
        using HttpRequestMessage request = Post(factory, "/api/auth/" + operation, session.Access.Value, session.RawRefresh);
        request.Content = new StringContent(missing ? string.Empty : "{", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(session.Refresh.Id))!.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_09_10_OpenApi_Describes_Empty_Json_Requests_And_Empty_Success_Responses()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ManualClient(factory);
        using HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach ((string operation, string success) in new[] { ("refresh", "200"), ("logout", "204") })
        {
            JsonElement post = paths.GetProperty("/api/auth/" + operation).GetProperty("post");
            post.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _).ShouldBeTrue();
            post.GetProperty("requestBody").GetProperty("required").GetBoolean().ShouldBeTrue();
            post.GetProperty("description").GetString()!.ShouldContain("X-Requested-With: bandup");
            JsonElement responses = post.GetProperty("responses");
            responses.GetProperty(success).TryGetProperty("content", out _).ShouldBeFalse();
            responses.GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();
            responses.TryGetProperty("400", out _).ShouldBeTrue();
            responses.TryGetProperty("500", out _).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task AUTH_09_10_Swagger_Sends_The_Csrf_Helper_Only_In_Development(string environmentName, bool helperExpected)
    {
        await using BandUpApiFactory factory = CreateFactory(environmentName: environmentName);
        using HttpClient client = ManualClient(factory);
        string initialization = await client.GetStringAsync("/swagger/index.js");

        initialization.Contains("X-Requested-With", StringComparison.Ordinal).ShouldBe(helperExpected);
    }

    [Fact]
    public async Task AUTH_09_10_Swagger_Page_And_Its_Scripts_Load_From_The_Api_Without_A_Frontend()
    {
        await using BandUpApiFactory factory = CreateFactory();
        using HttpClient client = ManualClient(factory);
        string html = await client.GetStringAsync("/swagger/index.html");
        html.ShouldContain("id=\"swagger-ui\"");
        string[] scripts = html.Split("<script src=\"", StringSplitOptions.None).Skip(1)
            .Select(part => part.Split('"')[0]).ToArray();
        scripts.Length.ShouldBeGreaterThanOrEqualTo(3);
        Uri page = new("http://localhost/swagger/index.html");

        foreach (string source in scripts)
        {
            Uri url = new(page, source);
            url.Host.ShouldBe(page.Host);
            using HttpResponseMessage script = await client.GetAsync(url);
            script.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await script.Content.ReadAsStringAsync()).ShouldNotBeEmpty();
        }
    }

    private BandUpApiFactory CreateFactory(bool secure = false, bool customNames = false, string? failure = null, string environmentName = "Development") =>
        new(environment, environmentName: environmentName, overrides: new()
        {
            ["Auth:RequireSecureCookies"] = secure.ToString(),
            ["Auth:AccessTokenMinutes"] = "5",
            ["Auth:RefreshTokenDays"] = "3",
            ["Auth:AccessCookieName"] = customNames ? "local_at" : "bu_at",
            ["Auth:RefreshCookieName"] = customNames ? "local_rt" : "bu_rt",
        }, configureServices: services =>
        {
            if (failure == "commit")
            {
                services.AddScoped<IUnitOfWork>(provider => new FailingCommitUnitOfWork(
                    new EfUnitOfWork(provider.GetRequiredService<BandUpDbContext>())));
            }
            else if (failure == "redis")
            {
                services.AddSingleton<ICacheService>(provider => new FailingWriteCache(
                    new RedisCacheService(provider.GetRequiredService<IConnectionMultiplexer>())));
            }
        });

    private async Task<SeededSession> SeedAsync(BandUpApiFactory factory, bool expired = false, bool revoked = false, bool inactive = false)
    {
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        User user = User.Create($"session-seed-{Guid.CreateVersion7():N}@example.com", "Session", "test-hash", now.AddDays(-1));
        string raw = tokens.GenerateRefreshToken();
        RefreshToken refresh = RefreshToken.Issue(user.Id, tokens.HashRefreshToken(raw), now.AddDays(-1), expired ? now.AddMinutes(-1) : now.AddDays(3), null);

        if (revoked)
        {
            refresh.Revoke(now.AddMinutes(-2));
        }

        if (inactive)
        {
            user.Deactivate();
        }

        await using BandUpDbContext context = CreateContext();
        context.Users.Add(user);
        context.RefreshTokens.Add(refresh);
        await context.SaveChangesAsync();
        return new SeededSession(user, tokens.CreateAccessToken(user.Id, "User", now), raw, refresh);
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
    private static AuthSettings Settings(BandUpApiFactory factory) => factory.Services.GetRequiredService<IOptions<AuthSettings>>().Value;
    private static HttpClient ManualClient(BandUpApiFactory factory, bool secure = false) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
    });

    private static HttpRequestMessage Post(BandUpApiFactory factory, string path, string? access, string? refresh)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path) { Content = JsonContent.Create(new { }) };
        AuthSettings settings = Settings(factory);
        List<string> cookies = [];
        if (access is not null)
        {
            cookies.Add(settings.AccessCookieName + "=" + access);
        }

        if (refresh is not null)
        {
            cookies.Add(settings.RefreshCookieName + "=" + refresh);
        }

        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies));
        }
        request.Headers.Add("X-Requested-With", "bandup");
        request.Headers.Add("X-Request-Id", "session-" + Guid.CreateVersion7());
        return request;
    }

    private static AccessToken ExpiredAccess(BandUpApiFactory factory, Guid userId) => factory.Services.GetRequiredService<ITokenService>()
        .CreateAccessToken(userId, "User", TimeProvider.System.GetUtcNow().AddMinutes(-Settings(factory).AccessTokenMinutes - 2));

    private static RefreshToken NewToken(BandUpApiFactory factory, Guid userId)
    {
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return RefreshToken.Issue(userId, tokens.HashRefreshToken(tokens.GenerateRefreshToken()), now, now.AddDays(3), null);
    }

    private static Dictionary<string, SetCookieHeaderValue> Cookies(HttpResponseMessage response) => response.Headers
        .GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
        .ToDictionary(cookie => cookie.Name.ToString(), StringComparer.Ordinal);

    private async Task AssertIssuedCookiesAsync(BandUpApiFactory factory, Dictionary<string, SetCookieHeaderValue> cookies, bool secure)
    {
        AuthSettings settings = Settings(factory);
        cookies.Count.ShouldBe(2);
        AssertPolicy(cookies[settings.AccessCookieName], secure);
        AssertPolicy(cookies[settings.RefreshCookieName], secure);
        JsonWebToken access = new JsonWebTokenHandler().ReadJsonWebToken(cookies[settings.AccessCookieName].Value.ToString());
        long expiresAt = long.Parse(access.GetClaim(IdentityClaimTypes.Expiry).Value, CultureInfo.InvariantCulture);
        cookies[settings.AccessCookieName].Expires!.Value.ToUnixTimeSeconds().ShouldBe(expiresAt);
        string hash = factory.Services.GetRequiredService<ITokenService>().HashRefreshToken(cookies[settings.RefreshCookieName].Value.ToString());
        await using BandUpDbContext verification = CreateContext();
        RefreshToken row = await verification.RefreshTokens.SingleAsync(token => token.TokenHash == hash);
        cookies[settings.RefreshCookieName].Expires!.Value.ToUnixTimeSeconds().ShouldBe(row.ExpiresAt.ToUnixTimeSeconds());
        (row.ExpiresAt - row.CreatedAt).ShouldBe(TimeSpan.FromDays(settings.RefreshTokenDays));
    }

    private static void AssertDeletedCookies(HttpResponseMessage response, AuthSettings settings, bool secure)
    {
        Dictionary<string, SetCookieHeaderValue> cookies = Cookies(response);
        cookies.Count.ShouldBe(2);
        foreach (string name in new[] { settings.AccessCookieName, settings.RefreshCookieName })
        {
            SetCookieHeaderValue cookie = cookies[name];
            AssertPolicy(cookie, secure);
            cookie.Value.ToString().ShouldBeEmpty();
            cookie.Expires!.Value.ShouldBeLessThanOrEqualTo(DateTimeOffset.UnixEpoch);
        }
    }

    private static void AssertPolicy(SetCookieHeaderValue cookie, bool secure)
    {
        cookie.Path.ToString().ShouldBe("/");
        cookie.Domain.HasValue.ShouldBeFalse();
        cookie.HttpOnly.ShouldBeTrue();
        cookie.Secure.ShouldBe(secure);
        cookie.SameSite.ToString().ShouldBe("Strict");
        cookie.Expires.ShouldNotBeNull();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        string requestId = response.Headers.GetValues("X-Request-Id").Single();
        json.RootElement.GetProperty("traceId").GetString().ShouldBe(requestId);
    }

    private sealed record SeededSession(User User, AccessToken Access, string RawRefresh, RefreshToken Refresh);

    private sealed class FailingCommitUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => inner.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => inner.BeginTransactionAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Test commit failure.");
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => inner.RollbackTransactionAsync(cancellationToken);
    }

    private sealed class FailingWriteCache(ICacheService inner) : ICacheService
    {
        public Task<TValue?> GetAsync<TValue>(string key, CancellationToken cancellationToken) => inner.GetAsync<TValue>(key, cancellationToken);
        public Task SetAsync<TValue>(string key, TValue value, TimeSpan? timeToLive, CancellationToken cancellationToken) =>
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Test Redis write failure.", null, CommandStatus.Unknown);
        public Task RemoveAsync(string key, CancellationToken cancellationToken) => inner.RemoveAsync(key, cancellationToken);
    }
}
