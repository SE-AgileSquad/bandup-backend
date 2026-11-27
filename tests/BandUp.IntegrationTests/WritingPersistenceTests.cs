using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Domain.Billing;
using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingPersistenceTests(BandUpTestEnvironment environment) : WritingAcceptanceFixture(environment)
{
    private BandUpDbContext Context() => BandUpTestEnvironment.CreateContext(MigratedConnectionString);

    private static async Task<WritingSubmission> SeedAsync(BandUpApiFactory factory, BandUpDbContext db)
    {
        var (owner, command) = await Seed(factory);
        using var scope = Scope(factory, owner);
        var id = (await Send(scope, command)).Value.SubmissionId;
        return await db.WritingSubmissions.SingleAsync(s => s.Id == id);
    }

    [Fact]
    public async Task ArchivedPromptAndCompletedFeedbackRoundTripInFreshContext()
    {
        var clock = new TestClock();
        await using var factory = Factory(clock);
        var (owner, command) = await Seed(factory);
        using var operation = Scope(factory, owner);
        var id = (await Send(operation, command)).Value.SubmissionId;
        var dispatcher = operation.ServiceProvider.GetRequiredService<IDispatcher>();
        Assert.True((await dispatcher.SendAsync<ClaimWriting, bool>(new(id), Ct)).Value);
        var tr = new CriterionFeedback(6, "Position", ["Clear"], [], ["Expand"], [new WritingEvidence("cafe\u0301", "Observation")]);
        var other = new CriterionFeedback(6.5m, "Structure", [], [], ["Revise"]);
        var feedback = new WritingFeedback(id, "Summary", tr, other, other, other);
        Assert.True((await dispatcher.SendAsync<CompleteWriting, bool>(new(id, feedback), Ct)).Value);
        await using var db = Context();
        var submission = await db.WritingSubmissions.SingleAsync(s => s.Id == id);
        var prompt = await db.WritingPrompts.SingleAsync(p => p.Id == command.PromptId);
        prompt.Archive(clock.GetUtcNow());
        await db.SaveChangesAsync();

        await using var reader = Context();
        var saved = await reader.WritingSubmissions.SingleAsync(s => s.Id == submission.Id);
        Assert.Equal(submission.PromptSnapshot, saved.PromptSnapshot);
        Assert.Equal(submission.IdempotencyKey, saved.IdempotencyKey);
        Assert.True(saved.MatchesPayload(prompt.Id, Essay));
        Assert.Equal(WritingEvaluationStatus.Succeeded, saved.Status);
        Assert.Equal(submission.StartedAt, saved.StartedAt);
        Assert.Equal(submission.CompletedAt, saved.CompletedAt);
        Assert.Equal(submission.QueueDeadline, saved.QueueDeadline);
        var result = await reader.WritingFeedbacks.SingleAsync(f => f.Id == submission.Id);
        Assert.Equal("1.0", result.SchemaVersion);
        Assert.Equal(6.5m, result.EstimatedOverallBand);
        Assert.Equal("Observation", Assert.Single(result.Tr.Evidence!).Observation);
        Assert.Equal("Revise", Assert.Single(result.Gra.Recommendations));
        Assert.Null(result.Cc.Evidence);
        var shape = await reader.Database.SqlQuery<bool>($"""
            SELECT jsonb_typeof(payload) = 'object' AND payload->>'scoreLabel' = 'estimated'
                AND payload->'criteria'->'tr'->>'score' = '6'
                AND NOT (payload->'criteria'->'cc' ? 'evidence') AS "Value"
            FROM writing.writing_feedbacks WHERE submission_id = {submission.Id}
            """).SingleAsync();
        Assert.True(shape);
        await using var duplicate = Context();
        await ExpectViolation(PostgresErrorCodes.UniqueViolation, () => duplicate.Database.ExecuteSqlAsync($"""
            INSERT INTO writing.writing_feedbacks (submission_id, schema_version, estimated_overall_band, payload)
            SELECT submission_id, schema_version, estimated_overall_band, payload
            FROM writing.writing_feedbacks WHERE submission_id = {submission.Id}
            """));
    }

    [Fact]
    public async Task DatabaseEnforcesOwnerKeyAndRestrictiveReferences()
    {
        await using var factory = Factory();
        await using var db = Context();
        var submission = await SeedAsync(factory, db);
        await ExpectViolation(PostgresErrorCodes.UniqueViolation, () => db.Database.ExecuteSqlAsync($"""
            INSERT INTO writing.writing_submissions
                (id, user_id, idempotency_key, prompt_id, prompt_title, prompt_text, essay_text,
                 submitted_at, queue_deadline, publish_attempts, status)
            SELECT {Guid.NewGuid()}, user_id, idempotency_key, prompt_id, prompt_title, prompt_text, essay_text,
                   submitted_at, queue_deadline, publish_attempts, status
            FROM writing.writing_submissions WHERE id = {submission.Id}
            """));
        await using var check = Context();
        await ExpectViolation(PostgresErrorCodes.ForeignKeyViolation, () =>
            check.Database.ExecuteSqlAsync($"DELETE FROM writing.writing_prompts WHERE id = {submission.PromptId}"));
        await ExpectViolation(PostgresErrorCodes.ForeignKeyViolation, () =>
            check.Database.ExecuteSqlAsync($"UPDATE writing.writing_submissions SET user_id = {Guid.NewGuid()} WHERE id = {submission.Id}"));
        var entity = check.Model.FindEntityType(typeof(WritingSubmission))!;
        Assert.Equal(DeleteBehavior.Restrict, entity.GetForeignKeys().Single(f =>
            f.PrincipalEntityType.ClrType == typeof(User)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, entity.GetForeignKeys().Single(f =>
            f.PrincipalEntityType.ClrType == typeof(WritingPrompt)).DeleteBehavior);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "UserId", "SubmittedAt", "Id" }));
    }

    [Fact]
    public async Task RefundCapacityDefaultsToZeroAndBoundsTheCombinedBalance()
    {
        await using var factory = Factory();
        var (owner, _) = await Seed(factory, balance: null);
        await using var db = Context();
        var account = CreditAccount.Open(owner, int.MaxValue, DateTimeOffset.UnixEpoch);
        db.Add(account);
        await db.SaveChangesAsync();
        await using var check = Context();
        Assert.Equal(0, (await check.CreditAccounts.SingleAsync(a => a.Id == account.Id)).ReservedRefundCapacity);
        await ExpectViolation(PostgresErrorCodes.CheckViolation, () => check.Database.ExecuteSqlAsync(
            $"UPDATE billing.credit_accounts SET reserved_refund_capacity = 1 WHERE id = {account.Id}"));
        await ExpectViolation(PostgresErrorCodes.CheckViolation, () => check.Database.ExecuteSqlAsync(
            $"UPDATE billing.credit_accounts SET reserved_refund_capacity = -1 WHERE id = {account.Id}"));
        Assert.Equal(1, await check.Database.ExecuteSqlAsync(
            $"UPDATE billing.credit_accounts SET balance = {int.MaxValue - 1}, reserved_refund_capacity = 1 WHERE id = {account.Id}"));
    }

    private static async Task ExpectViolation(string code, Func<Task<int>> write)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(async () => { await write(); });
        Assert.Equal(code, error.SqlState);
    }
}
