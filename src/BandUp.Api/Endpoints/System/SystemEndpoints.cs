using BandUp.Api.Errors;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.System.GetServerTime;
using BandUp.Application.Features.System.SendPing;

namespace BandUp.Api.Endpoints.System;

/// <summary>
/// The reference slice: one query and one command, showing the whole path from HTTP to a
/// handler. Everything else in the product is written the same way.
/// </summary>
public sealed class SystemEndpoints : IEndpointGroup
{
    public string Prefix => "system";

    public bool RequiresDiagnostics => true;

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/time", GetServerTimeAsync)
            .WithName("System_GetServerTime")
            .Produces<ServerTimeDto>();

        endpoints.MapPost("/ping", SendPingAsync)
            .WithName("System_SendPing")
            .Produces<SendPingResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GetServerTimeAsync(
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<ServerTimeDto> result = await dispatcher.QueryAsync<GetServerTimeQuery, ServerTimeDto>(
            new GetServerTimeQuery(),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.ToProblem(httpContext);
    }

    private static async Task<IResult> SendPingAsync(
        SendPingCommand command,
        IDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync<SendPingCommand, Guid>(command, cancellationToken);

        // No Location header: the ping is a job, not a resource the caller can fetch.
        return result.IsSuccess
            ? TypedResults.Accepted((string?)null, new SendPingResponse(result.Value))
            : result.ToProblem(httpContext);
    }

    public sealed record SendPingResponse(Guid PingId);
}
