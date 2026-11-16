using System.Net;
using System.Security.Claims;
using System.Text;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Logout;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog.Events;
using Shouldly;
using StackExchange.Redis;
using Role = BandUp.Domain.Identity.Role;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class LogoutTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task AUTH_10_Logout_Commits_Only_The_Presented_Session_And_Rejects_A_Copied_Access_Cookie()
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession caller = await SeedAsync(factory);
        SeededSession foreign = await SeedAsync(factory);
        RefreshToken otherDevice = NewToken(factory, caller.User.Id);
        await using (BandUpDbContext seed = CreateContext())
        {
            seed.RefreshTokens.Add(otherDevice);
            await seed.SaveChangesAsync();
        }

        (await GetMeStatusAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.OK);
        Result<LogoutOutcome> result = await DispatchLogoutAsync(factory, caller.Access.Value, caller.RawRefresh);

        result.IsSuccess.ShouldBeTrue();
        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(caller.Refresh.Id))!.RevokedAt.ShouldNotBeNull();
        (await verification.RefreshTokens.FindAsync(otherDevice.Id))!.RevokedAt.ShouldBeNull();
        (await verification.RefreshTokens.FindAsync(foreign.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await IsBlacklistedAsync(factory, caller.Access.TokenId)).ShouldBeTrue();
        (await GetMeStatusAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetMeStatusAsync(factory, foreign.Access.Value)).ShouldBe(HttpStatusCode.OK);

        Result<LogoutOutcome> repeated = await DispatchLogoutAsync(factory, caller.Access.Value, caller.RawRefresh);
        repeated.IsFailure.ShouldBeTrue();
        repeated.Error!.Code.ShouldBe(Error.UnauthorizedCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task AUTH_10_Logout_Still_Blacklists_The_Caller_With_An_Unusable_Refresh_Cookie(string scenario)
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession caller = await SeedAsync(factory);
        SeededSession foreign = await SeedAsync(factory);
        DateTimeOffset previouslyRevokedAt = caller.Refresh.CreatedAt.AddMinutes(1);
        await using (BandUpDbContext setup = CreateContext())
        {
            if (scenario == "revoked")
            {
                (await setup.RefreshTokens.FindAsync(caller.Refresh.Id))!.Revoke(previouslyRevokedAt);
            }
            else if (scenario == "expired")
            {
                setup.Entry((await setup.RefreshTokens.FindAsync(caller.Refresh.Id))!)
                    .Property(token => token.ExpiresAt).CurrentValue = caller.Refresh.CreatedAt.AddMinutes(1);
            }

            await setup.SaveChangesAsync();
        }

        string? presented = scenario switch
        {
            "missing" => null,
            "unknown" => "unknown-refresh-cookie",
            "foreign" => foreign.RawRefresh,
            _ => caller.RawRefresh,
        };
        (await DispatchLogoutAsync(factory, caller.Access.Value, presented)).IsSuccess.ShouldBeTrue();

        await using BandUpDbContext verification = CreateContext();
        RefreshToken saved = (await verification.RefreshTokens.FindAsync(caller.Refresh.Id))!;

        if (scenario == "revoked")
        {
            saved.RevokedAt.ShouldBe(previouslyRevokedAt);
        }
        else if (scenario == "expired")
        {
            saved.RevokedAt.ShouldNotBeNull();
        }
        else
        {
            saved.RevokedAt.ShouldBeNull();
        }

        (await verification.RefreshTokens.FindAsync(foreign.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await IsBlacklistedAsync(factory, caller.Access.TokenId)).ShouldBeTrue();
        (await GetMeStatusAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AUTH_10_Anonymous_Logout_Does_Not_Revoke_A_Presented_Refresh_Cookie()
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession caller = await SeedAsync(factory);

        Result<LogoutOutcome> result = await DispatchLogoutAsync(factory, null, caller.RawRefresh);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.UnauthorizedCode);
        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(caller.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await IsBlacklistedAsync(factory, caller.Access.TokenId)).ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_10_Redis_Write_Failure_Aborts_Logout_And_Rolls_Back_Refresh_Revocation()
    {
        CacheFault fault = new() { FailWrites = true };
        await using BandUpApiFactory factory = CreateFactory(fault: fault);
        SeededSession caller = await SeedAsync(factory);

        await Should.ThrowAsync<RedisConnectionException>(() =>
            DispatchLogoutAsync(factory, caller.Access.Value, caller.RawRefresh));

        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(caller.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await IsBlacklistedAsync(factory, caller.Access.TokenId)).ShouldBeFalse();
        (await GetMeStatusAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_10_Database_Failure_Rolls_Back_Refresh_But_Does_Not_Undo_The_Redis_Blacklist(bool failCommit)
    {
        await using BandUpApiFactory factory = CreateFactory(databaseFailure: failCommit ? "commit" : "save");
        SeededSession caller = await SeedAsync(factory);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            DispatchLogoutAsync(factory, caller.Access.Value, caller.RawRefresh));

        await using BandUpDbContext verification = CreateContext();
        (await verification.RefreshTokens.FindAsync(caller.Refresh.Id))!.RevokedAt.ShouldBeNull();
        (await IsBlacklistedAsync(factory, caller.Access.TokenId)).ShouldBeTrue();
        (await GetMeStatusAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AUTH_10_Redis_Read_Outage_Permits_A_Validated_Jwt_And_Logs_A_Warning()
    {
        CacheFault fault = new() { FailReads = true };
        TestLogSink logs = new();
        await using BandUpApiFactory factory = CreateFactory(fault: fault, logs: logs);
        SeededSession caller = await SeedAsync(factory);
        await factory.Services.GetRequiredService<IAccessTokenRevocationStore>()
            .RevokeAsync(caller.Access.TokenId, caller.Access.ExpiresAt, CancellationToken.None);

        (await GetMeStatusAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.OK);

        logs.Events.ShouldContain(log => log.Level == LogEventLevel.Warning &&
            log.MessageTemplate.Text.StartsWith("Access-token revocation lookup unavailable", StringComparison.Ordinal));
        int reads = fault.Reads;
        string forged = CreateJwt(factory, caller.User.Id, "bad-signature");
        (await GetMeStatusAsync(factory, forged)).ShouldBe(HttpStatusCode.Unauthorized);
        fault.Reads.ShouldBe(reads);
    }

    [Theory]
    [InlineData("bad-signature")]
    [InlineData("missing-sub")]
    [InlineData("bad-sub")]
    [InlineData("missing-jti")]
    [InlineData("bad-jti")]
    [InlineData("duplicate-jti")]
    [InlineData("missing-exp")]
    [InlineData("bad-exp")]
    [InlineData("expired")]
    public async Task AUTH_10_Invalid_Jwts_Are_Rejected_Before_The_Blacklist_Lookup(string scenario)
    {
        CacheFault fault = new() { FailReads = true };
        await using BandUpApiFactory factory = CreateFactory(fault: fault);
        SeededSession caller = await SeedAsync(factory);
        string token = CreateJwt(factory, caller.User.Id, scenario);

        (await GetMeStatusAsync(factory, token)).ShouldBe(HttpStatusCode.Unauthorized);

        fault.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task AUTH_10_Authorization_Header_Does_Not_Replace_The_Access_Cookie()
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession caller = await SeedAsync(factory);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new("Bearer", caller.Access.Value);
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AUTH_10_Logout_Blacklists_A_Jwt_Still_Accepted_Within_Clock_Skew()
    {
        await using BandUpApiFactory factory = CreateFactory();
        SeededSession caller = await SeedAsync(factory);
        AuthSettings settings = factory.Services.GetRequiredService<IOptions<AuthSettings>>().Value;
        AccessToken access = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(
            caller.User.Id, "User", TimeProvider.System.GetUtcNow().AddMinutes(-settings.AccessTokenMinutes).AddSeconds(-1));

        (await GetMeStatusAsync(factory, access.Value)).ShouldBe(HttpStatusCode.OK);
        (await DispatchLogoutAsync(factory, access.Value, null)).IsSuccess.ShouldBeTrue();
        (await IsBlacklistedAsync(factory, access.TokenId)).ShouldBeTrue();
        (await GetMeStatusAsync(factory, access.Value)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private BandUpApiFactory CreateFactory(CacheFault? fault = null, string? databaseFailure = null, TestLogSink? logs = null) =>
        new(environment, logSink: logs, configureServices: services =>
        {
            if (fault is not null)
            {
                services.AddSingleton<ICacheService>(provider => new FaultingCache(
                    new RedisCacheService(provider.GetRequiredService<IConnectionMultiplexer>()), fault));
            }

            if (databaseFailure is not null)
            {
                services.AddScoped<IUnitOfWork>(provider => new FailingUnitOfWork(
                    new EfUnitOfWork(provider.GetRequiredService<BandUpDbContext>()), databaseFailure));
            }
        });

    private async Task<SeededSession> SeedAsync(BandUpApiFactory factory)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        User user = User.Create($"logout-{Guid.CreateVersion7():N}@example.com", "Logout", "test-hash", now.AddDays(-1));
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        string raw = tokens.GenerateRefreshToken();
        RefreshToken refresh = RefreshToken.Issue(user.Id, tokens.HashRefreshToken(raw), now.AddDays(-1), now.AddDays(7), null);
        AccessToken access = tokens.CreateAccessToken(user.Id, Role.User.ToString(), now);
        await using BandUpDbContext context = CreateContext();
        context.Users.Add(user);
        context.RefreshTokens.Add(refresh);
        await context.SaveChangesAsync();
        return new SeededSession(user, access, raw, refresh);
    }

    private static RefreshToken NewToken(BandUpApiFactory factory, Guid userId)
    {
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return RefreshToken.Issue(userId, tokens.HashRefreshToken(tokens.GenerateRefreshToken()), now, now.AddDays(7), null);
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private static async Task<Result<LogoutOutcome>> DispatchLogoutAsync(BandUpApiFactory factory, string? access, string? refresh)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };

        if (access is not null)
        {
            context.Request.Headers.Cookie = "bu_at=" + access;
        }

        IHttpContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = context;

        try
        {
            AuthenticateResult authentication = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

            if (authentication.Succeeded)
            {
                context.User = authentication.Principal!;
            }

            return await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .SendAsync<LogoutCommand, LogoutOutcome>(new LogoutCommand(refresh), CancellationToken.None);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task<HttpStatusCode> GetMeStatusAsync(BandUpApiFactory factory, string access)
    {
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", "bu_at=" + access);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static Task<bool> IsBlacklistedAsync(BandUpApiFactory factory, Guid tokenId) =>
        factory.Services.GetRequiredService<IAccessTokenRevocationStore>().IsRevokedAsync(tokenId, CancellationToken.None);

    private static string CreateJwt(BandUpApiFactory factory, Guid userId, string scenario)
    {
        AuthSettings settings = factory.Services.GetRequiredService<IOptions<AuthSettings>>().Value;
        Dictionary<string, object> claims = new()
        {
            [IdentityClaimTypes.Subject] = userId.ToString(),
            [IdentityClaimTypes.JwtId] = Guid.CreateVersion7().ToString(),
            [IdentityClaimTypes.Role] = "User",
        };

        switch (scenario)
        {
            case "missing-sub": claims.Remove(IdentityClaimTypes.Subject); break;
            case "bad-sub": claims[IdentityClaimTypes.Subject] = "invalid-user-id"; break;
            case "missing-jti": claims.Remove(IdentityClaimTypes.JwtId); break;
            case "bad-jti": claims[IdentityClaimTypes.JwtId] = "invalid-jwt-id"; break;
            case "duplicate-jti": claims[IdentityClaimTypes.JwtId] = new[] { Guid.CreateVersion7().ToString(), Guid.CreateVersion7().ToString() }; break;
            case "bad-exp": claims[IdentityClaimTypes.Expiry] = "invalid-expiry"; break;
        }

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        string key = scenario == "bad-signature" ? new string('x', 48) : settings.SigningKey!;
        SecurityTokenDescriptor descriptor = new()
        {
            Claims = claims,
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            NotBefore = now.AddMinutes(-20).UtcDateTime,
            Expires = scenario is "missing-exp" or "bad-exp" ? null
                : scenario == "expired" ? now.AddMinutes(-5).UtcDateTime : now.AddMinutes(15).UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    private sealed record SeededSession(User User, AccessToken Access, string RawRefresh, RefreshToken Refresh);

    private sealed class CacheFault
    {
        public bool FailReads { get; init; }
        public bool FailWrites { get; init; }
        public int Reads;
    }

    private sealed class FaultingCache(ICacheService inner, CacheFault fault) : ICacheService
    {
        public Task<TValue?> GetAsync<TValue>(string key, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref fault.Reads);
            return fault.FailReads ? Task.FromException<TValue?>(Outage()) : inner.GetAsync<TValue>(key, cancellationToken);
        }

        public Task SetAsync<TValue>(string key, TValue value, TimeSpan? timeToLive, CancellationToken cancellationToken) =>
            fault.FailWrites ? Task.FromException(Outage()) : inner.SetAsync(key, value, timeToLive, cancellationToken);

        public Task RemoveAsync(string key, CancellationToken cancellationToken) => inner.RemoveAsync(key, cancellationToken);

        private static RedisConnectionException Outage() => new(
            ConnectionFailureType.UnableToConnect, CommandFlags.None, "test Redis outage", null, CommandStatus.Unknown);
    }

    private sealed class FailingUnitOfWork(IUnitOfWork inner, string failure) : IUnitOfWork
    {
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => inner.BeginTransactionAsync(cancellationToken);

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            int count = await inner.SaveChangesAsync(cancellationToken);

            if (failure == "save")
            {
                throw new InvalidOperationException("Test database failure after flushing logout revocation.");
            }

            return count;
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken) => failure == "commit"
            ? throw new InvalidOperationException("Test database commit failure.")
            : inner.CommitTransactionAsync(cancellationToken);

        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => inner.RollbackTransactionAsync(cancellationToken);
    }
}
