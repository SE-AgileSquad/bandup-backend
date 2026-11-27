using BandUp.Domain.Billing;
using BandUp.Domain.Identity;
using BandUp.Domain.Writing;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class WritingPersistenceTests(BandUpTestEnvironment environment)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly string Essay = string.Join(" ", Enumerable.Repeat("cafe\u0301", 100)) + "\n";

    private BandUpDbContext Context() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private static async Task<(WritingSubmission Submission, WritingPrompt Prompt)> SeedAsync(BandUpDbContext db)
    {
        var user = User.Create($"{Guid.NewGuid():N}@example.test", "Learner", "hash", Now);
        var prompt = WritingPrompt.CreateDraft("Transport", "Discuss public transport.", "Opinion", 100, 350, user.Id, Now);
        prompt.Publish(Now);
        var submission = new WritingSubmission(user.Id, Guid.NewGuid(), prompt, Essay, Now, Now.AddMinutes(5));
        db.AddRange(user, prompt, submission);
        await db.SaveChangesAsync();
        return (submission, prompt);
    }

    [Fact]
    public async Task ArchivedPromptAndCompletedFeedbackRoundTripInFreshContext()
    {
        await using var db = Context();
        var (submission, prompt) = await SeedAsync(db);
        var tr = new CriterionFeedback(6, "Position", ["Clear"], [], ["Expand"], [new WritingEvidence("essay", "Observation")]);
        var other = new CriterionFeedback(6.5m, "Structure", [], [], ["Revise"]);
        var feedback = new WritingFeedback(submission.Id, "Summary", tr, other, other, other);
        submission.StartProcessing(Now.AddMinutes(1));
        submission.Succeed(feedback, Now.AddMinutes(2));
        db.WritingFeedbacks.Add(feedback);
        prompt.Archive(Now.AddDays(1));
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
        duplicate.Add(new WritingFeedback(submission.Id, "Second", other, other, other, other));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task DatabaseEnforcesOwnerKeyAndRestrictiveReferences()
    {
        await using var db = Context();
        var (submission, prompt) = await SeedAsync(db);
        var duplicate = new WritingSubmission(submission.UserId, submission.IdempotencyKey, prompt, Essay, Now, submission.QueueDeadline);
        db.Add(duplicate);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await using var check = Context();
        await ExpectViolation(PostgresErrorCodes.ForeignKeyViolation, () =>
            check.Database.ExecuteSqlAsync($"DELETE FROM writing.writing_prompts WHERE id = {prompt.Id}"));
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
        await using var db = Context();
        var (submission, _) = await SeedAsync(db);
        var account = CreditAccount.Open(submission.UserId, int.MaxValue, Now);
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
