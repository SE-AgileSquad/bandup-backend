using BandUp.Application.Abstractions;
using BandUp.Application.Features.Vocabulary;
using BandUp.Application.Features.Content.Admin;
using BandUp.Application.Features.Media.Admin;
using BandUp.Infrastructure.Media;
using BandUp.Application.Features.Learning.Admin;
using BandUp.Application.Features.Billing;
using BandUp.Application.Features.Dashboard.Admin;
using BandUp.Application.Features.Exams;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Learning;
using BandUp.Application.Features.Learning.Admin.Mappings;
using BandUp.Application.Features.Reading;
using BandUp.Application.Features.Writing.Admin;
using BandUp.Infrastructure.Caching;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Hosting;
using BandUp.Infrastructure.Messaging;
using BandUp.Infrastructure.Security;
using BandUp.Infrastructure.Persistence;
using BandUp.Infrastructure.Persistence.Queries.Admin;
using BandUp.Infrastructure.Persistence.Queries.Exams;
using BandUp.Infrastructure.Persistence.Queries.Learning;
using BandUp.Infrastructure.Persistence.Queries.Reading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        services.AddSecurity(configuration);
        services.AddEmailDelivery();
        services.AddBilling();
        services.AddOptions<CloudinarySettings>().BindConfiguration("Cloudinary");
        services.AddHttpClient("CloudinaryImages", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddSingleton<IImageStorage, CloudinaryImageStorage>();
        services.AddScoped<IAdminPhotoStore, EfAdminPhotoStore>();
        services.AddSingleton<ImageCleanupService>();
        services.AddHostedService<ImageCleanupWorker>();

        return services;
    }

    private static IServiceCollection AddSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Hashing has no configuration and no per-request state. The token service reads the
        // signing key once at startup, so it is a singleton too; a key rotation restarts the
        // process, which the ADR accepts.
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        services
            .AddOptions<AuthSettings>()
            .BindConfiguration(AuthSettings.SectionName)
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.SigningKey),
                $"{AuthSettings.SectionName}:SigningKey is required.")
            .Validate(
                settings => settings.SigningKey is { Length: >= 32 },
                $"{AuthSettings.SectionName}:SigningKey must be at least 32 characters.")
            .Validate(
                settings => settings.AccessTokenMinutes is > 0 and <= 60,
                $"{AuthSettings.SectionName}:AccessTokenMinutes must be between 1 and 60.")
            .Validate(
                settings => settings.RefreshTokenDays is > 0 and <= 90,
                $"{AuthSettings.SectionName}:RefreshTokenDays must be between 1 and 90.");

        // Validation runs when ITokenService is first resolved, not at startup. Only the API
        // issues tokens, so only the API fails fast; the Worker never resolves this service.
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ISessionTokenIssuer, SessionTokenIssuer>();
        services.AddSingleton<IAccessTokenRevocationStore, CachedAccessTokenRevocationStore>();

        services.AddOptions<PasswordResetSettings>()
            .BindConfiguration(PasswordResetSettings.SectionName)
            .Validate(settings => PasswordResetCodeProtector.IsValidKey(settings.CodeProtectionKey),
                "PasswordReset:CodeProtectionKey must be Base64 encoding at least 32 random bytes.")
            .Validate(settings => ServiceProviders.PasswordResetRateLimit.Contains(settings.RateLimitProvider, StringComparer.OrdinalIgnoreCase),
                "PasswordReset:RateLimitProvider must be Redis or InMemory.");
        // The API opts into startup validation. Other hosts do not require a recovery key
        // unless they resolve recovery services.
        services.AddSingleton<IPasswordResetCodeProtector, PasswordResetCodeProtector>();
        services.AddSingleton<PasswordResetRateLimitKeys>();
        services.AddSingleton<IPasswordResetRateLimiter>(provider =>
            string.Equals(provider.GetRequiredService<IOptions<PasswordResetSettings>>().Value.RateLimitProvider,
                ServiceProviders.InMemory, StringComparison.OrdinalIgnoreCase)
                ? new InMemoryPasswordResetRateLimiter(provider.GetRequiredService<PasswordResetRateLimitKeys>(), provider.GetRequiredService<TimeProvider>())
                : new RedisPasswordResetRateLimiter(provider.GetRequiredService<IConnectionMultiplexer>(), provider.GetRequiredService<PasswordResetRateLimitKeys>()));

        services.AddOptions<OtpLoginSettings>()
            .BindConfiguration(OtpLoginSettings.SectionName)
            .Validate(settings => OtpLoginCodeProtector.IsValidKey(settings.CodeProtectionKey),
                "OtpLogin:CodeProtectionKey must be Base64 encoding at least 32 random bytes.")
            .Validate(settings => ServiceProviders.OtpLoginRateLimit.Contains(settings.RateLimitProvider, StringComparer.OrdinalIgnoreCase),
                "OtpLogin:RateLimitProvider must be Redis or InMemory.");
        services.AddSingleton<IOtpLoginCodeProtector, OtpLoginCodeProtector>();
        services.AddSingleton<OtpLoginRateLimitKeys>();
        services.AddSingleton<IOtpLoginRateLimiter>(provider =>
            string.Equals(provider.GetRequiredService<IOptions<OtpLoginSettings>>().Value.RateLimitProvider,
                ServiceProviders.InMemory, StringComparison.OrdinalIgnoreCase)
                ? new InMemoryOtpLoginRateLimiter(provider.GetRequiredService<OtpLoginRateLimitKeys>(), provider.GetRequiredService<TimeProvider>())
                : new RedisOtpLoginRateLimiter(provider.GetRequiredService<IConnectionMultiplexer>(), provider.GetRequiredService<OtpLoginRateLimitKeys>()));

        services
            .AddOptions<CaptchaSettings>()
            .BindConfiguration(CaptchaSettings.SectionName)
            .Validate(
                settings => settings.FallbackWindowMinutes is > 0 and <= 1440,
                $"{CaptchaSettings.SectionName}:FallbackWindowMinutes must be between 1 and 1440.")
            .Validate(
                settings => settings.FallbackMaxAttempts is > 0 and <= 100,
                $"{CaptchaSettings.SectionName}:FallbackMaxAttempts must be between 1 and 100.")
            .ValidateOnStart();

        services.AddHttpClient<TurnstileCaptchaVerifier>();

        string captchaProvider = configuration[$"{CaptchaSettings.SectionName}:Provider"]
            ?? ServiceProviders.Fake;

        if (string.Equals(captchaProvider, ServiceProviders.Turnstile, StringComparison.OrdinalIgnoreCase))
        {
            services
                .AddOptions<CaptchaSettings>()
                .Validate(
                    settings => !string.IsNullOrWhiteSpace(settings.SecretKey),
                    $"{CaptchaSettings.SectionName}:SecretKey is required when " +
                    $"{CaptchaSettings.SectionName}:Provider is {ServiceProviders.Turnstile}.")
                .ValidateOnStart();

            services.AddSingleton<ICaptchaVerifier, OutageTolerantCaptchaVerifier>(provider =>
                new OutageTolerantCaptchaVerifier(
                    provider.GetRequiredService<TurnstileCaptchaVerifier>(),
                    provider.GetRequiredService<ICacheService>(),
                    provider.GetRequiredService<IOptions<CaptchaSettings>>().Value,
                    provider.GetRequiredService<ILogger<OutageTolerantCaptchaVerifier>>()));
        }
        else
        {
            services.AddSingleton<ICaptchaVerifier, OutageTolerantCaptchaVerifier>(provider =>
                new OutageTolerantCaptchaVerifier(
                    new FakeCaptchaVerifier(),
                    provider.GetRequiredService<ICacheService>(),
                    provider.GetRequiredService<IOptions<CaptchaSettings>>().Value,
                    provider.GetRequiredService<ILogger<OutageTolerantCaptchaVerifier>>()));
        }

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
        services.AddScoped<IVocabularyStore, EfVocabularyStore>();
        services.AddScoped<IAdminPassageStore, EfAdminPassageStore>();
        services.AddScoped<IAdminLessonStore, EfAdminLessonStore>();
        services.AddScoped<IAdminLessonMappingStore, EfAdminLessonMappingStore>();
        services.AddScoped<IAdminWritingPromptStore, EfAdminWritingPromptStore>();
        services.AddScoped<IAdminDashboardQueries, AdminDashboardQueries>();
        services.AddScoped<IRefreshTokenStore, EfRefreshTokenStore>();
        services.AddScoped<IPasswordResetChallengeStore, EfPasswordResetChallengeStore>();
        services.AddScoped<IOtpLoginChallengeStore, EfOtpLoginChallengeStore>();
        services.AddScoped<ICreditAccountRepository, EfCreditAccountRepository>();
        services.AddScoped<ICreditLedgerRepository, EfCreditLedgerRepository>();

        services.AddScoped<IAttemptRepository, EfAttemptRepository>();
        services.AddScoped<IAnswerKeyQueries, AnswerKeyQueries>();
        services.AddScoped<IAttemptQueries, AttemptQueries>();
        services.AddScoped<IReadingQueries, ReadingQueries>();
        services.AddScoped<ILessonQueries, LessonQueries>();

        return services;
    }

    private static IServiceCollection AddBilling(this IServiceCollection services)
    {
        services.AddScoped<ICreditService, CreditService>();
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
