using System.Diagnostics;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class MigrationTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task INF_10_The_Initial_Migration_Creates_The_Vector_And_Citext_Extensions()
    {
        await using NpgsqlConnection connection = new(environment.MigratedConnectionString);
        await connection.OpenAsync();

        Dictionary<string, string> extensions = await ReadExtensionsAsync(connection);

        extensions.ShouldContainKey("vector");
        extensions.ShouldContainKey("citext");
    }

    [Fact]
    public async Task INF_10_The_Migration_Is_Recorded_In_The_Public_Schema()
    {
        await using NpgsqlConnection connection = new(environment.MigratedConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_schema
            FROM information_schema.tables
            WHERE table_name = '__EFMigrationsHistory'
            """;

        (await command.ExecuteScalarAsync() as string).ShouldBe(BandUpDbContext.MigrationsHistorySchema);
    }

    [Fact]
    public async Task INF_10_Applying_The_Migration_Twice_Is_A_No_Op()
    {
        await using BandUpDbContext context = BandUpTestEnvironment.CreateContext(
            environment.MigratedConnectionString);

        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task INF_10_Api_Migrate_Argument_Applies_The_Schema_And_Exits()
    {
        string databaseName = "bandup_cli_" + Guid.CreateVersion7().ToString("N");
        await using (NpgsqlConnection admin = new(environment.MigratedConnectionString))
        {
            await admin.OpenAsync();
            await using NpgsqlCommand create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE {databaseName}";
            await create.ExecuteNonQueryAsync();
        }

        string connectionString = new NpgsqlConnectionStringBuilder(environment.MigratedConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--migrate");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        foreach ((string key, string? value) in environment.SettingsFor(connectionString))
        {
            start.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
        }

        using Process process = new() { StartInfo = start };
        process.Start().ShouldBeTrue();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        process.ExitCode.ShouldBe(0, (await errors) + (await output));
        await using BandUpDbContext verification = BandUpTestEnvironment.CreateContext(connectionString);
        (await verification.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        Dictionary<string, string> extensions = await ReadExtensionsAsync(connection);
        extensions.ShouldContainKey("vector");
        extensions.ShouldContainKey("citext");
    }

    private static async Task<Dictionary<string, string>> ReadExtensionsAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT extname, extversion FROM pg_extension";
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        Dictionary<string, string> extensions = [];

        while (await reader.ReadAsync())
        {
            extensions[reader.GetString(0)] = reader.GetString(1);
        }

        return extensions;
    }
}
