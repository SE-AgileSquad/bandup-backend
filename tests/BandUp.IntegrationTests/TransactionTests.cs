using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// Proves the transaction behavior against a real database, on a scratch table that exists
/// only for this test. The unit tests cover the ordering; this covers the commit and rollback
/// actually reaching PostgreSQL.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class TransactionTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task INF_09_A_Successful_Command_Commits_Its_Write()
    {
        Guid note = Guid.CreateVersion7();

        await using BandUpApiFactory factory = new(
            environment,
            configureServices: services =>
                services.AddScoped<ICommandHandler<ScratchCommand, int>, ScratchHandler>());

        Result<int> result = await SendAsync(factory, new ScratchCommand(note, ShouldFail: false));

        result.IsSuccess.ShouldBeTrue();
        (await RowExistsAsync(note)).ShouldBeTrue();
    }

    [Fact]
    public async Task INF_09_A_Failed_Result_Rolls_The_Write_Back()
    {
        Guid note = Guid.CreateVersion7();

        await using BandUpApiFactory factory = new(
            environment,
            configureServices: services =>
                services.AddScoped<ICommandHandler<ScratchCommand, int>, ScratchHandler>());

        Result<int> result = await SendAsync(factory, new ScratchCommand(note, ShouldFail: true));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.ConflictCode);
        (await RowExistsAsync(note)).ShouldBeFalse();
    }

    [Fact]
    public async Task INF_09_An_Exception_Rolls_The_Write_Back_And_Propagates()
    {
        Guid note = Guid.CreateVersion7();

        await using BandUpApiFactory factory = new(
            environment,
            configureServices: services =>
                services.AddScoped<ICommandHandler<ScratchCommand, int>, ScratchHandler>());

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await SendAsync(factory, new ScratchCommand(note, ShouldFail: false, ShouldThrow: true)));

        (await RowExistsAsync(note)).ShouldBeFalse();
    }

    private static async Task<Result<int>> SendAsync(BandUpApiFactory factory, ScratchCommand command)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        return await dispatcher.SendAsync<ScratchCommand, int>(command, CancellationToken.None);
    }

    private async Task<bool> RowExistsAsync(Guid id)
    {
        await using NpgsqlConnection connection = new(environment.MigratedConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM scratch_probe WHERE id = $1";
        command.Parameters.AddWithValue(id);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    public sealed record ScratchCommand(Guid Note, bool ShouldFail, bool ShouldThrow = false)
        : ICommand<int>;

    /// <summary>
    /// Writes through the context so the write joins the transaction the behavior opened.
    /// Raw SQL is used because the scratch table has no entity mapping.
    /// </summary>
    private sealed class ScratchHandler(BandUpDbContext context) : ICommandHandler<ScratchCommand, int>
    {
        public async Task<Result<int>> HandleAsync(
            ScratchCommand command,
            CancellationToken cancellationToken)
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO scratch_probe (id, note) VALUES ({0}, {1})",
                [command.Note, "probe"],
                cancellationToken);

            if (command.ShouldThrow)
            {
                throw new InvalidOperationException("deliberate failure after the write");
            }

            return command.ShouldFail
                ? Result<int>.Failure(Error.Conflict("deliberate failure after the write"))
                : Result<int>.Success(1);
        }
    }
}