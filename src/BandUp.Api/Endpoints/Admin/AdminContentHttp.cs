using BandUp.Api.Auth;
using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Api.Endpoints.Admin;

internal static class AdminContentHttp
{
    internal static RouteGroupBuilder RequireAdmin(IEndpointRouteBuilder endpoints) =>
        RequirePolicy(endpoints, AuthExtensions.AdminPolicy);

    internal static RouteGroupBuilder RequireSuperAdmin(IEndpointRouteBuilder endpoints) =>
        RequirePolicy(endpoints, AuthExtensions.SuperAdminPolicy);

    private static RouteGroupBuilder RequirePolicy(IEndpointRouteBuilder endpoints, string policy) =>
        endpoints.MapGroup("").RequireAuthorization(policy)
            .WithMetadata(
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []));

    internal static async Task<IResult> CreatedAsync<TCommand>(
        TCommand command, string resourcePath, IDispatcher dispatcher, HttpContext http)
        where TCommand : ICommand<Guid>
    {
        Result<Guid> result = await dispatcher.SendAsync<TCommand, Guid>(command, http.RequestAborted);
        return result.IsSuccess
            ? TypedResults.Created($"{resourcePath}/{result.Value}", new CreatedResource(result.Value))
            : result.ToProblem(http);
    }

    internal static async Task<IResult> ChangeAsync<TCommand>(
        TCommand command, IDispatcher dispatcher, HttpContext http)
        where TCommand : ICommand<bool>
    {
        Result<bool> result = await dispatcher.SendAsync<TCommand, bool>(command, http.RequestAborted);
        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(http);
    }

    internal static async Task<IResult> ReadAsync<TQuery, TResponse>(
        TQuery query, IDispatcher dispatcher, HttpContext http)
        where TQuery : IQuery<TResponse>
    {
        Result<TResponse> result = await dispatcher.QueryAsync<TQuery, TResponse>(query, http.RequestAborted);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(http);
    }

    internal sealed record CreatedResource(Guid Id);
}
