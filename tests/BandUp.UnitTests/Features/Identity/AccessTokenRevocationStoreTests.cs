using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using StackExchange.Redis;

namespace BandUp.UnitTests.Features.Identity;

public sealed class AccessTokenRevocationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(60)]
    [InlineData(-10)]
    public async Task AUTH_10_Revocation_Lasts_Through_Expiry_And_Clock_Skew(int expiresInSeconds)
    {
        FakeTimeProvider time = new(Now);
        CachedAccessTokenRevocationStore store = Create(new InMemoryCacheService(time), time);
        Guid tokenId = Guid.CreateVersion7();
        (await store.IsRevokedAsync(tokenId, CancellationToken.None)).ShouldBeFalse();

        await store.RevokeAsync(tokenId, Now.AddSeconds(expiresInSeconds), CancellationToken.None);

        (await store.IsRevokedAsync(tokenId, CancellationToken.None)).ShouldBeTrue();
        (await store.IsRevokedAsync(Guid.CreateVersion7(), CancellationToken.None)).ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(expiresInSeconds + 29));
        (await store.IsRevokedAsync(tokenId, CancellationToken.None)).ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(1));
        (await store.IsRevokedAsync(tokenId, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task AUTH_10_Tokens_Outside_The_Validation_Window_Do_Not_Create_Entries()
    {
        ICacheService cache = Substitute.For<ICacheService>();
        CachedAccessTokenRevocationStore store = Create(cache, new FakeTimeProvider(Now));

        await store.RevokeAsync(Guid.CreateVersion7(), Now.AddSeconds(-31), CancellationToken.None);

        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AUTH_10_Redis_Read_Outage_Fails_Open_And_Logs_A_Warning()
    {
        ICacheService cache = Substitute.For<ICacheService>();
        RedisConnectionException failure = new(
            ConnectionFailureType.UnableToConnect, CommandFlags.None, "test outage", null, CommandStatus.Unknown);
        cache.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(failure));
        ILogger<CachedAccessTokenRevocationStore> logger = Substitute.For<ILogger<CachedAccessTokenRevocationStore>>();
        CachedAccessTokenRevocationStore store = new(cache, new FakeTimeProvider(Now), logger);

        (await store.IsRevokedAsync(Guid.CreateVersion7(), CancellationToken.None)).ShouldBeFalse();

        var log = logger.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(ILogger.Log));
        log.GetArguments()[0].ShouldBe(LogLevel.Warning);
        log.GetArguments()[3].ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task AUTH_10_Redis_Write_Outage_Propagates()
    {
        ICacheService cache = Substitute.For<ICacheService>();
        cache.SetAsync(Arg.Any<string>(), true, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new RedisConnectionException(
                ConnectionFailureType.UnableToConnect, CommandFlags.None, "test outage", null, CommandStatus.Unknown)));
        CachedAccessTokenRevocationStore store = Create(cache, new FakeTimeProvider(Now));

        await Should.ThrowAsync<RedisConnectionException>(() =>
            store.RevokeAsync(Guid.CreateVersion7(), Now.AddMinutes(5), CancellationToken.None));
    }

    [Fact]
    public async Task AUTH_10_Cancellation_And_Unexpected_Read_Failures_Do_Not_Fail_Open()
    {
        ICacheService cache = Substitute.For<ICacheService>();
        CachedAccessTokenRevocationStore store = Create(cache, new FakeTimeProvider(Now));
        cache.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new OperationCanceledException()));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.IsRevokedAsync(Guid.CreateVersion7(), CancellationToken.None));

        cache.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("test bug")));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.IsRevokedAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    private static CachedAccessTokenRevocationStore Create(ICacheService cache, TimeProvider time) =>
        new(cache, time, NullLogger<CachedAccessTokenRevocationStore>.Instance);
}
