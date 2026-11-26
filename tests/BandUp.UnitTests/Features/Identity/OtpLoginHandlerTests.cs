using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.RequestOtpLogin;
using BandUp.Application.Features.Identity.VerifyOtpLogin;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class OtpLoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
    private const string Email = "learner@example.com";
    private const string Ip = "203.0.113.7";
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ICaptchaVerifier _captcha = Substitute.For<ICaptchaVerifier>();
    private readonly IOtpLoginRateLimiter _limits = Substitute.For<IOtpLoginRateLimiter>();
    private readonly IOtpLoginChallengeStore _store = Substitute.For<IOtpLoginChallengeStore>();
    private readonly IOtpLoginCodeProtector _codes = Substitute.For<IOtpLoginCodeProtector>();
    private readonly IPostCommitEmailQueue _emails = Substitute.For<IPostCommitEmailQueue>();
    private readonly ISessionTokenIssuer _issuer = Substitute.For<ISessionTokenIssuer>();
    private readonly IRefreshTokenStore _refresh = Substitute.For<IRefreshTokenStore>();

    public OtpLoginHandlerTests()
    {
        _captcha.VerifyAsync("captcha", Ip, Arg.Any<CancellationToken>()).Returns(CaptchaOutcome.Valid);
        _codes.GenerateCode().Returns("012345");
        _codes.Protect(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345").Returns(new string('a', 64));
    }

    [Fact]
    public async Task AUTH_16_Request_Forwards_Connection_Ip_To_Captcha_And_Queues_An_Expiring_Code()
    {
        var session = Session();
        _store.FindForUpdateAsync(Email, Arg.Any<CancellationToken>()).Returns(session with { Challenge = null });
        var result = await RequestAsync();
        result.IsSuccess.ShouldBeTrue();
        await _captcha.Received(1).VerifyAsync("captcha", Ip, CancellationToken.None);
        await _store.Received(1).ReplaceAsync(Arg.Is<OtpLoginChallenge>(challenge =>
            challenge.UserId == session.User.Id && challenge.ExpiresAt == Now.AddMinutes(10) && challenge.CodeMac != "012345"), CancellationToken.None);
        _emails.Received(1).Enqueue(Arg.Is<EmailMessage>(message => message.To == Email && message.TextBody.Contains("012345")), Now.AddMinutes(10));
    }

    [Theory]
    [InlineData(CaptchaOutcome.InvalidToken)]
    [InlineData(CaptchaOutcome.ProviderUnavailable)]
    public async Task AUTH_16_Bad_Captcha_Does_Not_Consume_An_Email_Cooldown(CaptchaOutcome outcome)
    {
        _captcha.VerifyAsync("captcha", Ip, Arg.Any<CancellationToken>()).Returns(outcome);
        (await RequestAsync()).IsFailure.ShouldBeTrue();
        _limits.ReceivedCalls().ShouldBeEmpty();
        _store.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("cooldown")]
    public async Task AUTH_16_Ineligible_Requests_Are_Acknowledged_Without_Storing_Or_Emailing(string state)
    {
        var session = Session();
        if (state == "inactive") { session.User.Deactivate(); }
        _store.FindForUpdateAsync(Email, Arg.Any<CancellationToken>()).Returns(state == "unknown" ? null : session);
        (await RequestAsync()).Value.Message.ShouldBe(new RequestOtpLoginOutcome().Message);
        _codes.Received(1).GenerateCode();
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<OtpLoginChallenge>(), Arg.Any<CancellationToken>());
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_16_Ip_Limits_Return_429_And_Email_Limits_Are_Generic(bool ipLimited)
    {
        var decision = ipLimited ? OtpLoginRateLimitDecision.ClientRateLimited : OtpLoginRateLimitDecision.AccountSuppressed;
        _limits.AcquireRequestAsync(Email, Ip, Arg.Any<CancellationToken>()).Returns(decision);
        _limits.AcquireVerificationAsync(Email, Ip, Arg.Any<CancellationToken>()).Returns(decision);
        var requested = await RequestAsync();
        var verified = await VerifyAsync();
        if (ipLimited)
        {
            requested.Error!.Code.ShouldBe(Error.OtpLoginRateLimitedCode);
            verified.Error!.Code.ShouldBe(Error.OtpLoginRateLimitedCode);
        }
        else
        {
            requested.IsSuccess.ShouldBeTrue();
            verified.Value.ShouldBeOfType<VerifyOtpLoginOutcome.Rejected>();
        }
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_16_Wrong_Guess_Is_A_Committable_Rejection_And_Exhausts_After_Five_Attempts()
    {
        var session = Session();
        _store.FindForUpdateAsync(Email, Arg.Any<CancellationToken>()).Returns(session);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            (await VerifyAsync()).Value.ShouldBeOfType<VerifyOtpLoginOutcome.Rejected>();
        }
        session.Challenge!.FailedAttempts.ShouldBe(5);
        _issuer.ReceivedCalls().ShouldBeEmpty();
        _refresh.ReceivedCalls().ShouldBeEmpty();
        session.User.LastLoginAt.ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_16_Verified_Code_Consumes_Once_And_Issues_An_Ordinary_Refresh_Session()
    {
        var session = Session();
        _store.FindForUpdateAsync(Email, Arg.Any<CancellationToken>()).Returns(session);
        _codes.Verify(session.Challenge!.Id, session.User.Id, "012345", session.Challenge.CodeMac).Returns(true);
        var issued = Issued(session.User);
        _issuer.Issue(session.User, Now, "browser").Returns(issued);
        var result = (await VerifyAsync()).Value.ShouldBeOfType<VerifyOtpLoginOutcome.Completed>();
        result.Login.UserId.ShouldBe(session.User.Id);
        result.Login.RefreshToken.ShouldBe(issued.RawRefreshToken);
        session.User.LastLoginAt.ShouldBe(Now);
        session.Challenge.ConsumedAt.ShouldBe(Now);
        await _refresh.Received(1).AddAsync(issued.RefreshToken, CancellationToken.None);
        (await VerifyAsync()).Value.ShouldBeOfType<VerifyOtpLoginOutcome.Rejected>();
        _issuer.Received(1).Issue(Arg.Any<User>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("invalidated")]
    public async Task AUTH_16_Unusable_States_Reject_Even_A_Matching_Code(string state)
    {
        var session = Session();
        if (state == "inactive") { session.User.Deactivate(); }
        if (state == "invalidated") { session.Challenge!.Invalidate(Now); }
        if (state == "missing") { session = session with { Challenge = null }; }
        if (state == "expired") { _time.Advance(TimeSpan.FromMinutes(10)); }
        _store.FindForUpdateAsync(Email, Arg.Any<CancellationToken>()).Returns(state == "unknown" ? null : session);
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(true);
        (await VerifyAsync()).Value.ShouldBeOfType<VerifyOtpLoginOutcome.Rejected>();
        _issuer.ReceivedCalls().ShouldBeEmpty();
        _refresh.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_16_Expiry_During_Token_Issuance_Does_Not_Stage_A_Session()
    {
        var session = Session();
        _store.FindForUpdateAsync(Email, Arg.Any<CancellationToken>()).Returns(session);
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(true);
        _issuer.Issue(session.User, Now, "browser").Returns(_ =>
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            return Issued(session.User);
        });
        (await VerifyAsync()).Value.ShouldBeOfType<VerifyOtpLoginOutcome.Rejected>();
        session.Challenge!.ConsumedAt.ShouldBeNull();
        session.User.LastLoginAt.ShouldBeNull();
        _refresh.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_16_Quota_Failure_Fails_Closed()
    {
        _limits.AcquireVerificationAsync(Email, Ip, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtpLoginRateLimitDecision>(new InvalidOperationException("quota unavailable")));
        await Should.ThrowAsync<InvalidOperationException>(() => VerifyAsync());
        _issuer.ReceivedCalls().ShouldBeEmpty();
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<RequestOtpLoginOutcome>> RequestAsync() =>
        new RequestOtpLoginHandler(_captcha, _limits, _store, _codes, _emails, _time)
            .HandleAsync(new RequestOtpLoginCommand(Email, "captcha", Ip), CancellationToken.None);

    private Task<Result<VerifyOtpLoginOutcome>> VerifyAsync() =>
        new VerifyOtpLoginHandler(_limits, _store, _codes, _issuer, _refresh, _time)
            .HandleAsync(new VerifyOtpLoginCommand(Email, "012345", Ip, "browser"), CancellationToken.None);

    private static OtpLoginSession Session()
    {
        User user = User.Create(Email, "Learner", "unchanged-hash", Now.AddDays(-1));
        return new(user, OtpLoginChallenge.Issue(Guid.CreateVersion7(), user.Id, new string('a', 64), Now, Now.AddMinutes(10), 5));
    }

    private static IssuedSession Issued(User user) => new(
        new AccessToken("access", Guid.CreateVersion7(), Now.AddMinutes(15)), "refresh",
        RefreshToken.Issue(user.Id, new string('b', 64), Now, Now.AddDays(7), "browser"));
}
