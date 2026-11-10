using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

/// <summary>
/// AUTH-05: when the CAPTCHA provider is unreachable, a client gets a small number of
/// attempts per window, then the request is refused. An invalid token from a healthy provider
/// must never be forgiven — only unreachability triggers the fallback.
/// </summary>
public sealed class CaptchaFallbackTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingCache _cache;

    public CaptchaFallbackTests()
    {
        _cache = new RecordingCache(_time);
    }

    private OutageTolerantCaptchaVerifier Create(int maxAttempts = 3)
    {
        CaptchaSettings settings = new()
        {
            FallbackWindowMinutes = 60,
            FallbackMaxAttempts = maxAttempts,
        };

        return new OutageTolerantCaptchaVerifier(
            new UnreachableVerifier(),
            _cache,
            settings,
            NullLogger<OutageTolerantCaptchaVerifier>.Instance);
    }

    [Fact]
    public async Task The_First_Outage_Attempt_Is_Allowed()
    {
        CaptchaOutcome outcome = await Create().VerifyAsync("token", "1.2.3.4", CancellationToken.None);

        outcome.ShouldBe(CaptchaOutcome.Valid);
    }

    [Fact]
    public async Task Attempts_Below_The_Limit_Are_Allowed_Then_The_Limit_Rejects()
    {
        OutageTolerantCaptchaVerifier verifier = Create(maxAttempts: 3);

        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.ProviderUnavailable);
    }

    [Fact]
    public async Task Attempts_Are_Counted_Per_Ip()
    {
        OutageTolerantCaptchaVerifier verifier = Create(maxAttempts: 1);

        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.ProviderUnavailable);
        (await verifier.VerifyAsync("t", "5.6.7.8", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
    }

    [Fact]
    public async Task The_Window_Expires()
    {
        OutageTolerantCaptchaVerifier verifier = Create(maxAttempts: 1);

        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.ProviderUnavailable);

        _time.Advance(Window + TimeSpan.FromSeconds(1));

        (await verifier.VerifyAsync("t", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
    }

    [Fact]
    public async Task An_Invalid_Token_From_A_Healthy_Provider_Is_Never_Forgiven()
    {
        OutageTolerantCaptchaVerifier verifier = new(
            new RejectingVerifier(),
            _cache,
            new CaptchaSettings(),
            NullLogger<OutageTolerantCaptchaVerifier>.Instance);

        (await verifier.VerifyAsync("bad", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.InvalidToken);

        // A string of invalid attempts must not consume, or be excused by, the outage budget.
        _cache.Keys.ShouldNotContain(key => key.Contains("captcha"));
    }

    [Fact]
    public async Task A_Valid_Token_Passes_Straight_Through()
    {
        OutageTolerantCaptchaVerifier verifier = new(
            new AcceptingVerifier(),
            _cache,
            new CaptchaSettings(),
            NullLogger<OutageTolerantCaptchaVerifier>.Instance);

        (await verifier.VerifyAsync("good", "1.2.3.4", CancellationToken.None)).ShouldBe(CaptchaOutcome.Valid);
        _cache.Keys.ShouldNotContain(key => key.Contains("captcha"));
    }

    /// <summary>Stands in for a provider whose siteverify endpoint cannot be reached.</summary>
    private sealed class UnreachableVerifier : ICaptchaVerifier
    {
        public Task<CaptchaOutcome> VerifyAsync(string? token, string? clientIp, CancellationToken ct) =>
            Task.FromResult(CaptchaOutcome.ProviderUnavailable);
    }

    private sealed class RejectingVerifier : ICaptchaVerifier
    {
        public Task<CaptchaOutcome> VerifyAsync(string? token, string? clientIp, CancellationToken ct) =>
            Task.FromResult(CaptchaOutcome.InvalidToken);
    }

    private sealed class AcceptingVerifier : ICaptchaVerifier
    {
        public Task<CaptchaOutcome> VerifyAsync(string? token, string? clientIp, CancellationToken ct) =>
            Task.FromResult(CaptchaOutcome.Valid);
    }

    /// <summary>Observes what the decorator writes, and behaves like the in-memory cache.</summary>
    private sealed class RecordingCache(TimeProvider timeProvider) : ICacheService
    {
        private readonly InMemoryCacheService _inner = new(timeProvider);

        public List<string> Keys { get; } = [];

        public Task<TValue?> GetAsync<TValue>(string key, CancellationToken ct)
        {
            Keys.Add(key);
            return _inner.GetAsync<TValue>(key, ct);
        }

        public Task SetAsync<TValue>(string key, TValue value, TimeSpan? ttl, CancellationToken ct)
        {
            Keys.Add(key);
            return _inner.SetAsync(key, value, ttl, ct);
        }

        public Task RemoveAsync(string key, CancellationToken ct) => _inner.RemoveAsync(key, ct);
    }
}
