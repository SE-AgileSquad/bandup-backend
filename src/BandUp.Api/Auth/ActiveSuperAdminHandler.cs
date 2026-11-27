using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using Microsoft.AspNetCore.Authorization;

namespace BandUp.Api.Auth;

internal sealed class ActiveSuperAdminRequirement : IAuthorizationRequirement;

internal sealed class ActiveSuperAdminHandler(ICurrentUser currentUser, IUserRepository users)
    : AuthorizationHandler<ActiveSuperAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveSuperAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || currentUser.UserId is not { } userId)
        {
            return;
        }

        CancellationToken cancellationToken = context.Resource is HttpContext http
            ? http.RequestAborted
            : CancellationToken.None;
        if (await users.IsActiveSuperAdminAsync(userId, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
