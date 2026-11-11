using System.Net;
using System.Text;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

/// <summary>
/// The Turnstile adapter's only job is mapping transport outcomes onto the three-valued
/// contract: a healthy provider's verdict is passed through, and anything unreachable becomes
/// ProviderUnavailable rather than an exception or a silent pass.
/// </summary>
public sealed class TurnstileCaptchaVerifierTests
{
    private static CaptchaSettings Settings() => new()
    {
        SiteKey = "site-key",
        SecretKey = "secret-key",
    };

    private static TurnstileCaptchaVerifier Create(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(Settings()),
        NullLogger<TurnstileCaptchaVerifier>.Instance);

    [Fact]
    public async Task A_Successful_Verification_Is_Valid()
    {
        TurnstileCaptchaVerifier verifier = Create(new StubHandler(
            HttpStatusCode.OK,
            """{"success":true}"""));

        (await verifier.VerifyAsync("token", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.Valid);
    }

    [Fact]
    public async Task A_Rejected_Token_Is_InvalidToken()
    {
        TurnstileCaptchaVerifier verifier = Create(new StubHandler(
            HttpStatusCode.OK,
            """{"success":false,"error-codes":["invalid-input-response"]}"""));

        (await verifier.VerifyAsync("bad-token", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.InvalidToken);
    }

    [Fact]
    public async Task An_Empty_Token_Is_InvalidToken_Without_Calling_The_Provider()
    {
        StubHandler handler = new(HttpStatusCode.OK, """{"success":true}""");
        TurnstileCaptchaVerifier verifier = Create(handler);

        (await verifier.VerifyAsync(null, "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.InvalidToken);
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_Non_Success_Status_Is_ProviderUnavailable(HttpStatusCode status)
    {
        TurnstileCaptchaVerifier verifier = Create(new StubHandler(status, ""));

        (await verifier.VerifyAsync("token", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.ProviderUnavailable);
    }

    [Fact]
    public async Task A_Transport_Failure_Is_ProviderUnavailable_Not_An_Exception()
    {
        TurnstileCaptchaVerifier verifier = Create(new ThrowingHandler());

        (await verifier.VerifyAsync("token", "1.2.3.4", CancellationToken.None))
            .ShouldBe(CaptchaOutcome.ProviderUnavailable);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
