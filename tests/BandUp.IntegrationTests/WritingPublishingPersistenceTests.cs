using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Writing;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingPublishingPersistenceTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    private static int _clockSlot;

    private static TestClock NewClock()
    {
        // The shared test database retains rows from other tests. Each polling test starts
        // after all previous fixture deadlines without changing or deleting their submissions.
        var clock = new TestClock();
        var now = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Interlocked.Increment(ref _clockSlot));
        clock.Advance(now - clock.GetUtcNow());
        return clock;
    }

    [Fact]
    public async Task ActiveLeasePreventsDoubleReservationAndConfirmationPublishesOnlyOnce()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, id) = await Accept(factory);
        var baseline = await BusinessState(factory, owner);
        var reservation = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        Assert.Equal(id, reservation.SubmissionId);
        Assert.NotEqual(Guid.Empty, reservation.LeaseId);
        var leased = await Read(factory, id);
        Assert.Equal(1, leased.PublishAttempts);
        Assert.Equal(reservation.LeaseId, leased.PublishLeaseId);
        Assert.Equal(clock.GetUtcNow().AddSeconds(30), leased.PublishLeaseExpiresAt);
        Assert.Null(await Reserve(factory));
        Assert.False(await Outcome(factory, reservation with { SubmissionId = Guid.NewGuid() }, confirmed: true));
        Assert.False(await Outcome(factory, reservation with { LeaseId = Guid.NewGuid() }, confirmed: false));

        Assert.True(await Outcome(factory, reservation, confirmed: true));
        var published = await Read(factory, id);
        Assert.Equal(clock.GetUtcNow(), published.PublishedAt);
        Assert.Null(published.PublishLeaseId);
        Assert.Null(published.PublishLeaseExpiresAt);
        Assert.Null(published.NextPublishAttemptAt);
        Assert.Equal(1, published.PublishAttempts);
        Assert.False(await Outcome(factory, reservation, confirmed: true));
        Assert.Null(await Reserve(factory));
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    [Fact]
    public async Task WorkersSkipAnUncommittedRowLockAndReserveDistinctSubmissions()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (_, first) = await Accept(factory);
        var (_, second) = await Accept(factory);
        clock.Advance(TimeSpan.FromSeconds(1));
        var (_, third) = await Accept(factory);
        var ordered = new[] { first, second }.Order().Append(third).ToArray();
        using var holding = factory.Services.CreateScope();
        await Work(holding).BeginTransactionAsync(Ct);
        var held = Assert.IsType<WritingPublishReservation>(await Store(holding).ReserveAsync(Ct));
        Assert.Equal(ordered[0], held.SubmissionId);
        Assert.Equal(0, (await Read(factory, held.SubmissionId)).PublishAttempts);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var other = Assert.IsType<WritingPublishReservation>(await Reserve(factory, timeout.Token));
        Assert.Equal(ordered[1], other.SubmissionId);
        Assert.NotEqual(held.LeaseId, other.LeaseId);
        await Work(holding).CommitTransactionAsync(Ct);
        Assert.Equal(ordered[2], Assert.IsType<WritingPublishReservation>(await Reserve(factory)).SubmissionId);
        Assert.Null(await Reserve(factory));
        Assert.Equal(1, (await Read(factory, held.SubmissionId)).PublishAttempts);
    }

    [Fact]
    public async Task ExpiredAndSupersededLeasesCannotRecordEitherOutcome()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (_, id) = await Accept(factory);
        var first = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(await Outcome(factory, first, confirmed: true));
        Assert.False(await Outcome(factory, first, confirmed: false));
        var second = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        Assert.Equal(id, second.SubmissionId);
        Assert.NotEqual(first.LeaseId, second.LeaseId);
        Assert.False(await Outcome(factory, first, confirmed: true));
        Assert.False(await Outcome(factory, first, confirmed: false));
        var row = await Read(factory, id);
        Assert.Equal(2, row.PublishAttempts);
        Assert.Equal(second.LeaseId, row.PublishLeaseId);
        Assert.Null(row.PublishedAt);
        Assert.Null(row.NextPublishAttemptAt);
        Assert.True(await Outcome(factory, second, confirmed: true));
    }

    [Fact]
    public async Task FailedOrUncertainAttemptsBackOffUntilTheBudgetIsExhausted()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, id) = await Accept(factory);
        var baseline = await BusinessState(factory, owner);
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var lease = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
            Assert.Equal(id, lease.SubmissionId);
            Assert.True(await Outcome(factory, lease, confirmed: false));
            var row = await Read(factory, id);
            Assert.Equal(attempt, row.PublishAttempts);
            Assert.Null(row.PublishLeaseId);
            Assert.Null(row.PublishLeaseExpiresAt);
            Assert.Null(row.PublishedAt);
            Assert.Null(await Reserve(factory));

            DateTimeOffset? due = attempt < 5 ? clock.GetUtcNow().AddSeconds(1 << (attempt - 1)) : null;
            Assert.Equal(due, row.NextPublishAttemptAt);
            if (due is { } retryAt)
            {
                clock.Advance(retryAt - clock.GetUtcNow() - TimeSpan.FromMicroseconds(1));
                Assert.Null(await Reserve(factory));
                clock.Advance(TimeSpan.FromMicroseconds(1));
            }
        }
        Assert.Equal(baseline, await BusinessState(factory, owner));
        Assert.Equal(WritingEvaluationStatus.Queued, (await Read(factory, id)).Status);
        Assert.Null(await Reserve(factory));
    }

    [Fact]
    public async Task QueueDeadlineAndProcessingStateFencePublication()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, processing) = await Accept(factory);
        var old = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        Assert.Equal(processing, old.SubmissionId);
        Assert.True(await Dispatch<ClaimWriting, bool>(factory, new(processing), Ct));
        var baseline = await BusinessState(factory, owner);
        Assert.False(await Outcome(factory, old, confirmed: true));
        Assert.False(await Outcome(factory, old, confirmed: false));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await Reserve(factory));
        Assert.Equal(baseline, await BusinessState(factory, owner));
        Assert.True(await Dispatch<FailWriting, bool>(factory, new(processing), Ct));
        Assert.Null(await Reserve(factory));

        var (_, succeeded) = await Accept(factory);
        Assert.True(await Dispatch<ClaimWriting, bool>(factory, new(succeeded), Ct));
        var criterion = new CriterionFeedback(6, "Explanation", [], [], ["Add detail."]);
        var feedback = new WritingFeedback(succeeded, "Summary", criterion, criterion, criterion, criterion);
        Assert.True(await Dispatch<CompleteWriting, bool>(factory, new(succeeded, feedback), Ct));
        Assert.Null(await Reserve(factory));

        var (_, queued) = await Accept(factory);
        var (_, unleased) = await Accept(factory);
        var deadline = (await Read(factory, queued)).QueueDeadline;
        clock.Advance(deadline - clock.GetUtcNow() - TimeSpan.FromMicroseconds(1));
        var lease = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        Assert.Contains(lease.SubmissionId, new[] { queued, unleased });
        clock.Advance(TimeSpan.FromMicroseconds(1));
        Assert.Null(await Reserve(factory));
        Assert.False(await Outcome(factory, lease, confirmed: true));
        Assert.False(await Outcome(factory, lease, confirmed: false));
        var row = await Read(factory, lease.SubmissionId);
        Assert.Equal(WritingEvaluationStatus.Queued, row.Status);
        Assert.Equal(lease.LeaseId, row.PublishLeaseId);
        Assert.Null(row.PublishedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutcomeWaitingForRowLockChecksTimeAfterTheWait(bool confirmed)
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (_, id) = await Accept(factory);
        var reservation = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        using var holder = factory.Services.CreateScope();
        using var waiter = factory.Services.CreateScope();
        await Work(holder).BeginTransactionAsync(Ct);
        await Work(waiter).BeginTransactionAsync(Ct);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Db(holder).Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM writing.writing_submissions WHERE id = {id} FOR UPDATE
            """).ToListAsync(limit.Token);
        var pending = Store(waiter).RecordOutcomeAsync(reservation, confirmed, limit.Token);
        await WritingBillingTests.WaitForBlockAsync(Db(holder), Db(waiter), limit.Token);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Work(holder).CommitTransactionAsync(Ct);
        Assert.False(await pending);
        await Work(waiter).CommitTransactionAsync(Ct);
        var row = await Read(factory, id);
        Assert.Equal(reservation.LeaseId, row.PublishLeaseId);
        Assert.Null(row.PublishedAt);
        Assert.Null(row.NextPublishAttemptAt);
    }

    [Fact]
    public async Task ReservationSeesOnlyCommittedAcceptancesAndCanBeRolledBack()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, command) = await Seed(factory);
        using var acceptance = Scope(factory, owner);
        await Work(acceptance).BeginTransactionAsync(Ct);
        var accepted = (await acceptance.ServiceProvider.GetRequiredService<
            ICommandHandler<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>>().HandleAsync(command, Ct)).Value;
        await Work(acceptance).SaveChangesAsync(Ct);
        Assert.Null(await Reserve(factory));
        await Work(acceptance).CommitTransactionAsync(Ct);

        var baseline = await BusinessState(factory, owner);
        using var abandoned = factory.Services.CreateScope();
        var unit = Work(abandoned);
        await unit.BeginTransactionAsync(Ct);
        var rolledBack = Assert.IsType<WritingPublishReservation>(await Store(abandoned).ReserveAsync(Ct));
        await unit.RollbackTransactionAsync(Ct);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => unit.BeginTransactionAsync(Ct));
        var untouched = await Read(factory, accepted.SubmissionId);
        Assert.Equal(0, untouched.PublishAttempts);
        Assert.Null(untouched.PublishLeaseId);
        var committed = Assert.IsType<WritingPublishReservation>(await Reserve(factory));
        Assert.Equal(accepted.SubmissionId, committed.SubmissionId);
        Assert.NotEqual(rolledBack.LeaseId, committed.LeaseId);
        Assert.Equal(baseline, await BusinessState(factory, owner));
    }

    private static async Task<(Guid Owner, Guid Id)> Accept(BandUpApiFactory factory)
    {
        var (owner, command) = await Seed(factory);
        using var scope = Scope(factory, owner);
        return (owner, (await Send(scope, command)).Value.SubmissionId);
    }

    private static async Task<TResult> Dispatch<TCommand, TResult>(BandUpApiFactory factory, TCommand command, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        using var scope = factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        return (await dispatcher.SendAsync<TCommand, TResult>(command, ct)).Value;
    }

    private static Task<WritingPublishReservation?> Reserve(BandUpApiFactory factory, CancellationToken ct = default) =>
        Dispatch<ReserveWritingPublication, WritingPublishReservation?>(factory, new(), ct);

    private static Task<bool> Outcome(BandUpApiFactory factory, WritingPublishReservation reservation, bool confirmed) =>
        confirmed
            ? Dispatch<ConfirmWritingPublication, bool>(factory, new(reservation), Ct)
            : Dispatch<RecordWritingPublishFailure, bool>(factory, new(reservation), Ct);

    private static IWritingPublishingStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IWritingPublishingStore>();

    private static async Task<WritingSubmission> Read(BandUpApiFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await Db(scope).WritingSubmissions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private static async Task<string> BusinessState(BandUpApiFactory factory, Guid owner)
    {
        using var scope = factory.Services.CreateScope();
        var db = Db(scope);
        var account = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == owner);
        var ledger = await db.CreditLedger.AsNoTracking().Where(e => e.UserId == owner).OrderBy(e => e.Id).ToListAsync();
        var submission = await db.WritingSubmissions.AsNoTracking().SingleAsync(s => s.UserId == owner);
        var feedbackCount = await db.WritingFeedbacks.CountAsync(f => f.Id == submission.Id);
        return JsonSerializer.Serialize(new
        {
            account, ledger, feedbackCount, submission.Status, submission.IdempotencyKey, submission.PromptSnapshot,
            submission.EssayText, submission.SubmittedAt, submission.QueueDeadline, submission.StartedAt, submission.CompletedAt,
        });
    }
}
