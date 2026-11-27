using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Features.Identity;
using Shouldly;

namespace BandUp.IntegrationTests.Infrastructure;

internal static class RecoveryTestFlow
{
    public static async Task<string> VerifyAsync(HttpClient client, string email, string code)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/verify-reset-code", new { email, code });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Count().ShouldBe(2);
        string resetToken = body.RootElement.GetProperty("resetToken").GetString()!;
        PasswordResetPolicy.IsResetToken(resetToken).ShouldBeTrue();
        body.RootElement.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(TimeProvider.System.GetUtcNow());
        return resetToken;
    }
}
