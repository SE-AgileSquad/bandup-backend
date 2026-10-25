using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.System.GetServerTime;
using BandUp.Application.Features.System.SendPing;
using BandUp.Contracts.Messages;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.System;

public sealed class SystemSliceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task INF_07_GetServerTime_Handler_Returns_The_Injected_Clock_And_Server_Info()
    {
        FakeTimeProvider clock = new(Now);
        IServerInfo serverInfo = Substitute.For<IServerInfo>();
        serverInfo.Environment.Returns("Development");
        serverInfo.Version.Returns("1.2.3");

        GetServerTimeHandler handler = new(clock, serverInfo);

        Result<ServerTimeDto> result = await handler.HandleAsync(
            new GetServerTimeQuery(),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UtcNow.ShouldBe(Now);
        result.Value.Environment.ShouldBe("Development");
        result.Value.Version.ShouldBe("1.2.3");
    }

    [Fact]
    public async Task INF_07_SendPing_Handler_Publishes_The_Message_And_Returns_Its_Id()
    {
        FakeTimeProvider clock = new(Now);
        IMessagePublisher publisher = Substitute.For<IMessagePublisher>();
        SendPingHandler handler = new(publisher, clock);

        Result<Guid> result = await handler.HandleAsync(new SendPingCommand("hello"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);

        await publisher.Received(1).PublishAsync(
            Arg.Is<PingRequested>(message =>
                message.PingId == result.Value &&
                message.Message == "hello" &&
                message.RequestedAt == Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task INF_07_SendPing_Handler_Uses_A_Version7_Id()
    {
        SendPingHandler handler = new(Substitute.For<IMessagePublisher>(), new FakeTimeProvider(Now));

        Result<Guid> result = await handler.HandleAsync(new SendPingCommand("hello"), CancellationToken.None);

        // Version 7 ids carry the version in the most significant nibble of the third group.
        result.Value.ToString()[14].ShouldBe('7');
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void INF_05_SendPing_Validator_Rejects_An_Empty_Message(string message)
    {
        SendPingValidator validator = new();

        validator.Validate(new SendPingCommand(message)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void INF_05_SendPing_Validator_Rejects_A_Message_Longer_Than_The_Limit()
    {
        SendPingValidator validator = new();
        string tooLong = new('a', SendPingValidator.MaxMessageLength + 1);

        validator.Validate(new SendPingCommand(tooLong)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void INF_05_SendPing_Validator_Accepts_A_Message_At_The_Limit()
    {
        SendPingValidator validator = new();
        string atLimit = new('a', SendPingValidator.MaxMessageLength);

        validator.Validate(new SendPingCommand(atLimit)).IsValid.ShouldBeTrue();
    }
}
