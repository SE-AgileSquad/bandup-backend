using System.Net.Http.Json;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// Cloudflare Turnstile adapter. Calls <c>siteverify</c> with form fields — the endpoint does
/// not accept JSON. Any transport-level failure (timeout, DNS, 5xx) is
/// <see cref="CaptchaOutcome.ProviderUnavailable"/>, never an exception and never a silent
/// pass: the <see cref="OutageTolerantCaptchaVerifier"/> decides what an outage means.
/// </summary>
public sealed class TurnstileCaptchaVerifier(
    HttpClient httpClient,
    CaptchaSettings settings,
    ILogger<TurnstileCaptchaVerifier> logger) : ICaptchaVerifier
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<CaptchaOutcome> VerifyAsync(
        string? token,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return CaptchaOutcome.InvalidToken;
        }

        try
        {
            using CancellationTokenSource timeoutSource = CreateTimeout(cancellationToken);

            FormUrlEncodedContent content = new(
            [
                new KeyValuePair<string, string>("secret", settings.SecretKey ?? string.Empty),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", clientIp ?? string.Empty),
            ]);

            using HttpResponseMessage response = await httpClient.PostAsync(
                SiteVerifyUrl,
                content,
                timeoutSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Turnstile siteverify returned {statusCode}; treating as provider unavailable",
                    (int)response.StatusCode);

                return CaptchaOutcome.ProviderUnavailable;
            }

            TurnstileResponse? body = await response.Content
                .ReadFromJsonAsync<TurnstileResponse>(timeoutSource.Token);

            return body?.Success is true
                ? CaptchaOutcome.Valid
                : CaptchaOutcome.InvalidToken;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException
            or TimeoutException or OperationCanceledException)
        {
            // The token was not validated. Whether that blocks the request is the decorator's
            // decision; distinguishing the two outcomes here is all this adapter owes.
            logger.LogWarning(
                exception,
                "Turnstile siteverify is unreachable; treating as provider unavailable");

            return CaptchaOutcome.ProviderUnavailable;
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(Timeout);

        return source;
    }

    private sealed record TurnstileResponse(bool Success);
}
