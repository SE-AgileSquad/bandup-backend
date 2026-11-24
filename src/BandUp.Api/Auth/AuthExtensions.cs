using BandUp.Application.Features.Identity;
using BandUp.Application.Common;
using BandUp.Api.Errors;
using BandUp.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace BandUp.Api.Auth;

/// <summary>
/// Wires JWT bearer validation to the cookie ADR 0012 fixes: the token is read from
/// <c>bu_at</c> rather than an Authorization header, and stays anonymous when absent.
/// </summary>
public static class AuthExtensions
{
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddCookieJwtAuthentication(this IServiceCollection services)
    {
        // Read the settings from IOptions at runtime, not from IConfiguration at registration
        // time. The test factory's in-memory configuration is applied after the module
        // initializer, so reading IConfiguration here would see the empty appsettings value
        // and build a zero-length signing key.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthSettings>>((options, authOptions) =>
            {
                AuthSettings settings = authOptions.Value;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(settings.SigningKey!)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = AuthSettings.AccessTokenClockSkew,
                };

                options.Events = new JwtBearerEvents
                {
                    // NoResult prevents JwtBearer's default Authorization-header fallback.
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Cookies[settings.AccessCookieName];

                        if (string.IsNullOrWhiteSpace(context.Token))
                        {
                            context.NoResult();
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        AccessTokenIdentity? identity = AccessTokenClaims.Read(context.Principal);

                        if (identity is null)
                        {
                            context.Fail("Invalid session claims.");
                            return;
                        }

                        IAccessTokenRevocationStore revocations = context.HttpContext.RequestServices
                            .GetRequiredService<IAccessTokenRevocationStore>();

                        if (await revocations.IsRevokedAsync(identity.TokenId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Session revoked.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                        await Error.Unauthorized("Sign in to access this resource.")
                            .ToProblem(context.HttpContext).ExecuteAsync(context.HttpContext);
                    },
                };
            });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization(options => options.AddPolicy(
            AdminPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new ActiveAdminRequirement())));
        services.AddScoped<IAuthorizationHandler, ActiveAdminHandler>();

        return services;
    }
}
