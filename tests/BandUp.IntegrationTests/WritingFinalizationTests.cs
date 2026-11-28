using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Billing;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingFinalizationTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    private static TestClock NewClock()
    {
        var clock = new TestClock();
        // PostgreSQL stores timestamps in whole microseconds.
        clock.Advance(TimeSpan.FromTicks(-(clock.GetUtcNow().Ticks % 10)));
        return clock;
    }

    private static WritingFeedback Review(Guid id, string excerpt = "cafe\u0301")
    {
        var criterion = new CriterionFeedback(6.5m, "Explanation", [], [], ["Add detail"],
            [new WritingEvidence(excerpt, "Observation")]);
        return new WritingFeedback(id, "Summary", criterion, criterion, criterion, criterion);
    }

    private static async Task<(Guid Owner, Guid Id)> Accepted(BandUpApiFactory factory, bool processing = false)
    {
        var (owner, command) = await Seed(factory);
        using var request = Scope(factory, owner);
        var id = (await Send(request, command)).Value.SubmissionId;
        if (processing)
        {
            (await Dispatch(factory, owner, new ClaimWriting(id))).Value.ShouldBeTrue();
        }
        return (owner, id);
    }

    private static async Task<Result<bool>> Dispatch<T>(BandUpApiFactory factory, Guid owner, T command)
        where T : ICommand<bool>
    {
        using var scope = Scope(factory, owner);
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync<T, bool>(command, Ct);
    }

    private static Task<Result<bool>> Direct<T>(IServiceScope scope, T command, CancellationToken ct)
        where T : ICommand<bool> =>
        scope.ServiceProvider.GetRequiredService<ICommandHandler<T, bool>>().HandleAsync(command, ct);

    private static async Task<Result<bool>> Operation(IServiceScope scope, Guid owner, Guid id, string operation,
        CancellationToken ct)
    {
        switch (operation)
        {
            case "claim": return await Direct(scope, new ClaimWriting(id), ct);
            case "expire": return await Direct(scope, new ExpireWriting(id), ct);
            case "complete": return await Direct(scope, new CompleteWriting(id, Review(id)), ct);
            case "fail": return await Direct(scope, new FailWriting(id), ct);
            case "admin":
                var result = await scope.ServiceProvider.GetRequiredService<ICreditService>()
                    .AdjustAsync(owner, 1, owner, Guid.NewGuid(), null, ct);
                return result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Error!);
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task FlushAndCommit(IServiceScope scope)
    {
        await Work(scope).SaveChangesAsync(Ct);
        await Work(scope).CommitTransactionAsync(Ct);
    }

    private static async Task CheckSettlement(BandUpApiFactory factory, Guid owner, Guid id, WritingEvaluationStatus status,
        int balance, int reservations, int adjustments = 0)
    {
        using var scope = Scope(factory, owner);
        var db = Db(scope);
        var row = await db.WritingSubmissions.SingleAsync(s => s.Id == id);
        row.Status.ShouldBe(status);
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(balance);
        account.ReservedRefundCapacity.ShouldBe(reservations);
        var entries = await db.CreditLedger.Where(e => e.ReferenceId == id).ToListAsync();
        entries.All(e => e.UserId == owner).ShouldBeTrue();
        entries.Single(e => e.Reason == CreditLedgerEntry.WritingChargeReason).Delta.ShouldBe(-1);
        entries.Count.ShouldBe(status == WritingEvaluationStatus.Failed ? 2 : 1);
        if (status == WritingEvaluationStatus.Failed)
        {
            entries.Single(e => e.Reason == CreditLedgerEntry.WritingRefundReason).Delta.ShouldBe(1);
        }
        (await db.CreditLedger.CountAsync(e => e.UserId == owner && e.Reason == CreditLedgerEntry.AdminAdjustReason))
            .ShouldBe(adjustments);
        var feedback = await db.WritingFeedbacks.SingleOrDefaultAsync(f => f.Id == id);
        if (status == WritingEvaluationStatus.Succeeded)
        {
            feedback!.MatchesFeedback(Review(id)).ShouldBeTrue();
        }
        else
        {
            feedback.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData("claim", "claim", false)]
    [InlineData("claim", "expire", false)]
    [InlineData("expire", "claim", false)]
    [InlineData("complete", "fail", false)]
    [InlineData("fail", "complete", false)]
    [InlineData("complete", "complete", true)]
    [InlineData("fail", "fail", true)]
    [InlineData("expire", "expire", true)]
    [InlineData("admin", "complete", true)]
    [InlineData("complete", "admin", true)]
    [InlineData("admin", "fail", true)]
    [InlineData("fail", "admin", true)]
    public async Task FinalizationSerializesAtTheDatabaseLock(string first, string second, bool secondAllowed)
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var queued = first is "claim" or "expire";
        var (owner, id) = await Accepted(factory, processing: !queued);
        var deadline = clock.GetUtcNow().AddMinutes(5);
        clock.Advance((first == "expire" ? deadline : deadline.AddMicroseconds(-1)) - clock.GetUtcNow());
        using var holder = Scope(factory, owner);
        using var waiter = Scope(factory, owner);
        await Work(holder).BeginTransactionAsync(Ct);
        await Work(waiter).BeginTransactionAsync(Ct);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        (await Operation(holder, owner, id, first, limit.Token)).Value.ShouldBeTrue();
        if (second == "expire")
        {
            clock.Advance(deadline - clock.GetUtcNow());
        }
        var blocked = Operation(waiter, owner, id, second, limit.Token);
        await WritingBillingTests.WaitForBlockAsync(Db(holder), Db(waiter), limit.Token);
        await FlushAndCommit(holder);
        var next = await blocked;
        next.IsSuccess.ShouldBe(secondAllowed);
        if (next.IsSuccess)
        {
            next.Value.ShouldBe(first != second);
            await FlushAndCommit(waiter);
        }
        else
        {
            await Work(waiter).RollbackTransactionAsync(Ct);
        }

        var outcome = first == "admin" ? second : first;
        var status = outcome switch
        {
            "claim" => WritingEvaluationStatus.Processing,
            "complete" => WritingEvaluationStatus.Succeeded,
            _ => WritingEvaluationStatus.Failed
        };
        var admin = first == "admin" || second == "admin" ? 1 : 0;
        await CheckSettlement(factory, owner, id, status, admin + (status == WritingEvaluationStatus.Failed ? 1 : 0),
            status == WritingEvaluationStatus.Processing ? 1 : 0, admin);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task RolledBackFinalizationCanBeRetriedFromFreshScope(bool success, bool flush)
    {
        await using var factory = Factory();
        var (owner, id) = await Accepted(factory, processing: true);
        using var transaction = Scope(factory, owner);
        await Work(transaction).BeginTransactionAsync(Ct);
        (await Operation(transaction, owner, id, success ? "complete" : "fail", Ct)).Value.ShouldBeTrue();
        if (flush)
        {
            await Work(transaction).SaveChangesAsync(Ct);
        }
        await Work(transaction).RollbackTransactionAsync(Ct);
        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Processing, 0, 1);
        var retry = success ? await Dispatch(factory, owner, new CompleteWriting(id, Review(id)))
            : await Dispatch(factory, owner, new FailWriting(id));
        retry.Value.ShouldBeTrue();
        await CheckSettlement(factory, owner, id, success ? WritingEvaluationStatus.Succeeded : WritingEvaluationStatus.Failed,
            success ? 0 : 1, 0);
    }

    [Fact]
    public async Task WaitingClaimReadsServerTimeAfterAcquiringSubmissionLock()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, id) = await Accepted(factory);
        using var holder = Scope(factory, owner);
        using var waiter = Scope(factory, owner);
        await Work(holder).BeginTransactionAsync(Ct);
        await Work(waiter).BeginTransactionAsync(Ct);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var store = holder.ServiceProvider.GetRequiredService<IWritingSubmissionStore>();
        var queued = await store.LockSubmissionAsync(id, limit.Token);
        var claim = Direct(waiter, new ClaimWriting(id), limit.Token);
        await WritingBillingTests.WaitForBlockAsync(Db(holder), Db(waiter), limit.Token);
        clock.Advance(queued!.QueueDeadline - clock.GetUtcNow());
        await FlushAndCommit(holder);
        (await claim).IsFailure.ShouldBeTrue();
        await Work(waiter).RollbackTransactionAsync(Ct);
        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Queued, 0, 1);
    }

    [Fact]
    public async Task DeadlinePartitionsClaimAndExpiryOnAnUnsettledRow()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, id) = await Accepted(factory);
        var deadline = clock.GetUtcNow().AddMinutes(5);
        clock.Advance(deadline.AddMicroseconds(-1) - clock.GetUtcNow());
        (await Dispatch(factory, owner, new ExpireWriting(id))).IsFailure.ShouldBeTrue();
        (await Dispatch(factory, owner, new FailWriting(id))).IsFailure.ShouldBeTrue();
        clock.Advance(deadline - clock.GetUtcNow());
        (await Dispatch(factory, owner, new ClaimWriting(id))).IsFailure.ShouldBeTrue();
        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Queued, 0, 1);
        (await Dispatch(factory, owner, new ExpireWriting(id))).Value.ShouldBeTrue();
        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Failed, 1, 0);
    }

    [Fact]
    public async Task TerminalReplayCannotConsumeAnotherSubmissionsReservation()
    {
        var clock = NewClock();
        await using var factory = Factory(clock);
        var (owner, command) = await Seed(factory, 2);
        using var requests = Scope(factory, owner);
        var first = (await Send(requests, command)).Value.SubmissionId;
        var other = (await Send(requests, command with { IdempotencyKey = Guid.NewGuid() })).Value.SubmissionId;
        clock.Advance(TimeSpan.FromMinutes(1));
        var started = clock.GetUtcNow();
        (await Dispatch(factory, owner, new ClaimWriting(first))).Value.ShouldBeTrue();
        (await Dispatch(factory, owner, new ClaimWriting(first))).IsFailure.ShouldBeTrue();
        clock.Advance(TimeSpan.FromMinutes(1));
        var completed = clock.GetUtcNow();
        (await Dispatch(factory, owner, new CompleteWriting(first, Review(first)))).Value.ShouldBeTrue();
        clock.Advance(TimeSpan.FromHours(1));
        (await Dispatch(factory, owner, new CompleteWriting(first, Review(first)))).Value.ShouldBeFalse();
        (await Dispatch(factory, owner, new CompleteWriting(first, Review(first, "caf")))).IsFailure.ShouldBeTrue();
        (await Dispatch(factory, owner, new FailWriting(first))).IsFailure.ShouldBeTrue();
        await CheckSettlement(factory, owner, first, WritingEvaluationStatus.Succeeded, 0, 1);
        (await Dispatch(factory, owner, new ClaimWriting(other))).IsFailure.ShouldBeTrue();
        (await Dispatch(factory, owner, new ExpireWriting(other))).Value.ShouldBeTrue();
        var expired = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromDays(1));
        (await Dispatch(factory, owner, new ExpireWriting(other))).Value.ShouldBeFalse();
        await CheckSettlement(factory, owner, other, WritingEvaluationStatus.Failed, 1, 0);
        using var read = Scope(factory, owner);
        var rows = await Db(read).WritingSubmissions.Where(s => s.UserId == owner).ToDictionaryAsync(s => s.Id);
        rows[first].StartedAt.ShouldBe(started);
        rows[first].CompletedAt.ShouldBe(completed);
        rows[other].StartedAt.ShouldBeNull();
        rows[other].CompletedAt.ShouldBe(expired);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChangesRejectsDirectTrackedWritingMutations(bool asynchronous)
    {
        await using var factory = Factory();
        var (owner, id) = await Accepted(factory, processing: true);
        using var insertion = Scope(factory, owner);
        Db(insertion).Set<WritingFeedback>().Add(Review(id));
        await RejectSave(Db(insertion));

        using var transition = Scope(factory, owner);
        var db = Db(transition);
        var row = await db.WritingSubmissions.SingleAsync(s => s.Id == id);
        db.Entry(row).Property(s => s.Status).CurrentValue = WritingEvaluationStatus.Failed;
        db.Entry(row).Property(s => s.CompletedAt).CurrentValue = row.StartedAt;
        await RejectSave(db);
        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Processing, 0, 1);

        (await Dispatch(factory, owner, new CompleteWriting(id, Review(id)))).Value.ShouldBeTrue();
        using var modification = Scope(factory, owner);
        var feedbackDb = Db(modification);
        var feedback = await feedbackDb.Set<WritingFeedback>().SingleAsync(f => f.Id == id);
        feedbackDb.Entry(feedback).Property(f => f.Content).CurrentValue = Review(id, "caf").Content;
        await RejectSave(feedbackDb);
        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Succeeded, 0, 0);

        async Task RejectSave(BandUpDbContext context)
        {
            if (asynchronous)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    context.SaveChanges();
                });
            }
        }
    }

    [Fact]
    public async Task PendingTerminalTransitionIsExcludedFromOutstandingCountBeforeAnotherAcceptance()
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory);
        using var request = Scope(factory, owner);
        var id = (await Send(request, command)).Value.SubmissionId;
        (await Dispatch(factory, owner, new ClaimWriting(id))).Value.ShouldBeTrue();

        using var operations = Scope(factory, owner);
        await Work(operations).BeginTransactionAsync(Ct);
        (await Direct(operations, new FailWriting(id), Ct)).Value.ShouldBeTrue();
        var next = command with { IdempotencyKey = Guid.NewGuid() };
        var handler = operations.ServiceProvider.GetRequiredService<
            ICommandHandler<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>>();
        var replacement = (await handler.HandleAsync(next, Ct)).Value.SubmissionId;
        await FlushAndCommit(operations);

        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Failed, 0, 1);
        await CheckSettlement(factory, owner, replacement, WritingEvaluationStatus.Queued, 0, 1);
    }

    [Fact]
    public async Task RecoveryMetadataCanBeSavedWithoutLifecycleAuthorization()
    {
        await using var factory = Factory();
        var (owner, id) = await Accepted(factory);
        using var update = Scope(factory, owner);
        var db = Db(update);
        var row = await db.WritingSubmissions.SingleAsync(s => s.Id == id);
        var entry = db.Entry(row);
        var published = row.SubmittedAt.AddSeconds(1);
        var nextAttempt = published.AddSeconds(1);
        var lease = Guid.NewGuid();
        var leaseExpires = nextAttempt.AddSeconds(10);
        entry.Property(s => s.PublishAttempts).CurrentValue = 1;
        entry.Property(s => s.NextPublishAttemptAt).CurrentValue = nextAttempt;
        entry.Property(s => s.PublishLeaseId).CurrentValue = lease;
        entry.Property(s => s.PublishLeaseExpiresAt).CurrentValue = leaseExpires;
        entry.Property(s => s.PublishedAt).CurrentValue = published;
        await db.SaveChangesAsync();
        using var fresh = Scope(factory, owner);
        var saved = await Db(fresh).WritingSubmissions.SingleAsync(s => s.Id == id);
        saved.PublishAttempts.ShouldBe(1);
        saved.NextPublishAttemptAt.ShouldBe(nextAttempt);
        saved.PublishLeaseId.ShouldBe(lease);
        saved.PublishLeaseExpiresAt.ShouldBe(leaseExpires);
        saved.PublishedAt.ShouldBe(published);
        saved.Status.ShouldBe(WritingEvaluationStatus.Queued);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APositiveReservationDeficitBlocksAcceptanceAndSettlementButAllowsReplay(bool complete)
    {
        await using var factory = Factory();
        var (owner, command) = await Seed(factory, 3);
        using var acceptance = Scope(factory, owner);
        var receipt = (await Send(acceptance, command)).Value;
        var second = (await Send(acceptance, command with { IdempotencyKey = Guid.NewGuid() })).Value;
        var id = receipt.SubmissionId;
        (await Dispatch(factory, owner, new ClaimWriting(id))).Value.ShouldBeTrue();
        using var setup = Scope(factory, owner);
        await Db(setup).CreditAccounts.Where(a => a.UserId == owner)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedRefundCapacity, 1));

        using var replay = Scope(factory, owner);
        (await Send(replay, command)).Value.ShouldBe(receipt);
        using var newRequest = Scope(factory, owner);
        (await Send(newRequest, command with { IdempotencyKey = Guid.NewGuid() })).Error!.Code.ShouldBe(Error.ConflictCode);
        var result = complete ? await Dispatch(factory, owner, new CompleteWriting(id, Review(id)))
            : await Dispatch(factory, owner, new FailWriting(id));
        result.Error!.Code.ShouldBe(Error.ConflictCode);

        await CheckSettlement(factory, owner, id, WritingEvaluationStatus.Processing, 1, 1);
        await CheckSettlement(factory, owner, second.SubmissionId, WritingEvaluationStatus.Queued, 1, 1);
        using var fresh = Scope(factory, owner);
        var db = Db(fresh);
        (await db.WritingSubmissions.CountAsync(s => s.UserId == owner && s.CompletedAt == null)).ShouldBe(2);
        (await db.WritingSubmissions.CountAsync(s => s.UserId == owner)).ShouldBe(2);
        var ledger = await db.CreditLedger.Where(e => e.UserId == owner).ToListAsync();
        ledger.Count.ShouldBe(3);
        ledger.Sum(e => e.Delta).ShouldBe(1);
    }

    [Theory]
    [InlineData("missing charge")]
    [InlineData("wrong delta")]
    [InlineData("wrong owner")]
    [InlineData("missing reservation")]
    [InlineData("prior refund")]
    [InlineData("feedback owner")]
    [InlineData("feedback evidence")]
    public async Task InconsistentSettlementIsRejectedWithoutMutation(string scenario)
    {
        await using var factory = Factory();
        var (owner, id) = await Accepted(factory, processing: true);
        using var setup = Scope(factory, owner);
        var db = Db(setup);
        var charges = db.CreditLedger.Where(e => e.ReferenceId == id);
        var feedback = Review(id);
        switch (scenario)
        {
            case "missing charge": await charges.ExecuteDeleteAsync(); break;
            case "wrong delta": await charges.ExecuteUpdateAsync(s => s.SetProperty(e => e.Delta, -2)); break;
            case "wrong owner":
                var (other, _) = await Seed(factory);
                await charges.ExecuteUpdateAsync(s => s.SetProperty(e => e.UserId, other));
                break;
            case "missing reservation":
                await db.CreditAccounts.Where(a => a.UserId == owner)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedRefundCapacity, 0));
                break;
            case "prior refund":
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO billing.credit_ledger (id,user_id,delta,reason,reference_id,created_at) VALUES ({Guid.NewGuid()}, {owner}, 1, 'WritingRefund', {id}, {DateTimeOffset.UtcNow})");
                break;
            case "feedback owner": feedback = Review(Guid.NewGuid()); break;
            case "feedback evidence": feedback = Review(id, "café"); break;
        }
        var before = await charges.AsNoTracking().OrderBy(e => e.Id).Select(e => new { e.Id, e.Delta, e.UserId, e.Reason }).ToListAsync();
        (await Dispatch(factory, owner, new CompleteWriting(id, feedback))).IsFailure.ShouldBeTrue();
        if (!scenario.StartsWith("feedback", StringComparison.Ordinal))
        {
            (await Dispatch(factory, owner, new FailWriting(id))).IsFailure.ShouldBeTrue();
        }
        using var read = Scope(factory, owner);
        var saved = Db(read);
        var row = await saved.WritingSubmissions.SingleAsync(s => s.Id == id);
        row.Status.ShouldBe(WritingEvaluationStatus.Processing);
        row.CompletedAt.ShouldBeNull();
        (await saved.WritingFeedbacks.AnyAsync(f => f.Id == id)).ShouldBeFalse();
        var account = await saved.CreditAccounts.SingleAsync(a => a.UserId == owner);
        account.Balance.ShouldBe(0);
        account.ReservedRefundCapacity.ShouldBe(scenario == "missing reservation" ? 0 : 1);
        var after = await saved.CreditLedger.Where(e => e.ReferenceId == id).OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.Delta, e.UserId, e.Reason }).ToListAsync();
        after.SequenceEqual(before).ShouldBeTrue();
    }
}
