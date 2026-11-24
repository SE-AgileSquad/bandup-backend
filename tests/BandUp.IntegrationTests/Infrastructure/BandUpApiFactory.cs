using BandUp.Infrastructure.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API against the test containers. Configuration is supplied in memory so the
/// tests never read the development settings or the developer's user secrets.
/// </summary>
public sealed class BandUpApiFactory : WebApplicationFactory<Program>
{
    private readonly BandUpTestEnvironment _environment;
    private readonly string _environmentName;
    private readonly Dictionary<string, string?> _overrides;
    private readonly TestLogSink? _logSink;
    private readonly Action<IServiceCollection>? _configureServices;

    public BandUpApiFactory(
        BandUpTestEnvironment environment,
        string environmentName = "Development",
        Dictionary<string, string?>? overrides = null,
        TestLogSink? logSink = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _environment = environment;
        _environmentName = environmentName;
        _overrides = overrides ?? [];
        _logSink = logSink;
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Dictionary<string, string?> settings = BuildSettings();

        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(settings));

        // Counts which endpoints the tests reach (see EndpointHitRecorder).
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter, EndpointHitStartupFilter>());

        if (_logSink is null && _configureServices is null)
        {
            return;
        }

        builder.ConfigureTestServices(services =>
        {
            if (_logSink is not null)
            {
                // Replace the process logger with the same configuration plus a capturing sink,
                // so assertions run against the real pipeline and formatter.
                services.RemoveAll<ILoggerFactory>();

                IConfiguration configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(settings)
                    .Build();

                services.AddSerilog(BandUpLoggerConfiguration.Create(
                    BandUpLoggerConfiguration.Api,
                    configuration,
                    _logSink));
            }

            _configureServices?.Invoke(services);
        });
    }

    private Dictionary<string, string?> BuildSettings()
    {
        string connectionString = _overrides.TryGetValue("ConnectionStrings:Default", out string? custom)
            ? custom!
            : _environment.MigratedConnectionString;

        Dictionary<string, string?> settings = _environment.SettingsFor(connectionString);

        foreach ((string key, string? value) in _overrides)
        {
            settings[key] = value;
        }

        return settings;
    }
}