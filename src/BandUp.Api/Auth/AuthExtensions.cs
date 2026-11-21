using BandUp.Application.Features.Identity;
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
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                options.Events = new JwtBearerEvents
                {
                    // The cookie is the transport. No cookie, no token, no Authorization
                    // header fallback: the spec's cookie-first design, unchanged.
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Cookies[settings.AccessCookieName];
                        return Task.CompletedTask;
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
