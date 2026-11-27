using BandUp.Api.Auth;
using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Application.Features.Identity.GetMe;
using BandUp.Application.Features.Identity.Login;
using BandUp.Application.Features.Identity.Logout;
using BandUp.Application.Features.Identity.Refresh;
using BandUp.Application.Features.Identity.Register;
using BandUp.Application.Features.Identity.ResetPassword;
using BandUp.Application.Features.Identity.VerifyResetCode;
using BandUp.Application.Features.Identity.RequestOtpLogin;
using BandUp.Application.Features.Identity.VerifyOtpLogin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BandUp.Api.Endpoints.Auth;

/// <summary>
/// Maps the Identity use cases to HTTP and manages session cookies after dispatch completes.
/// </summary>
public sealed class AuthEndpoints(AuthCookieService cookieService) : IEndpointGroup
{
    public string Prefix => "auth";
    public bool RequiresDiagnostics => false;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/register", RegisterAsync)
            .WithName("Auth_Register")
            .Produces<UserDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapPost("/login", LoginAsync)
            .WithName("Auth_Login")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapPost("/otp/request", RequestOtpLoginAsync)
            .WithName("Auth_RequestOtpLogin")
            .AllowAnonymous()
            .Accepts<RequestOtpLoginRequest>("application/json")
            .WithSummary("Request an email sign-in code for an existing account.")
            .WithDescription("Send email and captchaToken. Unknown, inactive and suppressed accounts receive the same acknowledgement. Codes are delivered only by email. If session cookies are present, send X-Requested-With: bandup.")
            .Produces<RequestOtpLoginOutcome>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/otp/verify", VerifyOtpLoginAsync)
            .WithName("Auth_VerifyOtpLogin")
            .AllowAnonymous()
            .Accepts<VerifyOtpLoginRequest>("application/json")
            .WithSummary("Sign in using an email and a six-digit code.")
            .WithDescription("Send email and code as a string, preserving leading zeroes. Success sets access and refresh cookies after commit; tokens are never returned in JSON. Invalid, expired and exhausted codes share INVALID_OTP_LOGIN_CODE (401). Do not refresh/retry that error. If session cookies are present, send X-Requested-With: bandup.")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/me", GetMeAsync)
            .WithName("Auth_GetMe")
            .RequireAuthorization()
            .Produces<MeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/refresh", RefreshAsync)
            .WithName("Auth_Refresh")
            .AllowAnonymous()
            .Accepts<SessionRequest>("application/json")
            .WithSummary("Renew the session cookies.")
            .WithDescription("Uses the refresh cookie without requiring a valid access token. Send {} as application/json with X-Requested-With: bandup. Tokens are never returned in JSON.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/logout", LogoutAsync)
            .WithName("Auth_Logout")
            .RequireAuthorization()
            .Accepts<SessionRequest>("application/json")
            .WithSummary("End the current session.")
            .WithDescription("Requires a valid access cookie. Send {} as application/json with X-Requested-With: bandup. Success deletes both session cookies.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("Auth_ForgotPassword")
            .AllowAnonymous()
            .Accepts<ForgotPasswordRequest>("application/json")
            .WithSummary("Request an email password-reset code.")
            .WithDescription("Anonymous. Always returns the same acknowledgement for eligible, unknown or suppressed accounts. If session cookies are present, send X-Requested-With: bandup. Codes are delivered by email and never returned by this API.")
            .Produces<ForgotPasswordOutcome>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/verify-reset-code", VerifyResetCodeAsync)
            .WithName("Auth_VerifyResetCode")
            .AllowAnonymous()
            .Accepts<VerifyResetCodeRequest>("application/json")
            .WithSummary("Verify a password-reset code and obtain a reset-only token.")
            .WithDescription("Anonymous. Send email and a six-digit code as a string. Success consumes the code and returns a single-use resetToken valid for five minutes, without signing in or changing cookies. If session cookies are present, send X-Requested-With: bandup. Invalid codes share INVALID_PASSWORD_RESET_CODE (422).")
            .Produces<VerifyResetCodeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("Auth_ResetPassword")
            .AllowAnonymous()
            .Accepts<ResetPasswordRequest>("application/json")
            .WithSummary("Reset a password using the token from verify-reset-code.")
            .WithDescription("Anonymous. Send resetToken and newPassword. If session cookies are present, send X-Requested-With: bandup. Success consumes the token and clears this browser's session cookies after commit; sign in normally afterward. Invalid, expired and reused tokens share INVALID_PASSWORD_RESET_TOKEN (422). Existing access tokens on other devices keep their normal expiry.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        RegisterCommand command = new(
            request.Email,
            request.Password,
            request.DisplayName,
            request.CaptchaToken,
            httpContext.Connection.RemoteIpAddress?.ToString());

        Result<Guid> result = await dispatcher.SendAsync<RegisterCommand, Guid>(command, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToProblem(httpContext);
        }

        UserDto dto = new(result.Value, request.Email, request.DisplayName, "User", 3);
        return TypedResults.Created((string?)null, dto);
    }

