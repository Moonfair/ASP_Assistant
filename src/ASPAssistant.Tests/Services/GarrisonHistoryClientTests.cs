using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class GarrisonHistoryClientTests
{
    [Fact]
    public async Task PassportGrantLoadsOnlyBoundArknightsRoleAndReadsItsCurrentSeason()
    {
        var requests = new Queue<string>([
            "/web/v1/user/auth/generate_cred_by_code",
            "/api/v1/game/player/binding",
            "/web/v1/game/garrison/guide/resource",
            "/web/v1/game/garrison/guide/history-log?seasonId=act3autochess"
        ]);
        using var http = new HttpClient(new Handler(async request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            Assert.Equal("zonai.skland.com", request.RequestUri.Host);
            Assert.Equal(requests.Dequeue(), path);
            if (path.EndsWith("generate_cred_by_code"))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.False(request.Headers.Contains("cred"));
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("one-use-grant", body.RootElement.GetProperty("code").GetString());
                return Json("""{"code":0,"data":{"cred":"fixture-cred","token":"fixture-sign-key"}}""");
            }
            if (path.EndsWith("/resource"))
            {
                Assert.False(request.Headers.Contains("cred"));
                return Json("""{"code":0,"data":{"versionInfo":{"seasonId":"act3autochess","versionId":"V3_1"},"chars":[],"items":[],"strategies":[]}}""");
            }
            Assert.Equal("fixture-cred", Assert.Single(request.Headers.GetValues("cred")));
            var expectedSignature = path.EndsWith("/binding")
                ? "f0460e2c7efd41d797aa8562f7db1729"
                : "76b9ba36bf241732dca778b9e7214bea";
            Assert.Equal(expectedSignature, Assert.Single(request.Headers.GetValues("sign")));
            if (path.EndsWith("/binding"))
                return Json("""
                    {"code":0,"data":{"list":[{"appCode":"endfield","bindingList":[{"uid":"9"}]},
                        {"appCode":"arknights","bindingList":[{"gameId":1,"uid":"456","nickName":"另一个角色"}, {"gameId":1,"uid":"123","nickName":"博士","isDefault":true}]}]}}
                    """);
            Assert.Equal("1_123_0", Assert.Single(request.Headers.GetValues("sk-game-role")));
            return Json("""{"code":0,"data":{"records":[{"id":"record1","activityId":"act3autochess"}]}}""");
        }));
        var client = new GarrisonHistoryClient(http, new FixedClock());
        var session = await client.AuthorizeAsync("one-use-grant", default);
        Assert.Equal(2, session.Roles.Count);
        var role = session.Roles[0];
        Assert.Equal("博士", role.Name);
        var result = await client.FetchAsync(session, role, default);
        Assert.Equal("record1", Assert.Single(result.Records).Id);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task ExpiredGrantReturnsAnAuthorizationError()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(
            Json("""{"code":10002,"message":"private upstream data"}""", HttpStatusCode.Unauthorized))));
        var client = new GarrisonHistoryClient(http, new FixedClock());
        var failure = await Assert.ThrowsAsync<GarrisonAuthorizationException>(
            () => client.AuthorizeAsync("expired-grant", default));
        Assert.DoesNotContain("private upstream data", failure.Message);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1794459000);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
