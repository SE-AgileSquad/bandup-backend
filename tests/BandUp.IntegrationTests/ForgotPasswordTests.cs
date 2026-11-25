using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Email;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class ForgotPasswordTests(BandUpTestEnvironment environment)
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray());

    [Fact]
    public async Task AUTH_15_Request_Commits_Protected_Code_Before_Delivery_And_Immediate_Resend_Preserves_It()
    {
        User user = await SeedAsync();
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        sender.BeforeSend = async message =>
        {
            await using BandUpDbContext verification = CreateContext();
            PasswordResetChallenge saved = await verification.PasswordResetChallenges.SingleAsync(challenge => challenge.UserId == user.Id);
            string code = Regex.Match(message.TextBody, "[0-9]{6}").Value;
            code.Length.ShouldBe(6);
            factory.Services.GetRequiredService<IPasswordResetCodeProtector>().Verify(saved.Id, user.Id, code, saved.CodeMac).ShouldBeTrue();
        };
        Result<ForgotPasswordOutcome> result = await DispatchAsync(factory, user.Email);
        result.Value.ShouldBe(new ForgotPasswordOutcome());
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await DispatchAsync(factory, user.Email.ToUpperInvariant())).Value.ShouldBe(result.Value);
        await using BandUpDbContext context = CreateContext();
        (await context.PasswordResetChallenges.CountAsync(challenge => challenge.UserId == user.Id)).ShouldBe(1);
        (await context.Users.FindAsync(user.Id))!.PasswordHash.ShouldBe("existing-test-hash");
        sender.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AUTH_15_Permitted_Request_Replaces_An_Older_Code()
    {
        User user = await SeedAsync(withChallenge: true);
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        (await DispatchAsync(factory, user.Email)).IsSuccess.ShouldBeTrue();
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using BandUpDbContext context = CreateContext();
        var challenges = await context.PasswordResetChallenges.Where(challenge => challenge.UserId == user.Id)
            .OrderBy(challenge => challenge.CreatedAt).ToListAsync();
        challenges.Count.ShouldBe(2);
        challenges[0].InvalidatedAt.ShouldBe(challenges[1].CreatedAt);
        challenges[1].IsUsable(challenges[1].CreatedAt).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AUTH_15_Save_And_Commit_Failures_Restore_Previous_Code_And_Discard_Email(bool failSave)
    {
        User user = await SeedAsync(withChallenge: true);
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender, services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new FailingUnitOfWork(provider.GetRequiredService<BandUpDbContext>(), failSave));
        });
        await Should.ThrowAsync<InvalidOperationException>(() => DispatchAsync(factory, user.Email));
        factory.Services.GetRequiredService<EmailDeliveryQueue>().TryEnqueue(new EmailMessage("barrier@example.com", "Barrier", "done"), null);
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        sender.Messages.Single().Subject.ShouldBe("Barrier");
        await using BandUpDbContext context = CreateContext();
        var challenges = await context.PasswordResetChallenges.Where(challenge => challenge.UserId == user.Id).ToListAsync();
        challenges.Count.ShouldBe(1);
        challenges[0].InvalidatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_15_Ineligible_Accounts_Return_Uniform_Acknowledgements_And_Create_No_Email_Or_Challenge()
    {
        User inactive = await SeedAsync(active: false);
        ObservingSender sender = new();
        await using BandUpApiFactory factory = Factory(sender);
        var unknown = await DispatchAsync(factory, $"unknown-{Guid.CreateVersion7():N}@example.com");
        var disabled = await DispatchAsync(factory, inactive.Email);
        unknown.Value.ShouldBe(disabled.Value);
        factory.Services.GetRequiredService<EmailDeliveryQueue>().TryEnqueue(new EmailMessage("barrier@example.com", "Barrier", "done"), null);
        await sender.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        sender.Messages.Single().Subject.ShouldBe("Barrier");
        await using BandUpDbContext context = CreateContext();
        (await context.PasswordResetChallenges.AnyAsync(challenge => challenge.UserId == inactive.Id)).ShouldBeFalse();
    }

    private BandUpApiFactory Factory(ObservingSender sender, Action<IServiceCollection>? configure = null) => new(environment,
        overrides: new Dictionary<string, string?>
        {
            ["PasswordReset:CodeProtectionKey"] = Key,
            ["PasswordReset:RateLimitProvider"] = "InMemory",
            ["Email:Provider"] = "InMemory",
        }, configureServices: services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            configure?.Invoke(services);
        });

    private static async Task<Result<ForgotPasswordOutcome>> DispatchAsync(BandUpApiFactory factory, string email)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync<ForgotPasswordCommand, ForgotPasswordOutcome>(
            new ForgotPasswordCommand(email, "203.0.113.7"), CancellationToken.None);
    }

    private BandUpDbContext CreateContext() => BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);

    private async Task<User> SeedAsync(bool active = true, bool withChallenge = false)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        User user = User.Create($"forgot-{Guid.CreateVersion7():N}@example.com", "Learner", "existing-test-hash", now.AddDays(-1));
        if (!active)
        {
            user.Deactivate();
        }

        await using BandUpDbContext context = CreateContext();
        context.Users.Add(user);
        if (withChallenge)
        {
            context.PasswordResetChallenges.Add(PasswordResetChallenge.Issue(
            Guid.CreateVersion7(), user.Id, new string('a', 64), now.AddMinutes(-2), now.AddMinutes(8), 5));
        }

        await context.SaveChangesAsync();
        return user;
    }

    private sealed class ObservingSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Messages { get; } = new();
        public TaskCompletionSource<EmailMessage> Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<EmailMessage, Task>? BeforeSend { get; set; }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            try
            {
                if (BeforeSend is not null)
                {
                    await BeforeSend(message);
                }

                Messages.Enqueue(message);
                Delivered.TrySetResult(message);
            }
            catch (Exception exception)
            {
                Delivered.TrySetException(exception);
                throw;
            }
        }
    }

    private sealed class FailingUnitOfWork(BandUpDbContext context, bool failSave) : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner = new(context);
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => _inner.BeginTransactionAsync(cancellationToken);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken) => _inner.RollbackTransactionAsync(cancellationToken);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => failSave
            ? Task.FromException<int>(new InvalidOperationException("save failed")) : _inner.SaveChangesAsync(cancellationToken);
        public Task CommitTransactionAsync(CancellationToken cancellationToken) => !failSave
            ? Task.FromException(new InvalidOperationException("commit failed")) : _inner.CommitTransactionAsync(cancellationToken);
    }
}
