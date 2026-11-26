using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing.Admin;
using BandUp.Domain.Billing;
using BandUp.Domain.Identity;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Persistence;
using BandUp.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BandUp.IntegrationTests;

[Collection(BandUpCollection.Name)]
public sealed class AdminCreditTests(BandUpTestEnvironment environment)
{
    [Fact]
    public async Task Super_Admin_Can_Adjust_Arbitrary_Amounts_And_Read_The_Recorded_History()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.SuperAdmin);
        Guid userId = await UserAsync(factory);
        string path = $"/api/admin/users/{userId}/credits";
        AdminCreditBalance before = (await client.GetFromJsonAsync<AdminCreditBalance>(path))!;
        before.Balance.ShouldBe(3);
        Guid requestId = Guid.NewGuid();
        CreditAdjustmentResult grant = await AdjustAsync(client, userId, 17, requestId, " Practice ");
        grant.Balance.ShouldBe(20);
        grant.Delta.ShouldBe(17);
        grant.Note.ShouldBe("Practice");
        grant.IsReplay.ShouldBeFalse();
        CreditAdjustmentResult retry = await AdjustAsync(client, userId, 17, requestId, "Practice");
        retry.AdjustmentId.ShouldBe(grant.AdjustmentId);
        retry.IsReplay.ShouldBeTrue();
        retry.Balance.ShouldBe(20);
        await RejectAsync(client, userId, new(18, requestId, "Practice"), HttpStatusCode.Conflict);
        CreditAdjustmentResult charge = await AdjustAsync(client, userId, -20, Guid.NewGuid(), "Correction");
        charge.Balance.ShouldBe(0);
        await RejectAsync(client, userId, new(-1, Guid.NewGuid()), HttpStatusCode.Conflict);
        CreditAdjustmentResult laterRetry = await AdjustAsync(client, userId, 17, requestId, "Practice");
        laterRetry.AdjustmentId.ShouldBe(grant.AdjustmentId);
        laterRetry.Balance.ShouldBe(0); // A replay returns the current balance without applying its delta again.

        var history = (await client.GetFromJsonAsync<PagedResult<CreditHistoryItem>>(
            $"{path}/history?pageSize=2", JsonSerialization.Options))!;
        history.TotalCount.ShouldBe(3); // Signup plus the two successful adjustments.
        history.Items.Count.ShouldBe(2);
        history.Items.ShouldAllBe(item => item.CreatedByAdminId != null && item.Reason == CreditLedgerEntry.AdminAdjustReason);
        history.Items.ShouldContain(item => item.Id == grant.AdjustmentId && item.Note == "Practice");
        using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        var account = await db.CreditAccounts.SingleAsync(item => item.UserId == userId);
        account.Balance.ShouldBe(await db.CreditLedger.Where(item => item.UserId == userId).SumAsync(item => item.Delta));
        JsonElement page = await client.GetFromJsonAsync<JsonElement>($"/api/admin/users?search={before.Email}");
        page.GetProperty("items")[0].GetProperty("creditBalance").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_Debits_Cannot_Overdraw_And_Concurrent_Retries_Apply_Only_Once()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.SuperAdmin);
        Guid userId = await UserAsync(factory);
        Guid requestId = Guid.NewGuid();
        var results = await Task.WhenAll(AdjustAsync(client, userId, 8, requestId), AdjustAsync(client, userId, 8, requestId));
        results.Select(item => item.AdjustmentId).Distinct().Count().ShouldBe(1);
        results.Count(item => item.IsReplay).ShouldBe(1);
        using HttpResponseMessage first = await client.PostAsJsonAsync(AdjustPath(userId), new AdjustUserCreditsInput(-8, Guid.NewGuid()));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        Task<HttpResponseMessage>[] requests = [
            client.PostAsJsonAsync(AdjustPath(userId), new AdjustUserCreditsInput(-2, Guid.NewGuid())),
            client.PostAsJsonAsync(AdjustPath(userId), new AdjustUserCreditsInput(-2, Guid.NewGuid()))];
        HttpResponseMessage[] debits = await Task.WhenAll(requests);
        try
        {
            debits.Count(item => item.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            debits.Count(item => item.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        }
        finally
        {
            foreach (HttpResponseMessage response in debits)
            {
                response.Dispose();
            }
        }
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        (await db.CreditAccounts.SingleAsync(item => item.UserId == userId)).Balance.ShouldBe(1);
        (await db.CreditLedger.Where(item => item.UserId == userId).SumAsync(item => item.Delta)).ShouldBe(1);
    }

    [Fact]
    public async Task Missing_Accounts_Are_Not_Automatically_Created_And_Overflow_Does_Not_Write_History()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.SuperAdmin);
        Guid missingAccount = await UserAsync(factory, openAccount: false);
        await RejectAsync(client, missingAccount, new(5, Guid.NewGuid()), HttpStatusCode.Conflict);
        await RejectAsync(client, Guid.NewGuid(), new(5, Guid.NewGuid()), HttpStatusCode.NotFound);
        Guid userId = await UserAsync(factory);
        await AdjustAsync(client, userId, int.MaxValue - 3, Guid.NewGuid());
        await RejectAsync(client, userId, new(1, Guid.NewGuid()), HttpStatusCode.Conflict);
        var balance = await client.GetFromJsonAsync<AdminCreditBalance>($"/api/admin/users/{userId}/credits");
        balance!.Balance.ShouldBe(int.MaxValue);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        (await db.CreditLedger.CountAsync(entry => entry.UserId == userId)).ShouldBe(2);
        (await db.CreditAccounts.AnyAsync(account => account.UserId == missingAccount)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_Request_Id_Cannot_Be_Reused_For_A_Different_User_Or_Actor()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.SuperAdmin);
        using HttpClient otherActor = await ClientAsync(factory, Role.SuperAdmin);
        Guid first = await UserAsync(factory);
        Guid second = await UserAsync(factory);
        Guid requestId = Guid.NewGuid();
        await AdjustAsync(client, first, 7, requestId);
        await RejectAsync(client, second, new(7, requestId), HttpStatusCode.Conflict);
        await RejectAsync(otherActor, first, new(7, requestId), HttpStatusCode.Conflict);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        (await db.CreditAccounts.SingleAsync(account => account.UserId == first)).Balance.ShouldBe(10);
        (await db.CreditAccounts.SingleAsync(account => account.UserId == second)).Balance.ShouldBe(3);
    }

    [Theory]
    [InlineData(Role.User)]
    [InlineData(Role.Admin)]
    public async Task Ordinary_Users_And_Teachers_Cannot_Read_Or_Adjust_Credits(Role role)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, role);
        Guid userId = await UserAsync(factory);
        foreach (string suffix in new[] { "", "/history" })
        {
            using HttpResponseMessage response = await client.GetAsync($"/api/admin/users/{userId}/credits{suffix}");
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        await RejectAsync(client, userId, new(5, Guid.NewGuid()), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Validation_And_Csrf_Errors_Do_Not_Write_Credits()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = await ClientAsync(factory, Role.SuperAdmin);
        Guid userId = await UserAsync(factory);
        foreach (AdjustUserCreditsInput input in new AdjustUserCreditsInput[]
            { new(0, Guid.NewGuid()), new(null, Guid.NewGuid()), new(3, null), new(int.MinValue, Guid.NewGuid()), new(3, Guid.NewGuid(), new string('x', 201)) })
        {
            await RejectAsync(client, userId, input, HttpStatusCode.BadRequest);
        }
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        await RejectAsync(client, userId, new(5, Guid.NewGuid()), HttpStatusCode.BadRequest);
        using HttpClient anonymous = factory.CreateClient();
        await RejectAsync(anonymous, userId, new(5, Guid.NewGuid()), HttpStatusCode.Unauthorized);
        await using BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString);
        (await db.CreditLedger.CountAsync(entry => entry.UserId == userId)).ShouldBe(1);
    }

    private static string AdjustPath(Guid userId) => $"/api/admin/users/{userId}/credits/adjustments";
    private static async Task<CreditAdjustmentResult> AdjustAsync(HttpClient client, Guid userId, int delta, Guid requestId, string? note = null)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(AdjustPath(userId), new AdjustUserCreditsInput(delta, requestId, note));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreditAdjustmentResult>())!;
    }
    private static async Task RejectAsync(HttpClient client, Guid userId, AdjustUserCreditsInput input, HttpStatusCode status)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(AdjustPath(userId), input);
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
    }
    private async Task<Guid> UserAsync(BandUpApiFactory factory, bool openAccount = true)
    {
        User user = User.Create($"credit-{Guid.NewGuid():N}@example.test", "Credit user", "test-hash", DateTimeOffset.UtcNow);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BandUpDbContext>();
        db.Users.Add(user);
        if (openAccount)
        {
            await scope.ServiceProvider.GetRequiredService<ICreditService>().OpenSignupAccountAsync(user.Id, CancellationToken.None);
        }
        await db.SaveChangesAsync();
        return user.Id;
    }
    private async Task<HttpClient> ClientAsync(BandUpApiFactory factory, Role role)
    {
        Guid actorId = await UserAsync(factory);
        await using (BandUpDbContext db = BandUpTestEnvironment.CreateContext(environment.MigratedConnectionString))
        {
            User actor = await db.Users.SingleAsync(user => user.Id == actorId);
            actor.ChangeRole(role);
            await db.SaveChangesAsync();
        }
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<ITokenService>()
            .CreateAccessToken(actorId, role.ToString(), DateTimeOffset.UtcNow).Value;
        client.DefaultRequestHeaders.Add("Cookie", $"bu_at={token}");
        client.DefaultRequestHeaders.Add("X-Requested-With", "bandup");
        return client;
    }
}
