using BandUp.Api.Auth;
using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.GetMe;
using BandUp.Application.Features.Identity.Login;
using BandUp.Application.Features.Identity.Register;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BandUp.Api.Endpoints.Auth;

/// <summary>
/// AUTH-01 to 04, 06-08. Implements the registration, login and profile flow. Registration
/// creates the user + credit account; login mints cookies; GetMe provides the profile shape
/// for the dashboard.
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

        endpoints.MapGet("/me", GetMeAsync)
            .WithName("Auth_GetMe")
            .RequireAuthorization()
            .Produces<MeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
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
            result.Value.RefreshToken);

        UserDto dto = new(
            result.Value.UserId,
            result.Value.Email,
            result.Value.DisplayName,
            result.Value.Role,
            0); // Balance fetched in /me

        return TypedResults.Ok(dto);
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

    public sealed record RegisterRequest(string Email, string Password, string DisplayName, string CaptchaToken);
    public sealed record LoginRequest(string Email, string Password, string CaptchaToken);
    public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, int CreditBalance);
}
