using System.Net;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Security;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class OtpLoginQuotaTests(BandUpTestEnvironment environment)
{
    private static OtpLoginRateLimitKeys NewKeys() => new(Options.Create(new OtpLoginSettings
    {
        CodeProtectionKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()),
    }));

    [Fact]
    public async Task AUTH_16_Redis_Atomically_Admits_One_Concurrent_Send_Per_Email_Across_Instances()
    {
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(environment.RedisConnectionString);
        OtpLoginRateLimitKeys keys = NewKeys();
        var first = new RedisOtpLoginRateLimiter(connection, keys);
        var second = new RedisOtpLoginRateLimiter(connection, keys);
        string email = $"quota-{Guid.CreateVersion7():N}@example.com";
        var decisions = await Task.WhenAll(Enumerable.Range(0, 40).Select(index =>
            (index % 2 == 0 ? first : second).AcquireRequestAsync(index % 2 == 0 ? email : email.ToUpperInvariant(), NewIp(), CancellationToken.None)));
        decisions.Count(decision => decision == OtpLoginRateLimitDecision.Allowed).ShouldBe(1);
        decisions.Count(decision => decision == OtpLoginRateLimitDecision.AccountSuppressed).ShouldBe(39);
        TimeSpan? ttl = await connection.GetDatabase().KeyTimeToLiveAsync($"bandup:{{otp-login}}:request:cooldown:{keys.Email(email)}");
        ttl.ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
        ttl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        string redisKeys = string.Join(" ", connection.GetServer(connection.GetEndPoints()[0]).Keys(pattern: "bandup:{otp-login}:*"));
        redisKeys.ShouldNotContain(email);
        redisKeys.ShouldNotContain("@example.com");
    }

    [Fact]
    public async Task AUTH_16_Redis_Ip_Limit_Cannot_Be_Exceeded_By_Concurrent_Different_Emails()
    {
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(environment.RedisConnectionString);
        var limits = new RedisOtpLoginRateLimiter(connection, NewKeys());
        string ip = NewIp();
        var decisions = await Task.WhenAll(Enumerable.Range(0, 40).Select(index =>
            limits.AcquireRequestAsync($"{Guid.CreateVersion7():N}@example.com", ip, CancellationToken.None)));
        decisions.Count(decision => decision == OtpLoginRateLimitDecision.Allowed).ShouldBe(20);
        decisions.Count(decision => decision == OtpLoginRateLimitDecision.ClientRateLimited).ShouldBe(20);
    }

    [Fact]
    public async Task AUTH_16_Redis_Email_Request_Budget_Remains_After_Cooldown_Keys_Expire()
    {
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(environment.RedisConnectionString);
        OtpLoginRateLimitKeys keys = NewKeys();
        var limits = new RedisOtpLoginRateLimiter(connection, keys);
        string email = $"quota-{Guid.CreateVersion7():N}@example.com";
        string ip = NewIp();
        for (int index = 0; index < 5; index++)
        {
            (await limits.AcquireRequestAsync(email, ip, CancellationToken.None)).ShouldBe(OtpLoginRateLimitDecision.Allowed);
            // Simulate cooldown expiry without a minute-long wait; the hourly counter remains.
            await connection.GetDatabase().KeyDeleteAsync($"bandup:{{otp-login}}:request:cooldown:{keys.Email(email)}");
        }
        (await limits.AcquireRequestAsync(email, ip, CancellationToken.None)).ShouldBe(OtpLoginRateLimitDecision.AccountSuppressed);
    }

    [Fact]
    public async Task AUTH_16_Redis_Verification_Budgets_Are_Atomic_And_Independent_Of_Resends()
    {
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(environment.RedisConnectionString);
        var limits = new RedisOtpLoginRateLimiter(connection, NewKeys());
        string email = $"quota-{Guid.CreateVersion7():N}@example.com";
        var emailResults = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => limits.AcquireVerificationAsync(email, NewIp(), CancellationToken.None)));
        emailResults.Count(result => result == OtpLoginRateLimitDecision.Allowed).ShouldBe(20);
        (await limits.AcquireRequestAsync(email, NewIp(), CancellationToken.None)).ShouldBe(OtpLoginRateLimitDecision.Allowed);
        string ip = NewIp();
        var ipResults = await Task.WhenAll(Enumerable.Range(0, 120).Select(_ =>
            limits.AcquireVerificationAsync($"{Guid.CreateVersion7():N}@example.com", ip, CancellationToken.None)));
        ipResults.Count(result => result == OtpLoginRateLimitDecision.Allowed).ShouldBe(100);
        ipResults.Count(result => result == OtpLoginRateLimitDecision.ClientRateLimited).ShouldBe(20);
    }

    private static string NewIp() => new IPAddress(Guid.CreateVersion7().ToByteArray()).ToString();

    [Fact]
    public async Task AUTH_16_Login_And_Reset_Use_Independent_Redis_Quotas_Even_With_The_Same_Key()
    {
        using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(environment.RedisConnectionString);
        string key = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray());
        var login = new RedisOtpLoginRateLimiter(connection, NewKeys());
        var reset = new RedisPasswordResetRateLimiter(connection, new PasswordResetRateLimitKeys(Options.Create(new PasswordResetSettings { CodeProtectionKey = key })));
        string email = $"independent-{Guid.CreateVersion7():N}@example.com";
        string ip = NewIp();
        (await login.AcquireRequestAsync(email, ip, CancellationToken.None)).ShouldBe(OtpLoginRateLimitDecision.Allowed);
        (await reset.AcquireRequestAsync(email, ip, CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.Allowed);
        (await login.AcquireRequestAsync(email, ip, CancellationToken.None)).ShouldBe(OtpLoginRateLimitDecision.AccountSuppressed);
        (await reset.AcquireRequestAsync(email, ip, CancellationToken.None)).ShouldBe(PasswordResetRateLimitDecision.AccountSuppressed);
    }
}
