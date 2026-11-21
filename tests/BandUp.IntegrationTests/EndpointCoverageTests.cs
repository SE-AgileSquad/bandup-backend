using System.Net;
using BandUp.IntegrationTests.Infrastructure;
using Shouldly;

namespace BandUp.IntegrationTests;

/// <summary>
/// COV-02. Every endpoint that requires a signed-in user answers 401 without a session.
/// Which endpoints the tests reach at all is measured by <see cref="EndpointHitRecorder"/>,
/// which lists every mapped endpoint on its own, so no hand-kept list is needed.
/// </summary>
[Collection(BandUpCollection.Name)]
public sealed class EndpointCoverageTests(BandUpTestEnvironment environment)
{
    /// <summary>
    /// Every learner endpoint that requires a signed-in user. Without a session each must answer
    /// 401 before it looks at the route, the body or any header.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/reading/passages")]
    [InlineData("GET", "/api/reading/passages/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/lessons")]
    [InlineData("GET", "/api/lessons/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/attempts")]
    [InlineData("GET", "/api/attempts")]
    [InlineData("GET", "/api/attempts/summary")]
    [InlineData("GET", "/api/attempts/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/attempts/00000000-0000-0000-0000-000000000001/answers/00000000-0000-0000-0000-000000000002")]
    [InlineData("POST", "/api/attempts/00000000-0000-0000-0000-000000000001/submit")]
    [InlineData("GET", "/api/attempts/00000000-0000-0000-0000-000000000001/result")]
    public async Task COV_02_Protected_Endpoints_Return_401_Without_A_Session(string method, string url)
    {
        await using BandUpApiFactory factory = new(environment);
        using HttpClient client = ContentTestData.CreateClient(factory);

        using HttpRequestMessage request = new(new HttpMethod(method), url);
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}