    private async Task<IResult> LoginAsync(
        LoginRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        LoginCommand command = new(
            request.Email,
            request.Password,
            request.CaptchaToken,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());

        Result<LoginResult> result = await dispatcher.SendAsync<LoginCommand, LoginResult>(command, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToProblem(httpContext);
        }

        cookieService.SetSessionCookies(
            httpContext.Response.Cookies,
            result.Value.AccessToken,
            result.Value.RefreshToken,
            result.Value.RefreshTokenExpiresAt);

        UserDto dto = new(
            result.Value.UserId,
            result.Value.Email,
            result.Value.DisplayName,
            result.Value.Role,
            0); // Balance fetched in /me

        return TypedResults.Ok(dto);
    }

    private static async Task<IResult> RequestOtpLoginAsync(
        RequestOtpLoginRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        RequestOtpLoginCommand command = new(request.Email, request.CaptchaToken, httpContext.Connection.RemoteIpAddress?.ToString());
        Result<RequestOtpLoginOutcome> result = await dispatcher.SendAsync<RequestOtpLoginCommand, RequestOtpLoginOutcome>(command, cancellationToken);
        return result.IsSuccess ? TypedResults.Accepted((string?)null, result.Value) : result.ToProblem(httpContext);
    }

    private async Task<IResult> VerifyOtpLoginAsync(
        VerifyOtpLoginRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        VerifyOtpLoginCommand command = new(request.Email, request.Code, httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());
        Result<VerifyOtpLoginOutcome> result = await dispatcher.SendAsync<VerifyOtpLoginCommand, VerifyOtpLoginOutcome>(command, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToProblem(httpContext);
        }
        switch (result.Value)
        {
            case VerifyOtpLoginOutcome.Completed completed:
                LoginResult login = completed.Login;
                cookieService.SetSessionCookies(httpContext.Response.Cookies, login.AccessToken, login.RefreshToken, login.RefreshTokenExpiresAt);
                return TypedResults.Ok(new UserDto(login.UserId, login.Email, login.DisplayName, login.Role, 0));
            case VerifyOtpLoginOutcome.Rejected:
                return Error.InvalidOtpLogin().ToProblem(httpContext);
            default:
                throw new InvalidOperationException("Unknown OTP sign-in outcome.");
        }
    }

