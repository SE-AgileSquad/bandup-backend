using System.Text.Json;
using System.Text.Json.Serialization;
using BandUp.Api;
using BandUp.Api.Endpoints;
using BandUp.Api.Health;
using BandUp.Application;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Logging;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

const string MigrateArgument = "--migrate";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Read the signing key from the final configuration, not from the module initializer.
// The test factory's in-memory configuration is applied after the module initializer,
// so reading IConfiguration here would see the empty appsettings value.
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection(AuthSettings.SectionName));

builder.Services.AddSerilog(
    BandUpLoggerConfiguration.Create(BandUpLoggerConfiguration.Api, builder.Configuration),
    dispose: true);

string? sentryDsn = builder.Configuration["Sentry:Dsn"];

if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = builder.Environment.EnvironmentName;
        options.SendDefaultPii = false;
        options.AutoSessionTracking = false;
        options.SetBeforeSend((@event, _) =>
        {
            @event.SetTag("service", BandUpLoggerConfiguration.Api);
            return @event;
        });
    });
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApiConfiguration(builder.Configuration)
    .AddEndpointGroups();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

WebApplication app = builder.Build();

if (args.Contains(MigrateArgument, StringComparer.Ordinal))
{
    return await ApplyMigrationsAsync(app);
}

app.UseApiConfiguration(builder.Configuration);
app.MapHealthEndpoints();
app.MapEndpointGroups();

await app.RunAsync();

return 0;

static async Task<int> ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

    try
    {
        // Resolving the options runs the validators, so a missing connection string fails
        // here with a readable message instead of a null reference from UseNpgsql.
        _ = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;

        BandUpDbContext context = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();

        await context.Database.MigrateAsync();

        app.Logger.LogInformation("Migrations applied");

        return 0;
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Applying migrations failed");

        return 1;
    }
}

/// <summary>
/// Exposed so the integration tests can host this application with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program { }
