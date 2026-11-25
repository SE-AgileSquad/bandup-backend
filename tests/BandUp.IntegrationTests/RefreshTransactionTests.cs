using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Refresh;
using BandUp.Domain.Identity;
using BandUp.IntegrationTests.Infrastructure;
using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// Verifies ADR 0016 against PostgreSQL. The probe records revocations but deliberately does
/// not implement token lookup or reuse detection, which belong to the refresh handler.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class RefreshTransactionTests(BandUpTestEnvironment environment)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AUTH_09_Rejection_After_Revocation_Commits_Before_Dispatch_Returns()
    {
        Guid userId = await SeedSessionsAsync();

        await using BandUpApiFactory factory = CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Result<RefreshOutcome> result = await dispatcher.SendAsync<RevocationProbeCommand, RefreshOutcome>(
            new RevocationProbeCommand(userId, ShouldFail: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<RefreshOutcome.RejectedAfterRevocation>();

        // A separate context observes committed rows while the dispatch scope is still alive.
        await using BandUpDbContext verification = BandUpTestEnvironment.CreateContext(
            environment.MigratedConnectionString);
        List<RefreshToken> tokens = await verification.RefreshTokens
            .Where(token => token.UserId == userId)
            .ToListAsync();

        tokens.Count.ShouldBe(2);
        tokens.ShouldAllBe(token => token.RevokedAt == Now.AddMinutes(1));
    }

    [Fact]
    public async Task AUTH_09_Ordinary_Failed_Result_Rolls_Back_Revocations()
    {
        Guid userId = await SeedSessionsAsync();

        await using BandUpApiFactory factory = CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        IDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Result<RefreshOutcome> result = await dispatcher.SendAsync<RevocationProbeCommand, RefreshOutcome>(
            new RevocationProbeCommand(userId, ShouldFail: true),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.InvalidRefreshTokenCode);

        await using BandUpDbContext verification = BandUpTestEnvironment.CreateContext(
            environment.MigratedConnectionString);
        List<RefreshToken> tokens = await verification.RefreshTokens
            .Where(token => token.UserId == userId)
            .ToListAsync();

        tokens.Count.ShouldBe(2);
        tokens.ShouldAllBe(token => token.RevokedAt == null);
    }

    private BandUpApiFactory CreateFactory() => new(
        environment,
        configureServices: services => services
            .AddScoped<ICommandHandler<RevocationProbeCommand, RefreshOutcome>, RevocationProbeHandler>());

    private async Task<Guid> SeedSessionsAsync()
    {
        await using BandUpDbContext context = BandUpTestEnvironment.CreateContext(
            environment.MigratedConnectionString);

        User user = User.Create(
            $"refresh-transaction-{Guid.CreateVersion7():N}@example.com",
            "Refresh transaction probe",
            "test-only-password-hash",
            Now);
        context.Users.Add(user);

        for (int index = 0; index < 2; index++)
        {
            context.RefreshTokens.Add(RefreshToken.Issue(
                user.Id,
                $"{user.Id:N}{Guid.CreateVersion7():N}",
                Now,
                Now.AddDays(7),
                null));
        }

        await context.SaveChangesAsync();

        return user.Id;
    }

    public sealed record RevocationProbeCommand(Guid UserId, bool ShouldFail) : ICommand<RefreshOutcome>;

    private sealed class RevocationProbeHandler(IRefreshTokenStore store, IUnitOfWork unitOfWork)
        : ICommandHandler<RevocationProbeCommand, RefreshOutcome>
    {
        public async Task<Result<RefreshOutcome>> HandleAsync(
            RevocationProbeCommand command,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<RefreshToken> tokens = await store.ListUsableForUserAsync(
                command.UserId,
                cancellationToken);

            foreach (RefreshToken token in tokens)
            {
                token.Revoke(Now.AddMinutes(1));
            }

            // Flush to PostgreSQL so the failure test proves transaction rollback, not just
            // the absence of the pipeline's final SaveChanges call.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return command.ShouldFail
                ? Result<RefreshOutcome>.Failure(Error.InvalidRefreshToken())
                : Result<RefreshOutcome>.Success(new RefreshOutcome.RejectedAfterRevocation());
        }
    }
}
