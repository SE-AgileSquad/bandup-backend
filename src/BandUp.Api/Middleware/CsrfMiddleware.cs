using BandUp.Api.Errors;
using BandUp.Application.Common;
using BandUp.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BandUp.Api.Middleware;

/// <summary>
/// SameSite=Strict cookies keep most cross-site submissions out; this is the second layer.
/// A state-changing request that carries a session must also declare a JSON body and the custom
/// header, which a plain HTML form on another origin cannot send.
/// </summary>
/// <remarks>
/// Requests with no session cookie are not guarded: there is no ambient credential to forge,
/// and guarding them would break unauthenticated POSTs such as the diagnostics ping and
/// Swagger's "Try it out". The residual risk is login CSRF, which the CAPTCHA and the Strict
/// cookie policy bound.
/// </remarks>
public sealed class CsrfMiddleware(RequestDelegate next, IOptions<AuthSettings> settings)
{
    private const string ExpectedHeader = "X-Requested-With";
    private const string ExpectedHeaderValue = "bandup";

    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (IsSafe(httpContext.Request.Method) || !HasSession(httpContext) || IsAcceptable(httpContext.Request))
        {
            await next(httpContext);
            return;
        }

        ProblemDetails problem = ProblemDetailsFactory.ForError(
            Error.Validation(new Dictionary<string, string[]>
            {
                [ExpectedHeader] = [$"Requests with a session must send '{ExpectedHeader}: {ExpectedHeaderValue}' and a JSON body."],
            }),
            httpContext);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status400BadRequest;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }

    private bool HasSession(HttpContext httpContext)
    {
        AuthSettings auth = settings.Value;

        return httpContext.Request.Cookies.ContainsKey(auth.AccessCookieName)
            || httpContext.Request.Cookies.ContainsKey(auth.RefreshCookieName);
    }

    private static bool IsSafe(string method) =>
        SafeMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    private static bool IsAcceptable(HttpRequest request)
    {
        string? contentType = request.ContentType;

        bool jsonBody = contentType is not null &&
            contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

        string requestedWith = request.Headers[ExpectedHeader].ToString();

        return jsonBody && string.Equals(requestedWith, ExpectedHeaderValue, StringComparison.OrdinalIgnoreCase);
    }
}