using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public class MatchSessionTests
{
    private static readonly GameRoomObservation Idle = new("me", "小刻", null, null, "idle", []);

    private static GameRoomObservation Room(string roomId, string phase = "lobby", string host = "me") =>
        new("me", "小刻", roomId, host, phase, [new("me", "小刻", false), new("peer", "博士", true)]);

    private static BattleUpdate Sample(GameRoomObservation game) => new(BattleFeedState.WaitingForRound, Room: game);

    [Fact]
    public void OnlyTheEditablePreferencesAreRestoredAcrossRestarts()
    {
        var path = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new SettingsManager(path.FullName);
            Assert.Empty(settings.LoadMatchPreferences().Strategies);
            Assert.Equal("随便", settings.LoadMatchPreferences().Tactic);
            settings.SaveMatchPreferences(new(["策略甲", "策略乙"], ["炎"], "兜底"));
            var saved = new SettingsManager(path.FullName).LoadMatchPreferences();
            Assert.Equal(["策略甲", "策略乙"], saved.Strategies);
            Assert.Equal(["炎"], saved.Covenants);
            Assert.Equal("兜底", saved.Tactic);
        }
        finally { path.Delete(true); }
    }

    [Fact]
    public async Task OffersRemainSuggestionsUntilTheGameReportsActualMembership()
    {
        string? offered = null;
        var actions = new List<string>();
        using var client = new LineupSiteClient("", new Handler((action, _, body) =>
        {
            actions.Add(action);
            if (action == "sync")
            {
                var game = body.GetProperty("game");
                Assert.Equal("小刻", game.GetProperty("nickname").GetString());
                Assert.Equal("me", game.GetProperty("selfUid").GetString());
                Assert.Equal(new[] { "策略甲" }, body.GetProperty("strategies").Deserialize<string[]>());
                if (game.GetProperty("phase").GetString() == "lobby")
                {
                    Assert.Equal("peer", game.GetProperty("hostUid").GetString());
                    return Reply("lobby", "actual");
                }
                return offered == null ? Reply("idle") : Reply("offer", offered);
            }
            if (action == "next") Assert.Equal("aspa-first", body.GetProperty("roomId").GetString());
            offered = action == "next" ? "second" : "first";
            return Reply("offer", offered);
        }));
        using var session = new MatchSession(client);
        await session.SearchAsync();
        session.Observe(Sample(Idle));
        await session.RefreshAsync();
        await session.SearchAsync();
        Assert.Empty(actions); // No idle publication or search without a profile.
        session.SetPreferences(["策略甲"], ["炎"], "攻坚");
        await session.SearchAsync();
        Assert.Equal("offer", session.State?.Phase);
        Assert.Null(session.Observation?.RoomId);
        Assert.False(session.CanSendStroke);
        await session.NextAsync();
        Assert.Equal("second", session.State?.GameRoomId);
        session.Observe(Sample(Room("actual", host: "peer")));
        Assert.Null(session.State);
        await session.RefreshAsync();
        Assert.Equal("actual", session.State?.GameRoomId);
        Assert.Equal(new[] { "sync", "search", "next", "sync" }, actions);
    }

    [Fact]
    public async Task OnlyAFreshGameHostCanRecruitAndFullOrMatchingObservationsCloseTheControls()
    {
        var published = new List<string>();
        using var client = new LineupSiteClient("", new Handler((action, _, body) =>
        {
            if (action == "recruit") published.Add(body.GetProperty("invitation").GetString()!);
            var phase = action == "sync" ? body.GetProperty("game").GetProperty("phase").GetString()! : "lobby";
            return Reply(phase, "100");
        }));
        using var session = new MatchSession(client);
        session.SetPreferences(["策略甲"], [], "随便");
        session.Observe(Sample(Room("100", host: "peer")));
        await session.RefreshAsync();
        await session.RecruitAsync("guest");
        var lobby = Room("100");
        session.Observe(Sample(lobby));
        await session.RefreshAsync();
        await session.RecruitAsync("  游戏分享口令  ");
        session.Observe(Sample(lobby with { Members = [.. lobby.Members, new("three", "三", false), new("four", "四", false)] }));
        await session.RefreshAsync();
        await session.RecruitAsync("full");
        session.Observe(Sample(lobby with { Phase = "matching" }));
        await session.RefreshAsync();
        await session.RecruitAsync("matching");
        Assert.Equal(new[] { "游戏分享口令" }, published);
    }

    [Fact]
    public async Task ADelayedReplyFromTheLobbyCannotOverwriteTheNewInGameRoomNumber()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<(string Room, string Ticket, long Sequence)>();
        using var client = new LineupSiteClient("", new Handler(async (_, ticket, body) =>
        {
            var game = body.GetProperty("game");
            var id = game.GetProperty("roomId").GetString()!;
            seen.Add((id, ticket, body.GetProperty("sequence").GetInt64()));
            if (id == "old")
            {
                entered.SetResult();
                await resume.Task;
            }
            return Reply(game.GetProperty("phase").GetString()!, id);
        }));
        using var session = new MatchSession(client);
        session.Observe(Sample(Room("old", "matching", "peer")));
        var waiting = session.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.Observe(Sample(Room("new", "playing", "peer")));
        resume.SetResult();
        await waiting;
        Assert.Null(session.State);
        await session.RefreshAsync();
        Assert.Equal("playing", session.State?.Phase);
        Assert.Equal("new", session.State?.GameRoomId);
        Assert.Equal(seen[0].Ticket, seen[1].Ticket);
        Assert.True(seen[1].Sequence > seen[0].Sequence);
    }

    [Fact]
    public async Task ExplicitLeaveSuppressesThatRoomUntilTheNextConfirmedRoomTransition()
    {
        var actions = new List<(string Action, string Ticket)>();
        using var client = new LineupSiteClient("", new Handler((action, ticket, body) =>
        {
            actions.Add((action, ticket));
            return action == "leave" ? Reply("ended") : Reply("lobby", body.GetProperty("game").GetProperty("roomId").GetString());
        }));
        using var session = new MatchSession(client);
        session.Observe(Sample(Room("A")));
        await session.RefreshAsync();
        await session.LeaveAsync();
        session.Observe(new(BattleFeedState.ReadInterrupted));
        await session.RefreshAsync();
        session.Observe(Sample(Room("A")));
        await session.RefreshAsync();
        Assert.True(session.IsRoomDismissed);
        Assert.Null(session.State);
        session.Observe(Sample(Idle));
        session.Observe(Sample(Room("A")));
        await session.RefreshAsync();
        Assert.Equal(new[] { "sync", "leave", "sync" }, actions.Select(c => c.Action));
        Assert.Equal(actions[0].Ticket, actions[1].Ticket);
        Assert.NotEqual(actions[0].Ticket, actions[2].Ticket);
        Assert.Equal("A", session.State?.GameRoomId);
    }

    [Fact]
    public async Task LostSearchResponseIsRecoveredUsingItsOriginalTicket()
    {
        var tickets = new List<string>();
        using var client = new LineupSiteClient("", new Handler((action, ticket, _) =>
        {
            tickets.Add(ticket);
            if (action == "search") throw new HttpRequestException();
            return tickets.Count == 1 ? Reply("idle") : Reply("offer", "found");
        }));
        using var session = new MatchSession(client);
        session.SetPreferences(["甲"], [], "随便");
        session.Observe(Sample(Idle));
        await session.SearchAsync();
        Assert.NotNull(session.Error);
        Assert.False(session.IsSynchronized);
        await session.RefreshAsync();
        Assert.True(session.IsSynchronized);
        Assert.Equal("found", session.State?.GameRoomId);
        Assert.Equal(3, tickets.Count);
        Assert.Matches("^[a-f0-9]{64}$", tickets[0]);
        Assert.All(tickets, ticket => Assert.Equal(tickets[0], ticket));
    }

    [Fact]
    public async Task EnteringTheNativeQueueWithoutALobbyClearsTheCurrentSuggestion()
    {
        string? previousTicket = null;
        using var client = new LineupSiteClient("", new Handler((action, ticket, body) =>
        {
            if (previousTicket != null) Assert.Equal(previousTicket, ticket);
            previousTicket = ticket;
            if (action == "search") return Reply("offer", "suggested");
            var game = body.GetProperty("game");
            return Reply(game.GetProperty("phase").GetString()!, game.GetProperty("roomId").GetString());
        }));
        using var session = new MatchSession(client);
        session.SetPreferences(["甲"], [], "随便");
        session.Observe(Sample(Idle));
        await session.SearchAsync();
        Assert.Equal("offer", session.State?.Phase);
        session.Observe(Sample(Idle with { Phase = "matching" }));
        Assert.Null(session.State);
        await session.RefreshAsync();
        Assert.Equal("matching", session.State?.Phase);
        Assert.Null(session.State!.RoomId);
        session.Observe(Sample(Room("battle", "playing") with { HostUid = null }));
        await session.RefreshAsync();
        Assert.Equal("battle", session.State?.GameRoomId);
        Assert.True(session.CanSendStroke);
    }

    [Fact]
    public async Task SwitchingAccountsInTheSameRoomStartsANewTicket()
    {
        var requests = new List<(string Action, string Ticket)>();
        using var client = new LineupSiteClient("", new Handler((action, ticket, _) =>
        {
            requests.Add((action, ticket));
            return action == "leave" ? Reply("ended") : Reply("lobby", "shared");
        }));
        using var session = new MatchSession(client);
        var first = Room("shared");
        session.Observe(Sample(first));
        await session.RefreshAsync();
        session.Observe(Sample(first with { SelfUid = "peer", Nickname = "博士" }));
        await session.RefreshAsync();
        var syncs = requests.Where(r => r.Action == "sync").ToArray();
        Assert.Equal(2, syncs.Length);
        Assert.NotEqual(syncs[0].Ticket, syncs[1].Ticket);
        Assert.Contains(("leave", syncs[0].Ticket), requests);
    }

    [Fact]
    public async Task InterruptedBattleReadRetainsInkButPausesAllTransmissionUntilTheRoomIsVerifiedAgain()
    {
        int requests = 0;
        var stroke = new MatchStroke(Guid.NewGuid().ToString(), "peer", [new(.1, .2)], long.MaxValue);
        using var client = new LineupSiteClient("", new Handler((_, _, _) =>
        {
            requests++;
            return Reply("playing", "scene", strokes: [stroke]);
        }));
        using var session = new MatchSession(client);
        var playing = Sample(Room("scene", "playing"));
        session.Observe(playing);
        await session.RefreshAsync();
        session.Observe(new(BattleFeedState.ReadInterrupted));
        Assert.True(session.IsRoomReadPending);
        Assert.False(session.CanSendStroke);
        Assert.Equal(stroke.Id, Assert.Single(session.State!.Strokes).Id);
        await session.RefreshAsync();
        await session.StrokeAsync(Guid.NewGuid().ToString(), [new(.4, .5)]);
        Assert.Equal(1, requests);
        session.Observe(playing);
        Assert.True(session.CanSendStroke);
        session.Observe(new(BattleFeedState.ReadInterrupted));
        session.Observe(Sample(Room("other", "playing")));
        Assert.Null(session.State);
        Assert.False(session.CanSendStroke);
    }

    [Fact]
    public async Task KnownStrokesKeepTheirGeometryAndMutingTheOnlyPluginPeerStopsTransmission()
    {
        string[] muted = [];
        var sent = 0;
        var id = Guid.NewGuid().ToString();
        using var client = new LineupSiteClient("", new Handler((action, _, body) =>
        {
            bool known = action == "sync" && body.GetProperty("knownStrokes").EnumerateArray().Any(s => s.GetString() == id);
            if (action == "stroke") sent++;
            if (action == "mute") muted = body.GetProperty("muted").Deserialize<string[]>()!;
            return Reply("playing", "battle", muted, muted.Length == 0
                ? [new(id, "peer", known ? [] : [new(.2, .3), new(.7, .8)], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000)]
                : []);
        }));
        using var session = new MatchSession(client);
        session.Observe(Sample(Room("battle", "playing")));
        await session.RefreshAsync();
        await session.RefreshAsync();
        Assert.Equal(new MatchPoint(.7, .8), Assert.Single(session.State!.Strokes).Points.Last());
        Assert.True(session.CanSendStroke);
        await session.StrokeAsync(Guid.NewGuid().ToString(), [new(.2, .3)]);
        await session.MuteAsync(["peer"]);
        Assert.False(session.CanSendStroke);
        await session.StrokeAsync(Guid.NewGuid().ToString(), [new(.2, .3)]);
        Assert.Equal(1, sent);
        session.Observe(new(BattleFeedState.PermissionRequired));
        Assert.Null(session.Observation);
        Assert.Null(session.State);
        Assert.Contains("授权", session.MemoryStatus);
    }

    [Fact]
    public async Task LegacySnapshotKeepsDrawingButOnlyAnUnmutedCapablePeerCanReceiveShouts()
    {
        var syncs = 0;
        using var client = new LineupSiteClient("", new Handler((action, _, body) =>
        {
            Assert.Equal("sync", action);
            Assert.True(body.GetProperty("supportsShouts").GetBoolean());
            if (++syncs == 1) // Old relay: neither the member capability nor the shouts array exists.
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    phase = "playing", selfId = "me", roomId = "aspa-stage", gameRoomId = "stage",
                    members = new[] { new { id = "me", hasPlugin = true }, new { id = "peer", hasPlugin = true } },
                    muted = Array.Empty<string>(), strokes = Array.Empty<MatchStroke>()
                }) };
            return Reply("playing", "stage", muted: syncs == 3 ? ["modern"] : [], members:
            [
                new { id = "me", hasPlugin = true, supportsShouts = true },
                new { id = "peer", hasPlugin = true },
                new { id = "modern", hasPlugin = true, supportsShouts = true },
                new { id = "noPlugin", hasPlugin = false, supportsShouts = true }
            ]);
        }));
        using var session = new MatchSession(client);
        session.Observe(Sample(Room("stage", "playing")));
        await session.RefreshAsync();
        Assert.True(session.CanSendStroke);
        Assert.False(session.CanSendShout);
        Assert.False(await session.SendShoutAsync(new("当心□！", "浊心斯卡蒂")));
        await session.RefreshAsync();
        Assert.True(session.CanSendShout);
        await session.RefreshAsync();
        Assert.False(session.CanSendShout);
        Assert.True(session.CanSendStroke);
    }

    [Fact]
    public async Task QueuedShoutRechecksCapabilitiesAfterThePendingPoll()
    {
        var polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new LineupSiteClient("", new Handler(async (action, _, body) =>
        {
            Assert.Equal("sync", action);
            if (body.GetProperty("sequence").GetInt64() == 1) return Reply("playing", "stage");
            polling.SetResult();
            await refresh.Task;
            return Reply("playing", "stage", members: [new { id = "peer", hasPlugin = true, supportsShouts = false }]);
        }));
        using var session = new MatchSession(client);
        session.Observe(Sample(Room("stage", "playing")));
        await session.RefreshAsync();
        var inFlight = session.RefreshAsync();
        await polling.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(session.CanSendShout);
        var queued = session.SendShoutAsync(new("当心□！", "浊心斯卡蒂"));
        refresh.SetResult();
        await inFlight;
        Assert.False(await queued);
        Assert.True(session.CanSendStroke);
    }

    [Fact]
    public async Task SendingAndRepeatingShareOneCooldownAndNeverQueueDoubleClicks()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ids = new List<long>();
        List<MatchShout> echoes = [];
        using var client = new LineupSiteClient("", new Handler(async (action, _, body) =>
        {
            ids.Add(body.GetProperty("sequence").GetInt64());
            if (action == "shout")
            {
                Assert.Equal("aspa-stage", body.GetProperty("roomId").GetString());
                Assert.Equal("当心□！", body.GetProperty("template").GetString());
                Assert.Equal("浊心斯卡蒂", body.GetProperty("word").GetString());
                Assert.DoesNotContain(body.EnumerateObject(), property => property.Name == "text");
                started.TrySetResult();
                await resume.Task;
                echoes.Add(new(Guid.NewGuid().ToString(), "me", "当心浊心斯卡蒂！", 1760000000000L));
            }
            return Reply("playing", "stage", shouts: [.. echoes]);
        }));
        using var session = new MatchSession(client);
        var phrase = new QuickPhrase("当心□！", "浊心斯卡蒂");
        Assert.False(await session.SendShoutAsync(phrase));
        session.Observe(Sample(Room("stage", "playing")));
        await session.RefreshAsync();
        Assert.True(session.CanSendShout);
        Assert.False(await session.SendShoutAsync(phrase with { Word = "自由输入" }));
        var first = session.SendShoutAsync(phrase);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(session.CanSendShout);
        Assert.False(await session.SendShoutAsync(phrase));
        resume.SetResult();
        Assert.True(await first);
        Assert.InRange(session.ShoutCooldownMs, 1, 2000);
        Assert.False(await session.SendShoutAsync(phrase));
        var echo = Assert.Single(session.State!.Shouts);
        Assert.Equal("当心浊心斯卡蒂！", echo.Text);
        Assert.Equal(1760000000000L, echo.SentAt);
        Assert.Equal([1L, 2L], ids);
        await Task.Delay(session.ShoutCooldownMs + 25);
        await session.RefreshAsync();
        Assert.True(session.CanSendShout);
        Assert.True(await session.SendShoutAsync(phrase));
        Assert.Equal(2, session.State.Shouts.Length);
    }

    [Fact]
    public async Task FailedStaleAndUnacknowledgedSendsDoNotBecomeRecentPhrases()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var client = new LineupSiteClient("", new Handler(async (action, _, body) =>
        {
            if (action == "shout")
            {
                switch (++count)
                {
                    case 1: return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { error = "无人接收" }) };
                    case 2: return Reply("playing", "stage"); // No acknowledgement of this phrase.
                }
                started.SetResult();
                await resume.Task;
                return Reply("playing", "stage", shouts: [new("id", "me", "当心浊心斯卡蒂！")]);
            }
            return Reply("playing", "stage", action == "mute" ? ["peer"] : []);
        }));
        using var session = new MatchSession(client);
        var game = Room("stage", "playing");
        var phrase = new QuickPhrase("当心□！", "浊心斯卡蒂");
        session.Observe(Sample(game));
        await session.RefreshAsync();
        await session.MuteAsync(["peer"]);
        Assert.False(await session.SendShoutAsync(phrase));
        await session.RefreshAsync();
        session.Observe(new(BattleFeedState.ReadInterrupted));
        Assert.False(await session.SendShoutAsync(phrase));
        session.Observe(Sample(game));
        Assert.False(await session.SendShoutAsync(phrase));
        Assert.Equal("无人接收", session.Error);
        Assert.False(await session.SendShoutAsync(phrase));
        var stale = session.SendShoutAsync(phrase);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Observe(Sample(Room("other", "playing")));
        resume.SetResult();
        Assert.False(await stale);
        Assert.Null(session.State);
        Assert.Equal(3, count);
    }

    private static HttpResponseMessage Reply(string phase, string? gameRoomId = null,
        string[]? muted = null, MatchStroke[]? strokes = null, MatchShout[]? shouts = null, object[]? members = null) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                phase, selfId = "me", serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                roomId = gameRoomId == null ? null : "aspa-" + gameRoomId, gameRoomId,
                members = members ?? [new { id = "me", hasPlugin = true, supportsShouts = true },
                    new { id = "peer", hasPlugin = true, supportsShouts = true }, new { id = "noPlugin", hasPlugin = false }],
                muted = muted ?? [], strokes = strokes ?? [], shouts = shouts ?? []
            })
        };

    private sealed class Handler(Func<string, string, JsonElement, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public Handler(Func<string, string, JsonElement, HttpResponseMessage> respond)
            : this((action, ticket, body) => Task.FromResult(respond(action, ticket, body))) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.StartsWith("/api/desktop/match/live/", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            return await respond(request.RequestUri.Segments.Last(), request.Headers.Authorization!.Parameter!, body);
        }
    }
}
