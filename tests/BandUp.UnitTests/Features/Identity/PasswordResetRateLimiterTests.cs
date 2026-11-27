using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class PasswordResetRateLimiterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly PasswordResetRateLimitKeys _keys = new(Options.Create(new PasswordResetSettings
    {
        CodeProtectionKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()),
    }));

    [Fact]
    public async Task AUTH_15_Request_Cooldown_And_Hourly_Email_Budget_Survive_Resends()
    {
        var limits = new InMemoryPasswordResetRateLimiter(_keys, _time);
        for (int index = 0; index < 5; index++)
        {
            (await limits.AcquireRequestAsync("User@example.com", "203.0.113.1", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
            (await limits.AcquireRequestAsync("user@EXAMPLE.COM", "203.0.113.2", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.AccountSuppressed);
            _time.Advance(TimeSpan.FromMinutes(1));
        }
        (await limits.AcquireRequestAsync("user@example.com", "203.0.113.3", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.AccountSuppressed);
        _time.Advance(TimeSpan.FromHours(1));
        (await limits.AcquireRequestAsync("user@example.com", "203.0.113.3", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
    }

    [Fact]
    public async Task AUTH_15_One_Concurrent_Request_Per_Email_Is_Admitted()
    {
        var limits = new InMemoryPasswordResetRateLimiter(_keys, _time);
        PasswordResetRateLimitDecision[] decisions = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(index => Task.Run(() => limits.AcquireRequestAsync("parallel@example.com", $"203.0.113.{index + 1}", CancellationToken.None))));
        decisions.Count(decision => decision == PasswordResetRateLimitDecision.Allowed).ShouldBe(1);
    }

    [Theory]
    [InlineData("203.0.113.4", "::ffff:203.0.113.4")]
    [InlineData(null, "invalid-ip")]
    public async Task AUTH_15_Equivalent_And_Unknown_Clients_Share_The_Same_Ip_Budget(string? firstIp, string? secondIp)
    {
        var limits = new InMemoryPasswordResetRateLimiter(_keys, _time);
        for (int index = 0; index < 20; index++)
        {
            (await limits.AcquireRequestAsync($"user{index}@example.com", firstIp, CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        }

        (await limits.AcquireRequestAsync("another@example.com", secondIp, CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.ClientRateLimited);
    }

    [Fact]
    public async Task AUTH_15_Verification_Budget_Is_Separate_From_Send_Budget_And_Resets_On_Utc_Window()
    {
        var limits = new InMemoryPasswordResetRateLimiter(_keys, _time);
        for (int index = 0; index < 20; index++)
        {
            (await limits.AcquireVerificationAsync("user@example.com", "203.0.113.1", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        }

        (await limits.AcquireVerificationAsync("USER@example.com", "203.0.113.2", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.AccountSuppressed);
        (await limits.AcquireRequestAsync("user@example.com", "203.0.113.1", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        _time.Advance(TimeSpan.FromMinutes(15));
        (await limits.AcquireVerificationAsync("user@example.com", "203.0.113.1", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
    }

    [Fact]
    public async Task AUTH_15_Verification_Ip_Budget_Is_Shared_Across_Emails()
    {
        var limits = new InMemoryPasswordResetRateLimiter(_keys, _time);
        for (int index = 0; index < 100; index++)
        {
            (await limits.AcquireVerificationAsync($"user{index}@example.com", "203.0.113.1", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        }

        (await limits.AcquireVerificationAsync("final@example.com", "203.0.113.1", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.ClientRateLimited);
    }

    [Fact]
    public void AUTH_15_Quota_Identifiers_Are_Opaque_Case_Normalized_And_Purpose_Separated()
    {
        _keys.Email("user@example.com").ShouldBe(_keys.Email("USER@example.com"));
        _keys.Email("user@example.com").ShouldMatch("^[0-9a-f]{64}$");
        _keys.Email("user@example.com").ShouldNotContain("user");
        _keys.ClientIp("203.0.113.1").ShouldBe(_keys.ClientIp("::ffff:203.0.113.1"));
        _keys.ClientIp(null).ShouldBe(_keys.ClientIp("not-an-ip"));
        _keys.Email("unknown").ShouldNotBe(_keys.ClientIp(null));
    }

    [Fact]
    public async Task AUTH_17_Reset_Budgets_Are_Independent_Of_Email_Code_Verification_And_Request_Budgets()
    {
        var limits = new InMemoryPasswordResetRateLimiter(_keys, _time);
        string hash = new('a', 64);
        for (int count = 0; count < 20; count++)
        {
            (await limits.AcquireResetAsync(hash, "203.0.113.7", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        }
        (await limits.AcquireResetAsync(hash, "203.0.113.8", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.AccountSuppressed);
        (await limits.AcquireVerificationAsync("user@example.com", "203.0.113.7", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        (await limits.AcquireRequestAsync("user@example.com", "203.0.113.7", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        for (int count = 0; count < 80; count++)
        {
            (await limits.AcquireResetAsync(Guid.CreateVersion7().ToString("N"), "203.0.113.7", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        }
        (await limits.AcquireResetAsync("another", "203.0.113.7", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.ClientRateLimited);
        _time.Advance(TimeSpan.FromMinutes(15));
        (await limits.AcquireResetAsync(hash, "203.0.113.7", CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        _keys.ResetToken(hash).ShouldNotBe(_keys.Email(hash));
    }
}
