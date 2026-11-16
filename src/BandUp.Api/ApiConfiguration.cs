using BandUp.Api.Auth;
using BandUp.Api.Errors;
using BandUp.Api.Health;
using BandUp.Api.Middleware;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace BandUp.Api;

/// <summary>
/// Host-level wiring: the pieces that describe how this process is exposed rather than what
/// it does. Kept out of <c>Program.cs</c> so the composition root stays readable.
/// </summary>
public static class ApiConfiguration
{
    public const string CorsPolicy = "frontend";

    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    private static readonly string[] AllowedHeaders =
    [
        "Content-Type",
        "Accept",
        "Authorization",
        "X-Request-Id",
        "X-Requested-With",
    ];

    public static IServiceCollection AddApiConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<CorsSettings>()
            .BindConfiguration(CorsSettings.SectionName)
            .Validate(
                settings => Uri.TryCreate(settings.AllowedOrigin, UriKind.Absolute, out _),
                $"{CorsSettings.SectionName}:AllowedOrigin must be an absolute URI.")
            .ValidateOnStart();

        services
            .AddOptions<SwaggerSettings>()
            .BindConfiguration(SwaggerSettings.SectionName)
            .ValidateOnStart();

        services
            .AddOptions<ForwardedHeadersSettings>()
            .BindConfiguration(ForwardedHeadersSettings.SectionName)
            .ValidateOnStart();

        services
            .AddOptions<FeaturesSettings>()
            .BindConfiguration(FeaturesSettings.SectionName)
            .ValidateOnStart();

        services
            .AddOptions<SentrySettings>()
            .BindConfiguration(SentrySettings.SectionName)
            .ValidateOnStart();

        // Fail-fast on a bad signing key at API boot. The rules live with the binding in
        // Infrastructure; this host opts into running them at startup. The Worker never resolves
        // ITokenService, so it does not inherit the requirement for a key it would never use.
        services.AddOptions<AuthSettings>().ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddCookieJwtAuthentication();

        // Singleton: the service is stateless, and endpoint groups are singletons.
        services.AddSingleton<AuthCookieService>();

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            string? origin = configuration[$"{CorsSettings.SectionName}:AllowedOrigin"];

            if (!string.IsNullOrWhiteSpace(origin))
            {
                policy
                    .WithOrigins(origin)
                    .AllowCredentials()
                    .WithMethods(AllowedMethods)
                    .WithHeaders(AllowedHeaders);
            }
        }));

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The proxy is trusted by configuration alone: enabling the middleware is the
            // explicit decision, so known networks must not be limited to loopback.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "BandUp API",
            Version = "v1",
            Description = "IELTS Reading and Writing preparation platform.",
        }));

        services.AddProblemDetails();
        services.AddExceptionHandler<BandUpExceptionHandler>();

        services
            .AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                HealthCheckNames.Postgres,
                failureStatus: null,
                tags: [HealthCheckNames.DependencyTag],
                timeout: HealthCheckNames.Timeout)
            .AddCheck<CacheHealthCheck>(
                HealthCheckNames.Cache,
                failureStatus: null,
                tags: [HealthCheckNames.DependencyTag],
                timeout: HealthCheckNames.Timeout)
            .AddCheck<RabbitMqHealthCheck>(
                HealthCheckNames.RabbitMq,
                failureStatus: null,
                tags: [HealthCheckNames.DependencyTag],
                timeout: HealthCheckNames.Timeout)
            .AddCheck<PendingMigrationsHealthCheck>(
                HealthCheckNames.Migrations,
                failureStatus: null,
                tags: [HealthCheckNames.ReadyTag],
                timeout: HealthCheckNames.Timeout);

        return services;
    }

    public static WebApplication UseApiConfiguration(
        this WebApplication app,
        IConfiguration configuration)
    {
        IOptions<ForwardedHeadersSettings> forwarded =
            app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>();

        if (forwarded.Value.Enabled)
        {
            app.UseForwardedHeaders();
        }

        app.UseExceptionHandler();
        app.UseMiddleware<RequestIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseCors(CorsPolicy);

        // After CORS so preflights are answered, before the endpoints so a rejected request
        // never reaches a handler.
        app.UseMiddleware<CsrfMiddleware>();

        // Keep challenges inside the request-id/error envelope and after the CSRF guard.
        app.UseAuthentication();
        app.UseAuthorization();

        IOptions<SwaggerSettings> swagger = app.Services.GetRequiredService<IOptions<SwaggerSettings>>();

        if (swagger.Value.Enabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "swagger";

                if (app.Environment.IsDevelopment())
                {
                    options.UseRequestInterceptor("""
                        function (request) {
                            const url = new URL(request.url, window.location.href);
                            const method = (request.method || 'GET').toUpperCase();
                            const headers = request.headers || {};
                            const contentType = Object.entries(headers).find(([name]) => name.toLowerCase() === 'content-type');
                            if (url.origin === window.location.origin && url.pathname.startsWith('/api/') &&
                                !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method) &&
                                contentType && String(contentType[1]).toLowerCase().startsWith('application/json')) {
                                headers['X-Requested-With'] = 'bandup';
                                request.headers = headers;
                            }
                            return request;
                        }
                        """);
                }
            });
        }

        return app;
    }
}
