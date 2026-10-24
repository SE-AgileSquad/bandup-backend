using BandUp.Application;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Sentry;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(
    BandUpLoggerConfiguration.Create(BandUpLoggerConfiguration.Worker, builder.Configuration),
    dispose: true);

string? sentryDsn = builder.Configuration["Sentry:Dsn"];

if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.Logging.AddSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = builder.Environment.EnvironmentName;
        options.SendDefaultPii = false;
        options.AutoSessionTracking = false;
        options.SetBeforeSend((@event, _) =>
        {
            @event.SetTag("service", BandUpLoggerConfiguration.Worker);
            return @event;
        });
    });
}

// Long enough for the consumer to finish the delivery it is holding, then the broker
// redelivers anything still in flight to the next instance.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

IHost host = builder.Build();

await host.RunAsync();