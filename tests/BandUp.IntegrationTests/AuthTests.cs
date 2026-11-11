using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BandUp.IntegrationTests.Infrastructure;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// AUTH-01 to 08. Covers the public registration, login and profile endpoints.
/// The CAPTCHA provider is the Fake, so a real token is never required.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class AuthTests(BandUpTestEnvironment environment)
{
    private const string RegisterUrl = "/api/auth/register";
    private const string LoginUrl = "/api/auth/login";
    private const string MeUrl = "/api/auth/me";

    private static readonly string ValidPassword = new string('a', 9) + "1";      // 10 chars, letter + digit
    private static readonly string InvalidPassword = new string('a', 9);          // 9 chars, no digit → two violations

    // AUTH-01: register happy path
    [Fact]
    public async Task AUTH_01_Register_Returns_201_And_Creates_User_And_Credit_Account()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth01@example.com",
            password = ValidPassword,
            displayName = "Auth One",
            captchaToken = "fake-captcha-token",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("email").GetString().ShouldBe("auth01@example.com");
        body.GetProperty("displayName").GetString().ShouldBe("Auth One");
        body.GetProperty("role").GetString().ShouldBe("User");
        body.GetProperty("creditBalance").GetInt32().ShouldBe(3);

        // The password must never be plain text anywhere in the response.
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    // AUTH-02: duplicate email
    [Fact]
    public async Task AUTH_02_Register_With_Existing_Email_Returns_409_And_Email_Taken()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth02@example.com",
            password = ValidPassword,
            displayName = "Auth Two",
            captchaToken = "fake-captcha-token",
        });

        HttpResponseMessage second = await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "AUTH02@EXAMPLE.COM", // case-insensitive match via citext
            password = ValidPassword,
            displayName = "Auth Two B",
            captchaToken = "fake-captcha-token",
        });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        JsonElement body = await ReadJsonAsync(second);
        body.GetProperty("code").GetString().ShouldBe("EMAIL_TAKEN");
    }

    // AUTH-02 race: two concurrent registrations with the same email
    [Fact]
    public async Task AUTH_02_Concurrent_Register_With_Same_Email_Yields_One_201_And_One_409()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        var first = client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth02race@example.com",
            password = ValidPassword,
            displayName = "Race A",
            captchaToken = "fake-captcha-token",
        });

        var second = client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth02race@example.com",
            password = ValidPassword,
            displayName = "Race B",
            captchaToken = "fake-captcha-token",
        });

        HttpResponseMessage[] results = await Task.WhenAll(first, second);

        int created = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        int conflict = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        int serverError = results.Count(r => (int)r.StatusCode >= 500);

        created.ShouldBe(1);
        conflict.ShouldBe(1);
        serverError.ShouldBe(0);
    }

    // AUTH-03: weak password / invalid email
    [Fact]
    public async Task AUTH_03_Register_With_Weak_Password_Returns_400_Validation_Error()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth03@example.com",
            password = InvalidPassword, // 9 chars, no digit
            displayName = "Auth Three",
            captchaToken = "fake-captcha-token",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
        body.GetProperty("errors").TryGetProperty("Password", out _).ShouldBeTrue();
    }

    // AUTH-03: invalid email format
    [Fact]
    public async Task AUTH_03_Register_With_Invalid_Email_Returns_400_Validation_Error()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "not-an-email",
            password = ValidPassword,
            displayName = "Auth Three B",
            captchaToken = "fake-captcha-token",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
        body.GetProperty("errors").TryGetProperty("Email", out _).ShouldBeTrue();
    }

    // AUTH-04: missing CAPTCHA token
    [Fact]
    public async Task AUTH_04_Register_With_Missing_Captcha_Token_Returns_422_Captcha_Failed()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth04@example.com",
            password = ValidPassword,
            displayName = "Auth Four",
            captchaToken = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("CAPTCHA_FAILED");
    }

    // AUTH-06: login happy path
    [Fact]
    public async Task AUTH_06_Login_Returns_200_And_Sets_Cookies()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        // Arrange: register a user first so the login has credentials to match.
        await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth06@example.com",
            password = ValidPassword,
            displayName = "Auth Six",
            captchaToken = "fake-captcha-token",
        });

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(LoginUrl, new
        {
            email = "auth06@example.com",
            password = ValidPassword,
            captchaToken = "fake-captcha-token",
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("email").GetString().ShouldBe("auth06@example.com");
        body.GetProperty("role").GetString().ShouldBe("User");

        // Cookies must be present and HttpOnly.
        response.Headers.TryGetValues("Set-Cookie", out var setCookies).ShouldBeTrue();
        var cookies = setCookies!.ToList();

        cookies.ShouldContain(c => c.StartsWith("bu_at=", StringComparison.Ordinal), "bu_at must be set");
        cookies.ShouldContain(c => c.StartsWith("bu_rt=", StringComparison.Ordinal), "bu_rt must be set");
        cookies.ShouldContain(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase), "cookies must be HttpOnly");
        cookies.ShouldContain(c => c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "cookies must be SameSite=Strict");
    }

    // AUTH-07: unknown email — same message, comparable timing
    [Fact]
    public async Task AUTH_07_Login_With_Unknown_Email_Returns_401_Invalid_Credentials()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(LoginUrl, new
        {
            email = "auth07@example.com", // never registered
            password = ValidPassword,
            captchaToken = "fake-captcha-token",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("INVALID_CREDENTIALS");
    }

    // AUTH-07: wrong password — same message, comparable timing
    [Fact]
    public async Task AUTH_07_Login_With_Wrong_Password_Returns_401_Invalid_Credentials()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth07b@example.com",
            password = ValidPassword,
            displayName = "Auth Seven B",
            captchaToken = "fake-captcha-token",
        });

        HttpResponseMessage response = await client.PostAsJsonAsync(LoginUrl, new
        {
            email = "auth07b@example.com",
            password = new string('a', 10) + "2", // different password, same length
            captchaToken = "fake-captcha-token",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().ShouldBe("INVALID_CREDENTIALS");
    }

    // AUTH-08: GET /me without a session cookie
    [Fact]
    public async Task AUTH_08_GetMe_Without_Cookie_Returns_401()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(MeUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // AUTH-08: GET /me with a valid session cookie
    [Fact]
    public async Task AUTH_08_GetMe_With_Valid_Cookie_Returns_200()
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync(RegisterUrl, new
        {
            email = "auth08@example.com",
            password = ValidPassword,
            displayName = "Auth Eight",
            captchaToken = "fake-captcha-token",
        });

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(LoginUrl, new
        {
            email = "auth08@example.com",
            password = ValidPassword,
            captchaToken = "fake-captcha-token",
        });

        string? accessToken = loginResponse.Headers
            .GetValues("Set-Cookie")
            .Select(c => c.Split(';').FirstOrDefault(x => x.Trim().StartsWith("bu_at=")))
            .FirstOrDefault();

        using HttpRequestMessage request = new(HttpMethod.Get, MeUrl);
        request.Headers.Add("Cookie", accessToken!);

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("email").GetString().ShouldBe("auth08@example.com");
        body.GetProperty("role").GetString().ShouldBe("User");
        body.GetProperty("creditBalance").GetInt32().ShouldBe(3);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
