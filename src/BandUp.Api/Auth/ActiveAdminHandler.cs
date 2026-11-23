using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using Microsoft.AspNetCore.Authorization;

namespace BandUp.Api.Auth;

internal sealed class ActiveAdminRequirement : IAuthorizationRequirement;

internal sealed class ActiveAdminHandler(ICurrentUser currentUser, IUserRepository users)
    : AuthorizationHandler<ActiveAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || currentUser.UserId is not { } userId)
        {
            return;
        }

        CancellationToken cancellationToken = context.Resource is HttpContext http
            ? http.RequestAborted
            : CancellationToken.None;
        if (await users.IsActiveAdminAsync(userId, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
