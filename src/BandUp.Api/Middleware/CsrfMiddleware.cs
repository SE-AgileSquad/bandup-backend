using BandUp.Api.Errors;
using BandUp.Application.Common;
using BandUp.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using BandUp.Api.Endpoints.Admin;
using BandUp.Application.Features.Media.Admin;
using Microsoft.AspNetCore.Http.Features;

namespace BandUp.Api.Middleware;

/// <summary>
/// SameSite=Strict cookies keep most cross-site submissions out; this is the second layer.
/// A state-changing request that carries a session must also declare JSON (or multipart on
/// photo upload endpoints) and the custom header, which a plain cross-origin HTML form cannot send.
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
        bool photoUpload = httpContext.GetEndpoint()?.Metadata.GetMetadata<PhotoUploadMetadata>() is not null;
        if (photoUpload)
        {
            if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = ImageFileInfo.MaxRequestBytes;
            }
            if (httpContext.Request.ContentLength > ImageFileInfo.MaxRequestBytes)
            {
                var tooLarge = ProblemDetailsFactory.ForError(Error.Validation(new Dictionary<string, string[]>
                    { ["file"] = ["The photo request exceeds the size limit."] }), httpContext);
                tooLarge.Status = StatusCodes.Status413PayloadTooLarge;
                tooLarge.Title = "Payload Too Large";
                httpContext.Response.StatusCode = tooLarge.Status.Value;
                await httpContext.Response.WriteAsJsonAsync(tooLarge, options: null, contentType: "application/problem+json", cancellationToken: httpContext.RequestAborted);
                return;
            }
        }
        if (IsSafe(httpContext.Request.Method) || !HasSession(httpContext) || IsAcceptable(httpContext.Request, photoUpload))
        {
            await next(httpContext);
            return;
        }

        ProblemDetails problem = ProblemDetailsFactory.ForError(
            Error.Validation(new Dictionary<string, string[]>
            {
                [ExpectedHeader] = [$"Requests with a session must send '{ExpectedHeader}: {ExpectedHeaderValue}' and JSON (or multipart on photo upload endpoints)."],
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

    private static bool IsAcceptable(HttpRequest request, bool photoUpload)
    {
        string? contentType = request.ContentType;

        bool jsonBody = contentType is not null &&
            contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

        string requestedWith = request.Headers[ExpectedHeader].ToString();

        bool multipartPhoto = photoUpload && request.HasFormContentType
            && contentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);
        return (jsonBody || multipartPhoto) && string.Equals(requestedWith, ExpectedHeaderValue, StringComparison.OrdinalIgnoreCase);
    }
}
