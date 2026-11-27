using System.Net;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Writing;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Billing;
using BandUp.Domain.Common;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BandUp.IntegrationTests;

public sealed partial class WritingEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactPostgresConflictRecoversTheOriginalTerminalReceiptOrReturnsPayloadConflict(bool differentEssay)
    {
        var probe = new ConflictProbe(ConflictKind.Existing);
        var logs = new TestLogSink();
        await using var factory = ConflictFactory(probe, logs);
        using var client = ContentTestData.CreateClient(factory);
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-recovery");
        var prompt = await SeedPrompt(factory, user.Id);
        var key = probe.Key.ToString();
        using var accepted = await Post(client, user, Payload(prompt), key);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await ReadJson(accepted);
        var id = receipt.GetProperty("submissionId").GetGuid();

        using (var settle = factory.Services.CreateScope())
        {
            var dispatcher = settle.ServiceProvider.GetRequiredService<IDispatcher>();
            Assert.True((await dispatcher.SendAsync<ClaimWriting, bool>(new(id), CancellationToken.None)).Value);
            var criterion = new CriterionFeedback(7, "Explanation", [], [], ["Add an example."]);
            var feedback = new WritingFeedback(id, "Summary", criterion, criterion, criterion, criterion);
            Assert.True((await dispatcher.SendAsync<CompleteWriting, bool>(new(id, feedback), CancellationToken.None)).Value);
        }

        probe.Armed = true;
        var essay = differentEssay ? Essay.Normalize() : Essay;
        using var response = await Post(client, user, Payload(prompt, essay), key);
        if (differentEssay)
        {
            await AssertProblem(response, HttpStatusCode.Conflict, WritingErrorCodes.IdempotencyConflict);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(accepted.Headers.Location, response.Headers.Location);
            Assert.Equal(receipt.GetRawText(), (await ReadJson(response)).GetRawText());
            Assert.True(response.Headers.CacheControl?.NoStore);
        }

        AssertFreshRecovery(probe, user.Id);
        AssertPostgresConflict<WritingSubmissionKeyConflictException>(logs, "writing", "writing_submissions",
            "ix_writing_submissions_user_id_idempotency_key");
        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var row = await db.WritingSubmissions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal(id, row.Id);
        Assert.Equal(WritingEvaluationStatus.Succeeded, row.Status);
        Assert.Equal(Essay, row.EssayText);
        Assert.True(await db.WritingFeedbacks.AnyAsync(f => f.Id == id));
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == user.Id);
        Assert.Equal(2, account.Balance);
        Assert.Equal(0, account.ReservedRefundCapacity);
        var ledger = await db.CreditLedger.Where(e => e.UserId == user.Id).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(id, ledger.Single(e => e.Reason == CreditLedgerEntry.WritingChargeReason).ReferenceId);
        Assert.Equal(account.Balance, ledger.Sum(e => e.Delta));
    }

    [Fact]
    public async Task ExactPostgresConflictWithoutACommittedWinnerPreservesFailure()
    {
        var probe = new ConflictProbe(ConflictKind.Uncommitted);
        var logs = new TestLogSink();
        await using var factory = ConflictFactory(probe, logs);
        using var client = ContentTestData.CreateClient(factory);
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-recovery-missing");
        var prompt = await SeedPrompt(factory, user.Id);
        probe.Armed = true;

        using var response = await Post(client, user, Payload(prompt), probe.Key.ToString());
        await AssertProblem(response, HttpStatusCode.InternalServerError, Error.InternalErrorCode);
        Assert.Null(response.Headers.Location);
        AssertFreshRecovery(probe, user.Id);
        AssertPostgresConflict<WritingSubmissionKeyConflictException>(logs, "writing", "writing_submissions",
            "ix_writing_submissions_user_id_idempotency_key");
        await AssertUnchangedSignupState(factory, user.Id);
    }

    [Fact]
    public async Task UnrelatedPostgresUniqueViolationUsesTheExistingErrorPathWithoutRecovery()
    {
        var probe = new ConflictProbe(ConflictKind.Unrelated);
        var logs = new TestLogSink();
        await using var factory = ConflictFactory(probe, logs);
        using var client = ContentTestData.CreateClient(factory);
        var user = await ContentTestData.RegisterAndLoginAsync(client, "writing-recovery-unrelated");
        var prompt = await SeedPrompt(factory, user.Id);
        probe.Armed = true;

        using var response = await Post(client, user, Payload(prompt), probe.Key.ToString());
        await AssertProblem(response, HttpStatusCode.Conflict, Error.EmailTakenCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(1, probe.CommandRuns);
        Assert.Equal(2, probe.Lookups.Count);
        Assert.Same(probe.Lookups[0].Context, probe.Lookups[1].Context);
        AssertPostgresConflict<DuplicateEntityException>(logs, "billing", "credit_accounts", "ix_credit_accounts_user_id");
        await AssertUnchangedSignupState(factory, user.Id);
    }

    private BandUpApiFactory ConflictFactory(ConflictProbe probe, TestLogSink logs) => new(environment,
        overrides: new() { ["Features:WritingSubmissionEnabled"] = "true" }, logSink: logs,
        configureServices: services =>
        {
            services.RemoveAll<IWritingSubmissionStore>();
            services.AddScoped<IWritingSubmissionStore>(sp => new ConflictProbeStore(
                sp.GetRequiredService<EfWritingSubmissionStore>(), sp.GetRequiredService<BandUpDbContext>(), probe));
            services.AddScoped<IPipelineBehavior<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>>(sp =>
                new ConflictProbeBehavior(sp.GetRequiredService<BandUpDbContext>(),
                    sp.GetRequiredService<IWritingMutationWriter>(), probe));
        });

    private static void AssertFreshRecovery(ConflictProbe probe, Guid owner)
    {
        Assert.Equal(1, probe.CommandRuns);
        Assert.Equal(3, probe.Lookups.Count);
        Assert.All(probe.Lookups, lookup =>
        {
            Assert.Equal(owner, lookup.Owner);
            Assert.Equal(probe.Key, lookup.Key);
        });
        Assert.Same(probe.Lookups[0].Context, probe.Lookups[1].Context);
        Assert.NotSame(probe.Lookups[0].Context, probe.Lookups[2].Context);
        Assert.True(probe.FailedContextWasDisposedBeforeRecovery);
        Assert.False(probe.RecoveryHadTransaction);
    }

    private static void AssertPostgresConflict<T>(TestLogSink logs, string schema, string table, string constraint)
        where T : Exception
    {
        Assert.Contains(logs.Events, entry => entry.Exception is T
        {
            InnerException: DbUpdateException
            {
                InnerException: PostgresException postgres
            }
        } && postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgres.SchemaName == schema && postgres.TableName == table && postgres.ConstraintName == constraint);
    }

    private static async Task AssertUnchangedSignupState(BandUpApiFactory factory, Guid owner)
    {
        await AssertAccount(factory, owner, charges: 0);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var entry = await db.CreditLedger.SingleAsync(e => e.UserId == owner);
        Assert.Equal(CreditLedgerEntry.SignupBonusReason, entry.Reason);
        Assert.Equal(3, entry.Delta);
    }

    private enum ConflictKind { Existing, Uncommitted, Unrelated }

    private sealed class ConflictProbe(ConflictKind kind)
    {
        public ConflictKind Kind { get; } = kind;
        public Guid Key { get; } = Guid.NewGuid();
        public bool Armed { get; set; }
        public int CommandRuns { get; set; }
        public bool FailedContextWasDisposedBeforeRecovery { get; set; }
        public bool RecoveryHadTransaction { get; set; }
        public List<(Guid Owner, Guid Key, BandUpDbContext Context)> Lookups { get; } = [];
    }

    private sealed class ConflictProbeStore(IWritingSubmissionStore inner, BandUpDbContext db, ConflictProbe probe)
        : IWritingSubmissionStore
    {
        public Task<WritingSubmission?> FindByKeyAsync(Guid owner, Guid key, CancellationToken ct)
        {
            if (!probe.Armed || key != probe.Key)
            {
                return inner.FindByKeyAsync(owner, key, ct);
            }

            probe.Lookups.Add((owner, key, db));
            var original = probe.Lookups[0].Context;
            if (!ReferenceEquals(original, db))
            {
                probe.RecoveryHadTransaction = db.Database.CurrentTransaction is not null;
                try
                {
                    _ = original.Set<WritingSubmission>();
                }
                catch (ObjectDisposedException)
                {
                    probe.FailedContextWasDisposedBeforeRecovery = true;
                }
                return inner.FindByKeyAsync(owner, key, ct);
            }

            // Simulate the winner being invisible during both command lookups. Keep the real row locks and save.
            return probe.Kind == ConflictKind.Existing
                ? Task.FromResult<WritingSubmission?>(null)
                : inner.FindByKeyAsync(owner, key, ct);
        }

        public Task<WritingPromptListDto> ListEligiblePromptsAsync(CancellationToken ct) => inner.ListEligiblePromptsAsync(ct);
        public Task<WritingPrompt?> FindEligiblePromptAsync(Guid promptId, CancellationToken ct) =>
            inner.FindEligiblePromptAsync(promptId, ct);
        public Task<Guid?> FindOwnerAsync(Guid id, CancellationToken ct) => inner.FindOwnerAsync(id, ct);
        public Task<WritingSubmission?> LockSubmissionAsync(Guid id, CancellationToken ct) => inner.LockSubmissionAsync(id, ct);
        public Task<WritingFeedback?> FindFeedbackAsync(Guid id, CancellationToken ct) => inner.FindFeedbackAsync(id, ct);
        public Task<int> CountOutstandingAsync(Guid owner, CancellationToken ct) => inner.CountOutstandingAsync(owner, ct);
    }

    private sealed class ConflictProbeBehavior(BandUpDbContext db, IWritingMutationWriter writer, ConflictProbe probe)
        : IPipelineBehavior<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>
    {
        public async Task<Result<WritingSubmissionAcceptedDto>> HandleAsync(SubmitWritingEssayCommand request,
            CancellationToken ct, RequestHandlerDelegate<WritingSubmissionAcceptedDto> next)
        {
            var result = await next();
            if (!probe.Armed || request.IdempotencyKey != probe.Key || result.IsFailure)
            {
                return result;
            }

            probe.CommandRuns++;
            if (probe.Kind == ConflictKind.Uncommitted)
            {
                // Both inserts are lost on rollback, so even the real recovery lookup will find no row.
                var pending = db.WritingSubmissions.Local.Single();
                var prompt = await db.WritingPrompts.SingleAsync(p => p.Id == pending.PromptId, ct);
                await writer.AddAcceptedSubmissionAsync(new(pending.UserId, pending.IdempotencyKey, prompt,
                    pending.EssayText, pending.SubmittedAt, pending.QueueDeadline), ct);
            }
            else if (probe.Kind == ConflictKind.Unrelated)
            {
                var pending = db.WritingSubmissions.Local.Single();
                db.CreditAccounts.Add(CreditAccount.Open(pending.UserId, 0, pending.SubmittedAt));
            }
            return result;
        }
    }
}