    private async Task<IResult> RefreshAsync(
        SessionRequest _,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        RefreshCommand command = new(
            cookieService.ReadRefreshToken(httpContext.Request),
            httpContext.Request.Headers.UserAgent.ToString());
        Result<RefreshOutcome> result = await dispatcher.SendAsync<RefreshCommand, RefreshOutcome>(command, cancellationToken);

        if (result.IsFailure)
        {
            cookieService.ClearSessionCookies(httpContext.Response.Cookies);
            return result.ToProblem(httpContext);
        }

        switch (result.Value)
        {
            case RefreshOutcome.Issued issued:
                cookieService.SetSessionCookies(
                    httpContext.Response.Cookies,
                    issued.AccessToken,
                    issued.RefreshToken,
                    issued.RefreshTokenExpiresAt);
                return TypedResults.Ok();
            case RefreshOutcome.RejectedAfterRevocation:
                cookieService.ClearSessionCookies(httpContext.Response.Cookies);
                return Error.InvalidRefreshToken().ToProblem(httpContext);
            default:
                throw new InvalidOperationException("Unknown refresh outcome.");
        }
    }

    private async Task<IResult> LogoutAsync(
        SessionRequest _,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        LogoutCommand command = new(cookieService.ReadRefreshToken(httpContext.Request));
        Result<LogoutOutcome> result = await dispatcher.SendAsync<LogoutCommand, LogoutOutcome>(command, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToProblem(httpContext);
        }

        cookieService.ClearSessionCookies(httpContext.Response.Cookies);
        return TypedResults.NoContent();
    }

    private async Task<IResult> GetMeAsync(
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<MeDto> result = await dispatcher.QueryAsync<GetMeQuery, MeDto>(new GetMeQuery(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ForgotPasswordCommand command = new(request.Email, httpContext.Connection.RemoteIpAddress?.ToString());
        Result<ForgotPasswordOutcome> result = await dispatcher.SendAsync<ForgotPasswordCommand, ForgotPasswordOutcome>(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> VerifyResetCodeAsync(
        VerifyResetCodeRequest request, IDispatcher dispatcher, HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
        VerifyResetCodeCommand command = new(request.Email, request.Code, httpContext.Connection.RemoteIpAddress?.ToString());
        Result<VerifyResetCodeOutcome> result = await dispatcher.SendAsync<VerifyResetCodeCommand, VerifyResetCodeOutcome>(command, cancellationToken);
        if (result.IsFailure) { return result.ToProblem(httpContext); }
        return result.Value switch
        {
            VerifyResetCodeOutcome.Verified verified => TypedResults.Ok(new VerifyResetCodeResponse(verified.ResetToken, verified.ExpiresAt)),
            VerifyResetCodeOutcome.Rejected => Error.InvalidPasswordReset().ToProblem(httpContext),
            _ => throw new InvalidOperationException("Unknown reset-code verification outcome."),
        };
    }

    private async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ResetPasswordCommand command = new(request.ResetToken, request.NewPassword,
            httpContext.Connection.RemoteIpAddress?.ToString());
        Result<ResetPasswordOutcome> result = await dispatcher.SendAsync<ResetPasswordCommand, ResetPasswordOutcome>(command, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToProblem(httpContext);
        }

        switch (result.Value)
        {
            case ResetPasswordOutcome.Completed:
                cookieService.ClearSessionCookies(httpContext.Response.Cookies);
                return TypedResults.NoContent();
            case ResetPasswordOutcome.Rejected:
                return Error.InvalidPasswordResetToken().ToProblem(httpContext);
            default:
                throw new InvalidOperationException("Unknown password-reset outcome.");
        }
    }

    public sealed record RegisterRequest(string Email, string Password, string DisplayName, string CaptchaToken);
    public sealed record LoginRequest(string Email, string Password, string CaptchaToken);
    public sealed record RequestOtpLoginRequest(string Email, string? CaptchaToken);
    public sealed record VerifyOtpLoginRequest(string Email, string Code);
    public sealed record ForgotPasswordRequest(string Email);
    public sealed record VerifyResetCodeRequest(string Email, string Code);
    public sealed record VerifyResetCodeResponse(string ResetToken, DateTimeOffset ExpiresAt);
    public sealed record ResetPasswordRequest(string ResetToken, string NewPassword);
    public sealed record SessionRequest;
    public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, int CreditBalance);
}
