using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Hosting;
using BandUp.Infrastructure.Messaging;
using BandUp.Infrastructure.Security;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BandUp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IServerInfo, ServerInfo>();

        // The default identity is anonymous. The API replaces this with an HttpContext-based
        // implementation; the Worker keeps this one, because it has no request.
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        services.AddPersistence();
        services.AddCaching(configuration);
        services.AddMessaging(configuration);

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services
            .AddOptions<DatabaseSettings>()
            .BindConfiguration(DatabaseSettings.SectionName)
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Default),
                $"{DatabaseSettings.SectionName}:Default is required.")
            .ValidateOnStart();

        services.AddDbContext<BandUpDbContext>((provider, options) =>
        {
            DatabaseSettings settings = provider.GetRequiredService<IOptions<DatabaseSettings>>().Value;

            options.UseNpgsql(
                settings.Default,
                npgsql => npgsql.MigrationsHistoryTable(
                    BandUpDbContext.MigrationsHistoryTable,
                    BandUpDbContext.MigrationsHistorySchema));
        });

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRefreshTokenStore, EfRefreshTokenStore>();

        return services;
    }

    private static IServiceCollection AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<CacheSettings>()
            .BindConfiguration(CacheSettings.SectionName)
            .Validate(
                settings => ServiceProviders.Cache.Contains(settings.Provider, StringComparer.OrdinalIgnoreCase),
                $"{CacheSettings.SectionName}:Provider must be one of: {string.Join(", ", ServiceProviders.Cache)}.")
            .ValidateOnStart();

        string provider = configuration[$"{CacheSettings.SectionName}:Provider"] ?? ServiceProviders.Redis;

        if (string.Equals(provider, ServiceProviders.InMemory, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ICacheService, InMemoryCacheService>();
            return services;
        }

        services
            .AddOptions<RedisSettings>()
            .BindConfiguration(RedisSettings.SectionName)
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.ConnectionString),
                $"{RedisSettings.SectionName}:ConnectionString is required when " +
                $"{CacheSettings.SectionName}:Provider is {ServiceProviders.Redis}.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(providerServices =>
        {
            RedisSettings settings = providerServices.GetRequiredService<IOptions<RedisSettings>>().Value;

            ConfigurationOptions options = ConfigurationOptions.Parse(settings.ConnectionString!);

            // The cache is an optimisation, not a source of truth. Without this the first
            // resolution of ICacheService throws while a broker is down, which turns a
            // dependency outage into a 500 instead of a health check that reports it.
            options.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();

        return services;
    }

    private static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MessagePublisherSettings>()
            .BindConfiguration(MessagePublisherSettings.SectionName)
            .Validate(
                settings => ServiceProviders.MessagePublisher.Contains(settings.Provider, StringComparer.OrdinalIgnoreCase),
                $"{MessagePublisherSettings.SectionName}:Provider must be one of: " +
                $"{string.Join(", ", ServiceProviders.MessagePublisher)}.")
            .ValidateOnStart();

        services.AddSingleton(MessageRoutes.CreateDefault());
        services.AddScoped<IMessageBuffer, MessageBuffer>();

        string provider =
            configuration[$"{MessagePublisherSettings.SectionName}:Provider"] ?? ServiceProviders.RabbitMq;

        if (string.Equals(provider, ServiceProviders.InMemory, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDirectMessagePublisher, InMemoryMessagePublisher>();
            services.AddScoped<IMessagePublisher, BufferedMessagePublisher>();
            return services;
        }

        services
            .AddOptions<RabbitMqSettings>()
            .BindConfiguration(RabbitMqSettings.SectionName)
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Host),
                $"{RabbitMqSettings.SectionName}:Host is required.")
            .Validate(
                settings => settings.Port is > 0 and <= 65535,
                $"{RabbitMqSettings.SectionName}:Port must be between 1 and 65535.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.User),
                $"{RabbitMqSettings.SectionName}:User is required.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.Password),
                $"{RabbitMqSettings.SectionName}:Password is required.")
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<RabbitMqTopology>();
        services.AddSingleton<IDirectMessagePublisher, RabbitMqPublisher>();
        services.AddScoped<IMessagePublisher, BufferedMessagePublisher>();

        // Registered first so the exchange exists before any consumer starts reading.
        services.AddHostedService<RabbitMqTopologyInitializer>();

        return services;
    }
}