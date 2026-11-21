using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Data;

namespace ASPAssistant.Tests.Services;

public class MatchSessionTests
{
    [Fact]
    public void EditingNicknameReplacesPreviousValueAcrossRestarts()
    {
        var path = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new SettingsManager(path.FullName);
            Assert.Equal("", settings.LoadMatchNickname());
            settings.SaveMatchNickname("博士");
            Assert.Equal("博士", new SettingsManager(path.FullName).LoadMatchNickname());
            settings.SaveMatchNickname("小刻");
            Assert.Equal("小刻", new SettingsManager(path.FullName).LoadMatchNickname());
        }
        finally { path.Delete(true); }
    }

    [Theory]
    [InlineData(null, "join")]
    [InlineData("卫戍协议\n房间 A", "join-room")]
    public async Task LostJoinResponseCanRecoverWithTheSameTicketAndNextMatchGetsANewTicket(string? invitation, string expectedAction)
    {
        var attempts = new List<string>();
        bool lost = true;
        using var client = new LineupSiteClient("", new Handler((action, token, body) =>
        {
            attempts.Add(token);
            if (attempts.Count <= 2)
            {
                Assert.Equal(expectedAction, action);
                Assert.Equal(new[] { "策略一", "策略二" }, body.GetProperty("strategies").Deserialize<string[]>());
                Assert.Equal(new[] { "炎", "萨尔贡" }, body.GetProperty("covenants").Deserialize<string[]>());
                if (invitation is not null) Assert.Equal(invitation, body.GetProperty("invitation").GetString());
            }
            if (lost)
            {
                lost = false;
                throw new HttpRequestException("connection lost");
            }
            return Reply(action switch { "leave" => "ended", "join-room" => "joining", _ => "queued" });
        }));
        using var session = new MatchSession(client);
        await session.JoinAsync("测试", ["策略一", "策略二"], ["炎", "萨尔贡"], "随便", invitation);
        Assert.NotNull(session.Error);
        Assert.Equal("recovering", session.State?.Phase);
        await session.RefreshAsync();
        Assert.Equal(invitation is null ? "queued" : "joining", session.State?.Phase);
        await session.LeaveAsync();
        await session.JoinAsync("测试", ["策略"], [], "随便");
        Assert.Matches("^[a-f0-9]{64}$", attempts[0]);
        Assert.All(attempts.Take(3), ticket => Assert.Equal(attempts[0], ticket));
        Assert.NotEqual(attempts[0], attempts[3]);
    }

    [Fact]
    public async Task ConfirmationUsesServerClockWhenThePcClockIsDifferent()
    {
        using var client = new LineupSiteClient("", new Handler((_, _, _) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { phase = "confirming", serverNow = 1_000_000, deadline = 1_020_000 })
        }));
        using var session = new MatchSession(client);
        await session.JoinAsync("博士", ["众志合一"], [], "随便");
        Assert.InRange(session.ConfirmationSecondsRemaining, 19, 20);
    }

    [Fact]
    public async Task UncertainJoinCanBeCancelledWhileOffline()
    {
        var requests = new List<(string Action, string Ticket)>();
        using var client = new LineupSiteClient("", new Handler((action, ticket, _) =>
        {
            requests.Add((action, ticket));
            if (requests.Count <= 2) throw new HttpRequestException();
            return Reply("queued");
        }));
        using var session = new MatchSession(client);
        await session.JoinAsync("博士", ["众志合一"], [], "随便");
        await session.LeaveAsync();
        Assert.Equal("ended", session.State?.Phase);
        Assert.Equal(("leave", requests[0].Ticket), requests[1]);
        await session.JoinAsync("小刻", ["坚不可摧"], [], "攻坚");
        Assert.Equal("queued", session.State?.Phase);
        Assert.NotEqual(requests[0].Ticket, requests[2].Ticket);
    }

    [Fact]
    public async Task PollKeepsKnownStrokeGeometryAndMutingTheLastPeerStopsSending()
    {
        var sent = 0;
        var strokeId = Guid.NewGuid().ToString();
        string[] muted = [];
        using var client = new LineupSiteClient("", new Handler((action, _, body) =>
        {
            var points = new[] { new MatchPoint(.2, .3), new MatchPoint(.7, .8) };
            if (action == "state")
            {
                Assert.Equal(strokeId, body.GetProperty("knownStrokes")[0].GetString());
                points = [];
            }
            if (action == "mute") muted = body.GetProperty("muted").Deserialize<string[]>()!;
            if (action == "stroke") sent++;
            return Reply("playing", muted, muted.Length > 0 ? [] :
                [new MatchStroke(strokeId, "peer", points, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000)]);
        }));
        using var session = new MatchSession(client);
        await session.JoinAsync("测试", ["策略"], [], "随便");
        Assert.True(session.CanSendStroke);
        await session.RefreshAsync();
        Assert.Equal(new MatchPoint(.7, .8), Assert.Single(session.State!.Strokes).Points.Last());
        await session.MuteAsync(["peer"]);
        Assert.False(session.CanSendStroke);
        Assert.Empty(session.State.Strokes);
        await session.StrokeAsync(Guid.NewGuid().ToString(), [new(.1, .2)]);
        Assert.Equal(0, sent);
    }

    private static HttpResponseMessage Reply(string phase, string[]? muted = null, MatchStroke[]? strokes = null) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                phase, selfId = "me", serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), roomId = "room",
                members = new[] { new { id = "me" }, new { id = "peer" } },
                muted = muted ?? [], strokes = strokes ?? []
            })
        };

    private sealed class Handler(Func<string, string, JsonElement, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.StartsWith("/api/desktop/match/", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            return respond(request.RequestUri.Segments.Last(), request.Headers.Authorization!.Parameter!, body);
        }
    }
}
