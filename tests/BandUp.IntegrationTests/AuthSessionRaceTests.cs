using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AuthSessionRaceTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task AUTH_09_Concurrent_Http_Refresh_Commits_One_Successor_Then_Revokes_All_Owned_Sessions()
    {
        RaceGate gate = new();
        await using BandUpApiFactory factory = CreateFactory(gate);
        Session caller = await SeedAsync(factory);
        Session foreign = await SeedAsync(factory);
        using HttpClient firstClient = Client(factory);
        using HttpClient secondClient = Client(factory);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using HttpRequestMessage firstRequest = Post("refresh", caller);
        using HttpRequestMessage secondRequest = Post("refresh", caller);

        (HttpResponseMessage first, HttpResponseMessage second) = await RaceAsync(
            firstClient, firstRequest, secondClient, secondRequest, gate, timeout.Token);
        using (first)
        using (second)
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            Cookies(first)["bu_rt"].Value.ToString().ShouldNotBeEmpty();
            Cookies(second).Values.ShouldAllBe(cookie => cookie.Value.ToString() == string.Empty);
            await using BandUpDbContext verification = Context();
            List<RefreshToken> owned = await verification.RefreshTokens.Where(token => token.UserId == caller.UserId).ToListAsync();
            owned.Count.ShouldBe(3); // Original, other device and exactly one successor.
            owned.ShouldAllBe(token => token.RevokedAt != null);
            (await verification.RefreshTokens.FindAsync(foreign.TokenId))!.RevokedAt.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_09_10_Refresh_And_Logout_Serialize_The_Presented_Credentials(bool refreshFirst)
    {
        RaceGate gate = new();
        await using BandUpApiFactory factory = CreateFactory(gate);
        Session caller = await SeedAsync(factory);
        Session foreign = await SeedAsync(factory);
        using HttpClient firstClient = Client(factory);
        using HttpClient secondClient = Client(factory);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using HttpRequestMessage firstRequest = Post(refreshFirst ? "refresh" : "logout", caller);
        using HttpRequestMessage secondRequest = Post(refreshFirst ? "logout" : "refresh", caller);

        (HttpResponseMessage first, HttpResponseMessage second) = await RaceAsync(
            firstClient, firstRequest, secondClient, secondRequest, gate, timeout.Token);
        using (first)
        using (second)
        {
            await using BandUpDbContext verification = Context();
            List<RefreshToken> owned = await verification.RefreshTokens.Where(token => token.UserId == caller.UserId).ToListAsync();
            (await verification.RefreshTokens.FindAsync(foreign.TokenId))!.RevokedAt.ShouldBeNull();
            (await factory.Services.GetRequiredService<IAccessTokenRevocationStore>()
                .IsRevokedAsync(caller.Access.TokenId, CancellationToken.None)).ShouldBeTrue();

            if (refreshFirst)
            {
                first.StatusCode.ShouldBe(HttpStatusCode.OK);
                second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                owned.Count.ShouldBe(3);
                owned.Single(token => token.Id == caller.TokenId).RevokedAt.ShouldNotBeNull();
                // Logout targets the old presented refresh row, not its successor or other devices.
                owned.Where(token => token.Id != caller.TokenId).ShouldAllBe(token => token.RevokedAt == null);
                string newAccess = Cookies(first)["bu_at"].Value.ToString();
                (await GetMeAsync(factory, newAccess)).ShouldBe(HttpStatusCode.OK);
            }
            else
            {
                first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                owned.Count.ShouldBe(2);
                owned.ShouldAllBe(token => token.RevokedAt != null);
            }

            (await GetMeAsync(factory, caller.Access.Value)).ShouldBe(HttpStatusCode.Unauthorized);
            (await GetMeAsync(factory, foreign.Access.Value)).ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AUTH_09_Http_Refresh_For_Another_User_Does_Not_Wait_For_The_First_User()
    {
        RaceGate gate = new();
        await using BandUpApiFactory factory = CreateFactory(gate);
        Session firstUser = await SeedAsync(factory);
        Session secondUser = await SeedAsync(factory);
        using HttpClient firstClient = Client(factory);
        using HttpClient secondClient = Client(factory);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using HttpRequestMessage firstRequest = Post("refresh", firstUser);
        using HttpRequestMessage secondRequest = Post("refresh", secondUser);
        Task<HttpResponseMessage> first = firstClient.SendAsync(firstRequest, timeout.Token);

        try
        {
            await gate.FirstLocked.Task.WaitAsync(timeout.Token);
            using HttpResponseMessage independent = await secondClient.SendAsync(secondRequest, timeout.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
            independent.StatusCode.ShouldBe(HttpStatusCode.OK);
            first.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            gate.ReleaseFirst.TrySetResult();
        }

        using HttpResponseMessage released = await first.WaitAsync(timeout.Token);
        released.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RaceAsync(
        HttpClient firstClient, HttpRequestMessage firstRequest,
        HttpClient secondClient, HttpRequestMessage secondRequest,
        RaceGate gate, CancellationToken cancellationToken)
    {
        Task<HttpResponseMessage> first = firstClient.SendAsync(firstRequest, cancellationToken);
        Task<HttpResponseMessage>? second = null;

        try
        {
            await gate.FirstLocked.Task.WaitAsync(cancellationToken);
            second = secondClient.SendAsync(secondRequest, cancellationToken);
            int processId = await gate.SecondStarted.Task.WaitAsync(cancellationToken);
            await WaitForLockAsync(processId, second, cancellationToken);
        }
        finally
        {
            gate.ReleaseFirst.TrySetResult();
        }

        HttpResponseMessage[] responses = await Task.WhenAll(first, second!).WaitAsync(cancellationToken);
        return (responses[0], responses[1]);
    }

    private async Task WaitForLockAsync(int processId, Task<HttpResponseMessage> operation, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection observer = new(environment.MigratedConnectionString);
        await observer.OpenAsync(cancellationToken);
        await using NpgsqlCommand command = observer.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1 AND wait_event_type = 'Lock')";
        command.Parameters.AddWithValue(processId);
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (operation.IsCompleted)
            {
                using HttpResponseMessage response = await operation;
                throw new InvalidOperationException($"Second request completed before lock release: {response.StatusCode}.");
            }

            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("PostgreSQL did not report the expected HTTP session lock wait.");
    }

    private BandUpApiFactory CreateFactory(RaceGate gate) => new(environment, configureServices: services =>
        services.AddScoped<IRefreshTokenStore>(provider =>
        {
            BandUpDbContext context = provider.GetRequiredService<BandUpDbContext>();
            return new GatedStore(new EfRefreshTokenStore(context), context, gate);
        }));

    private async Task<Session> SeedAsync(BandUpApiFactory factory)
    {
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"http-race-{Guid.CreateVersion7():N}@example.com", "HTTP race", "test-hash", now);
        string raw = tokens.GenerateRefreshToken();
        RefreshToken token = RefreshToken.Issue(user.Id, tokens.HashRefreshToken(raw), now, now.AddDays(7), null);
        RefreshToken otherDevice = RefreshToken.Issue(user.Id, tokens.HashRefreshToken(tokens.GenerateRefreshToken()), now, now.AddDays(7), null);
        await using BandUpDbContext context = Context();
        context.Users.Add(user);
        context.RefreshTokens.AddRange(token, otherDevice);
        await context.SaveChangesAsync();
        return new Session(user.Id, token.Id, raw, tokens.CreateAccessToken(user.Id, "User", now));
    }

    private BandUpDbContext Context() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
    private static HttpClient Client(BandUpApiFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    private static HttpRequestMessage Post(string operation, Session session)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/" + operation) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Cookie", "bu_at=" + session.Access.Value + "; bu_rt=" + session.RawRefresh);
        request.Headers.Add("X-Requested-With", "bandup");
        return request;
    }

    private static Dictionary<string, SetCookieHeaderValue> Cookies(HttpResponseMessage response) => response.Headers
        .GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value)).ToDictionary(cookie => cookie.Name.ToString());

    private static async Task<HttpStatusCode> GetMeAsync(BandUpApiFactory factory, string access)
    {
        using HttpClient client = Client(factory);
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", "bu_at=" + access);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private sealed record Session(Guid UserId, Guid TokenId, string RawRefresh, AccessToken Access);
    private sealed class RaceGate
    {
        private int _calls;
        public TaskCompletionSource FirstLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Enter(BandUpDbContext context)
        {
            int call = Interlocked.Increment(ref _calls);

            if (call == 2)
            {
                SecondStarted.TrySetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
            }

            return call;
        }

        public async Task AfterLockAsync(int call, CancellationToken cancellationToken)
        {
            if (call == 1)
            {
                FirstLocked.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class GatedStore(IRefreshTokenStore inner, BandUpDbContext context, RaceGate gate) : IRefreshTokenStore
    {
        public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) => inner.FindByTokenHashAsync(tokenHash, cancellationToken);

        public async Task<RefreshSession?> FindSessionForUpdateAsync(string tokenHash, CancellationToken cancellationToken)
        {
            int call = gate.Enter(context);
            RefreshSession? session = await inner.FindSessionForUpdateAsync(tokenHash, cancellationToken);
            await gate.AfterLockAsync(call, cancellationToken);
            return session;
        }

        public Task<IReadOnlyList<RefreshToken>> ListUsableForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.ListUsableForUserAsync(userId, cancellationToken);
        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken) => inner.AddAsync(token, cancellationToken);
        public Task RevokeAllForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken cancellationToken) => inner.RevokeAllForUserAsync(userId, revokedAt, cancellationToken);

        public async Task RevokeForUserAsync(Guid userId, string tokenHash, DateTimeOffset revokedAt, CancellationToken cancellationToken)
        {
            int call = gate.Enter(context);
            await inner.RevokeForUserAsync(userId, tokenHash, revokedAt, cancellationToken);
            await gate.AfterLockAsync(call, cancellationToken);
        }
    }
}
