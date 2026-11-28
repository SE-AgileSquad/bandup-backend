using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// Starts the dependencies once for the whole run. Nothing here touches the development
/// compose stack: the containers are throwaway and their ports are assigned by Docker.
/// </summary>
public sealed class BandUpTestEnvironment : IAsyncLifetime
{
    public const string Database = "bandup";
    public const string PendingDatabase = "bandup_pending";
    public const string UserName = "bandup";
    public const string Password = "bandup";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase(Database)
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();

    /// <summary>Connection string for a database whose migrations have been applied.</summary>
    public string MigratedConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Connection string for a database that exists but has no schema, so every migration is
    /// still pending. Used to prove <c>/ready</c> withholds traffic before a migration.
    /// </summary>
    public string UnmigratedConnectionString { get; private set; } = string.Empty;

    public string RedisConnectionString { get; private set; } = string.Empty;

    public string RabbitMqHost { get; private set; } = string.Empty;

    public int RabbitMqPort { get; private set; }

    public string RabbitMqUser { get; private set; } = string.Empty;

    public string RabbitMqPassword { get; private set; } = string.Empty;

    /// <summary>A port nothing listens on, to simulate a dependency being down.</summary>
    public static int UnreachablePort { get; } = FindUnreachablePort();

    public Dictionary<string, string?> SettingsFor(string connectionString) => new()
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["Redis:ConnectionString"] = RedisConnectionString,
        ["Cache:Provider"] = "Redis",
        ["RabbitMq:Host"] = RabbitMqHost,
        ["RabbitMq:Port"] = RabbitMqPort.ToString(),
        ["RabbitMq:User"] = RabbitMqUser,
        ["RabbitMq:Password"] = RabbitMqPassword,
        ["MessagePublisher:Provider"] = "RabbitMq",
        ["Cors:AllowedOrigin"] = "http://localhost:3000",
        ["ForwardedHeaders:Enabled"] = "false",
        ["Swagger:Enabled"] = "true",
        ["Features:Diagnostics"] = "true",
        ["Sentry:Dsn"] = string.Empty,

        // Not a secret: a fixed key so tests are reproducible and never read user secrets.
        ["Auth:SigningKey"] = "integration-test-signing-key-not-for-any-real-use-0123456789",
        ["Auth:RequireSecureCookies"] = "false",
        ["Captcha:Provider"] = "Fake",
        ["PasswordReset:CodeProtectionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()),
        ["PasswordReset:RateLimitProvider"] = "InMemory",
        ["OtpLogin:CodeProtectionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)43, 32).ToArray()),
        ["OtpLogin:RateLimitProvider"] = "InMemory",
        ["Email:Provider"] = "InMemory",
        ["Email:FromEmail"] = "tests@bandup.local",
        ["Email:FromName"] = "BandUp tests",
        ["Email:QueueCapacity"] = "256",
        ["Email:TimeoutSeconds"] = "5",
    };

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq.StartAsync());

        MigratedConnectionString = _postgres.GetConnectionString();
        RedisConnectionString = _redis.GetConnectionString();
        RabbitMqHost = _rabbitMq.Hostname;
        RabbitMqPort = _rabbitMq.GetMappedPublicPort(5672);
        RabbitMqUser = UserName;
        RabbitMqPassword = Password;

        await using (NpgsqlConnection connection = new(MigratedConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"CREATE DATABASE {PendingDatabase}");
        }

        UnmigratedConnectionString = new NpgsqlConnectionStringBuilder(MigratedConnectionString)
        {
            Database = PendingDatabase,
        }.ConnectionString;

        await using (BandUpDbContext context = CreateContext(MigratedConnectionString))
        {
            // The same call the --migrate argument makes.
            await context.Database.MigrateAsync();

            // Test-only table that the transaction scenario writes to. It is created through
            // raw SQL rather than an entity mapping, so no product code has to know about it.
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS scratch_probe (id uuid PRIMARY KEY, note text NOT NULL)");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();

        // Last, so the containers are always cleaned up even when the coverage gate fails.
        EndpointHitRecorder.WriteReportAndEnforceMinimum();
    }

    public static BandUpDbContext CreateContext(string connectionString)
    {
        DbContextOptions<BandUpDbContext> options = new DbContextOptionsBuilder<BandUpDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BandUpDbContext(options);
    }

    /// <summary>Isolates real Worker tests from Writing rows left by other integration tests.</summary>
    public async Task<string> CreateIsolatedMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = $"bandup_worker_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(MigratedConnectionString))
        {
            await admin.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        var connectionString = new NpgsqlConnectionStringBuilder(MigratedConnectionString) { Database = database }.ConnectionString;
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Asks the OS for a free port and then closes the listener, so the port is almost
    /// certainly unused. Used to point a service at something that refuses connections.
    /// </summary>
    private static int FindUnreachablePort()
    {
        System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}
