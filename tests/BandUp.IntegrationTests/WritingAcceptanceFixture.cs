using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Billing;
using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BandUp.IntegrationTests;

public abstract class WritingAcceptanceFixture(BandUpTestEnvironment environment)
{
    protected static readonly string Essay = string.Join(" ", Enumerable.Repeat("cafe\u0301", 100)) + "\n";
    protected BandUpTestEnvironment TestEnvironment { get; } = environment;

    protected static readonly CancellationToken Ct = CancellationToken.None;

    protected string MigratedConnectionString => TestEnvironment.MigratedConnectionString;
    protected BandUpApiFactory Factory(TimeProvider? clock = null) => new(TestEnvironment, configureServices: services =>
    {
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }
    });

    protected static IServiceScope Scope(BandUpApiFactory factory, Guid owner)
    {
        var scope = factory.Services.CreateScope();
        var caller = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        caller.IsAuthenticated.Returns(true);
        caller.UserId.Returns(owner);
        return scope;
    }

    protected static BandUpDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
    protected static IUnitOfWork Work(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    protected static Task<Result<WritingSubmissionAcceptedDto>> Send(IServiceScope scope, SubmitWritingEssayCommand command,
        CancellationToken ct = default) => scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>(command, ct);

    protected static async Task<(Guid Owner, SubmitWritingEssayCommand Command)> Seed(BandUpApiFactory factory, int? balance = 1)
    {
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var now = DateTimeOffset.UnixEpoch;
        var user = User.Create($"{Guid.NewGuid():N}@example.test", "Learner", "hash", now);
        var prompt = WritingPrompt.CreateDraft("Cities", "Discuss growth.", "Opinion", 100, 350, user.Id, now);
        prompt.Publish(now);
        db.AddRange(user, prompt);
        if (balance.HasValue)
        {
            var account = CreditAccount.Open(user.Id, 0, now);
            db.Add(account);
            if (balance.Value > 0)
            {
                db.Add(account.RecordTransaction(balance.Value, CreditDelta.Grant, CreditLedgerEntry.AdminGrantReason, now));
            }
        }
        await db.SaveChangesAsync();
        return (user.Id, new(Guid.NewGuid(), prompt.Id, Essay));
    }
}
