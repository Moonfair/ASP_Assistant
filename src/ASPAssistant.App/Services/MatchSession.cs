using System.Net;
using System.Security.Cryptography;
using System.Windows.Threading;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Services;

/// <summary>Mirrors game observations on the sidebar dispatcher. A search result never establishes membership.</summary>
internal sealed class MatchSession : IDisposable
{
    private readonly LineupSiteClient _site;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly SemaphoreSlim _requests = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private MatchPreferences _preferences = new([], [], "随便");
    private string? _ticket, _account, _dismissedRoom;
    private long _generation, _sequence, _lastSample, _snapshotTick;
    private bool _syncFailed;
    private long _nextShoutTick;
    private int _sendingShout;
    private const int ShoutIntervalMs = 2000;

    internal MatchSession(LineupSiteClient site)
    {
        _site = site;
        _timer.Tick += OnTick;
    }

    internal GameRoomObservation? Observation { get; private set; }
    internal string MemoryStatus { get; private set; } = "等待游戏房间数据。";
    internal MatchSnapshot? State { get; private set; }
    internal bool IsRoomDismissed => _dismissedRoom != null;
    internal bool IsRoomReadPending { get; private set; }
    internal bool IsSynchronized => Observation != null && State != null && !_syncFailed && !IsRoomReadPending;
    internal string? Error { get; private set; }
    internal bool Busy { get; private set; }
    internal event Action? Changed;
    internal long ServerTimeNow => State is { } state
        ? state.ServerNow + Environment.TickCount64 - _snapshotTick : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private bool HasRecipient(bool requireShouts) => !Busy && IsSynchronized && Observation?.RoomId is { } room &&
        State is { Phase: "playing" } state && state.GameRoomId == room && state.Members.Any(m => m.HasPlugin &&
            (!requireShouts || m.SupportsShouts) && m.Id != state.SelfId && !state.Muted.Contains(m.Id));
    internal bool CanSendStroke => HasRecipient(requireShouts: false);
    internal int ShoutCooldownMs => (int)Math.Max(0, Volatile.Read(ref _nextShoutTick) - Environment.TickCount64);
    internal bool CanSendShout => !_shutdown.IsCancellationRequested && HasRecipient(requireShouts: true) && State?.RoomId != null &&
        Volatile.Read(ref _sendingShout) == 0 && ShoutCooldownMs == 0;

    internal void SetPreferences(string[] strategies, string[] covenants, string tactic) =>
        _preferences = new([.. strategies], [.. covenants], tactic);

    internal void Observe(BattleUpdate update)
    {
        if (_shutdown.IsCancellationRequested) return;
        if (update.State == BattleFeedState.Restricted)
        {
            SetUnavailable("受限模式不读取游戏房间。可在设置中更改内存读取选择。");
            return;
        }
        if (update.Source == GameTargetKind.MuMu)
        {
            SetUnavailable("自动识别房间需要使用官方 Windows 客户端。");
            return;
        }
        var game = update.Room;
        if (game == null)
        {
            // A roster can be replaced during a battle poll. Pause transmission without discarding
            // the last confirmed scene or its ten-second ink while waiting for the next sample.
            if (update.State is BattleFeedState.Live or BattleFeedState.WaitingForRound or BattleFeedState.ReadInterrupted &&
                Observation is { Phase: "playing", RoomId: { } room } && State?.GameRoomId == room &&
                Environment.TickCount64 - _lastSample < 1500)
            {
                if (!IsRoomReadPending)
                {
                    IsRoomReadPending = true;
                    ++_generation;
                    MemoryStatus = "正在核对对局数据…";
                    Changed?.Invoke();
                }
                return;
            }
            SetUnavailable(update.State switch
            {
                BattleFeedState.WindowsOnly => "自动识别房间需要使用官方 Windows 客户端。",
                BattleFeedState.UnsupportedBuild => "当前游戏版本尚未适配房间读取。",
                BattleFeedState.PermissionRequired => "请在「数据」页点击“授权读取”，以识别游戏房间。",
                BattleFeedState.ConnectionFailed => "读取连接已中断，可在「数据」页重新连接。",
                BattleFeedState.WaitingForClient => "等待官方 Windows 客户端。",
                _ => "正在读取游戏账号和房间…"
            });
            return;
        }

        IsRoomReadPending = false;
        _lastSample = Environment.TickCount64;
        if (_account != game.SelfUid)
        {
            if (_account != null) _ = RetireTicket();
            _account = game.SelfUid;
            _dismissedRoom = null;
        }
        if (_dismissedRoom != game.RoomId) _dismissedRoom = null;
        string status = _dismissedRoom != null ? "已退出 ASPA 房间，离开当前游戏房间后会恢复自动同步。" : "";
        var old = Observation;
        bool contextChanged = old == null || old.SelfUid != game.SelfUid || old.RoomId != game.RoomId ||
            old.Phase != game.Phase || old.HostUid != game.HostUid || old.Members.Length != game.Members.Length ||
            old.Members.Any(seat => !game.Members.Any(current => current.Uid == seat.Uid));
        bool changed = contextChanged || old!.Nickname != game.Nickname || !old.Members.SequenceEqual(game.Members) || MemoryStatus != status;
        Observation = game;
        MemoryStatus = status;
        if (contextChanged)
        {
            ++_generation;
            Error = null;
            if (_dismissedRoom == null)
                State = null; // Don't show an old offer or enable drawing in the previous game room while syncing.
        }
        _timer.Start();
        if (changed) Changed?.Invoke();
    }

    private void SetUnavailable(string status)
    {
        if (Observation == null && MemoryStatus == status) return;
        ++_generation;
        IsRoomReadPending = false;
        Observation = null;
        State = null;
        Error = null;
        MemoryStatus = status;
        _timer.Stop(); // The server expires an unobserved host's listing independently of its guests.
        Changed?.Invoke();
    }

    internal Task SearchAsync() => ActAsync("search");
    internal Task NextAsync() => ActAsync("next");
    internal Task CancelSearchAsync() => ActAsync("cancel-search");
    internal Task RecruitAsync(string invitation) => ActAsync("recruit", invitation: invitation.Trim());
    internal Task StopRecruitAsync() => ActAsync("stop-recruit");
    internal Task ForceStartAsync() => ActAsync("force-start");
    internal Task MuteAsync(string[] muted) => ActAsync("mute", muted: muted);

    private bool MayAct(string action, string? expectedRoom)
    {
        if (_dismissedRoom != null || IsRoomReadPending || Observation is not { } game) return false;
        var state = State;
        return action switch
        {
            "search" => game.Phase == "idle" && !string.IsNullOrWhiteSpace(game.SelfUid) && !string.IsNullOrWhiteSpace(game.Nickname) &&
                _preferences.Strategies.Length > 0 && (state == null || state.Phase == "idle"),
            "next" => game.Phase == "idle" && state?.Phase == "offer" && state.RoomId == expectedRoom,
            "cancel-search" => game.Phase == "idle" && state?.Phase is "offer" or "searching",
            _ when expectedRoom == null || state == null || state.RoomId != expectedRoom || game.RoomId != state.GameRoomId => false,
            "mute" => state.Phase == "playing",
            "force-start" => state.CanForceStart && game.SelfUid == game.HostUid && game.Phase == "lobby",
            "recruit" => state.Phase == "lobby" && game.Phase == "lobby" && game.SelfUid == game.HostUid &&
                _preferences.Strategies.Length > 0 && game.Members.Length < 4,
            "stop-recruit" => state.Recruiting && game.SelfUid == game.HostUid,
            _ => false
        };
    }

    private string Ticket() => _ticket ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private bool Current(string ticket, long generation) =>
        !_shutdown.IsCancellationRequested && _ticket == ticket && _generation == generation;

    private async Task ActAsync(string action, string? invitation = null, string[]? muted = null)
    {
        var expectedRoom = State?.RoomId;
        if (Busy || _shutdown.IsCancellationRequested || !MayAct(action, expectedRoom)) return;
        var ticket = Ticket();
        long generation = _generation;
        Busy = true;
        Error = null;
        Changed?.Invoke();
        try
        {
            await _requests.WaitAsync(_shutdown.Token);
            try
            {
                if (!Current(ticket, generation)) return;
                // Establish the observed identity and send the latest edited preferences before publishing/searching.
                if (action is "search" or "recruit") await SyncAsync(ticket, generation);
                if (Current(ticket, generation) && MayAct(action, expectedRoom))
                    await ExchangeAsync(ticket, generation, action, new { roomId = expectedRoom, invitation, muted, sequence = ++_sequence });
            }
            finally { _requests.Release(); }
        }
        catch (LineupSiteException ex) when (!_shutdown.IsCancellationRequested)
        {
            if (Current(ticket, generation))
            {
                Error = ex.Message;
                _syncFailed = true;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally
        {
            Busy = false;
            if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
        }
    }

    private async void OnTick(object? sender, EventArgs args) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_shutdown.IsCancellationRequested || Observation is not { } game) return;
        if (Environment.TickCount64 - _lastSample > (IsRoomReadPending ? 1500 : 6000))
        {
            SetUnavailable("尚未收到新的游戏房间数据。");
            return;
        }
        // An idle game does not register the player with the service until they start searching.
        if (Busy || IsRoomReadPending || _dismissedRoom != null ||
            (game.Phase == "idle" && (_ticket == null || (State?.Phase == "idle" && !_syncFailed))) || !_requests.Wait(0)) return;
        string ticket = Ticket();
        long generation = _generation;
        try { await SyncAsync(ticket, generation); }
        catch (LineupSiteException ex)
        {
            if (Current(ticket, generation))
            {
                Error = ex.Status == HttpStatusCode.NotFound ? "当前匹配服务尚未支持游戏房间同步。" : ex.Message;
                _syncFailed = true;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally { _requests.Release(); }
        if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
    }

    private Task SyncAsync(string ticket, long generation)
    {
        var preferences = _preferences;
        return ExchangeAsync(ticket, generation, "sync", new
        {
            game = Observation, preferences.Strategies, preferences.Covenants, preferences.Tactic,
            sequence = ++_sequence, knownStrokes = State?.Strokes.Select(s => s.Id).ToArray() ?? [], supportsShouts = true
        });
    }

    private async Task ExchangeAsync(string ticket, long generation, string action, object body)
    {
        long sent = Environment.TickCount64;
        var snapshot = await _site.MatchAsync(action, ticket, body, _shutdown.Token);
        if (!Current(ticket, generation)) return;
        if (snapshot.Phase is not ("idle" or "lobby" or "matching" or "playing" or "searching" or "offer" or "ended") ||
            snapshot.Members == null || snapshot.Muted == null || snapshot.Strokes == null || snapshot.Shouts == null || snapshot.RoomNicknames == null ||
            snapshot.Shouts.Any(s => s == null || string.IsNullOrEmpty(s.Id) || string.IsNullOrEmpty(s.PlayerId) || string.IsNullOrEmpty(s.Text)))
            throw new LineupSiteException("匹配服务返回的房间状态无效。");

        if (snapshot.Strokes.Any(s => s.Points.Length == 0))
        {
            var known = State?.RoomId == snapshot.RoomId ? State?.Strokes.ToDictionary(s => (s.PlayerId, s.Id)) : null;
            snapshot = snapshot with
            {
                Strokes = snapshot.Strokes.Select(s => s.Points.Length > 0 ? s : known?.GetValueOrDefault((s.PlayerId, s.Id)))
                    .OfType<MatchStroke>().ToArray()
            };
        }
        if (_syncFailed || action != "sync" || State?.Phase != snapshot.Phase) Error = null;
        _syncFailed = false;
        _snapshotTick = sent + (Environment.TickCount64 - sent) / 2;
        State = snapshot.Phase == "ended" ? null : snapshot;
        if (State == null) _ticket = null; // The next fresh observation can recreate an expired session.
        _timer.Interval = TimeSpan.FromMilliseconds(snapshot.Phase == "playing" ? 400 : 800);
    }

    internal async Task StrokeAsync(string id, MatchPoint[] points)
    {
        if (!CanSendStroke || _ticket is not { } ticket || State?.RoomId is not { } roomId) return;
        long generation = _generation, created = Environment.TickCount64;
        try
        {
            await _requests.WaitAsync(_shutdown.Token);
            try
            {
                if (Current(ticket, generation) && CanSendStroke && State?.RoomId == roomId && Environment.TickCount64 - created < 10000)
                    await ExchangeAsync(ticket, generation, "stroke", new { roomId, id, points, sequence = ++_sequence });
            }
            finally { _requests.Release(); }
        }
        catch (LineupSiteException ex) { if (Current(ticket, generation)) Error = ex.Message; }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
    }

    internal async Task<bool> SendShoutAsync(QuickPhrase phrase)
    {
        if (!CanSendShout || !QuickChatCatalog.IsValid(phrase, State?.RoomNicknames) || _ticket is not { } ticket || State?.RoomId is not { } room ||
            Interlocked.CompareExchange(ref _sendingShout, 1, 0) != 0) return false;
        long generation = _generation;
        try
        {
            Changed?.Invoke(); // Disable all send/repeat buttons while waiting for an in-flight poll.
            await _requests.WaitAsync(_shutdown.Token);
            try
            {
                if (!Current(ticket, generation) || !HasRecipient(requireShouts: true) || State?.RoomId != room || ShoutCooldownMs != 0 ||
                    !QuickChatCatalog.IsValid(phrase, State.RoomNicknames))
                    return false;
                var known = State.Shouts.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
                await ExchangeAsync(ticket, generation, "shout", new { roomId = room, phrase.Template, phrase.Word, phrase.IsRoomNickname, sequence = ++_sequence });
                if (!Current(ticket, generation) || State is not { Phase: "playing" } state ||
                    state.RoomId != room || state.GameRoomId != Observation?.RoomId) return false;
                // A stale/replayed command may return HTTP 200 with an unchanged snapshot.
                if (!state.Shouts.Any(s => s.PlayerId == state.SelfId && s.Text == phrase.Text && !known.Contains(s.Id)))
                {
                    Error = "服务端未确认这条喊话。";
                    return false;
                }
                Volatile.Write(ref _nextShoutTick, Environment.TickCount64 + ShoutIntervalMs);
                return true;
            }
            finally { _requests.Release(); }
        }
        catch (LineupSiteException ex)
        {
            if (Current(ticket, generation))
            {
                Error = ex.Message;
                if (ex.Status == HttpStatusCode.TooManyRequests)
                    Volatile.Write(ref _nextShoutTick, Environment.TickCount64 + ShoutIntervalMs);
            }
            return false;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return false; }
        finally
        {
            Volatile.Write(ref _sendingShout, 0);
            if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
        }
    }

    internal Task LeaveAsync()
    {
        if (Observation?.RoomId is not { } roomId || _shutdown.IsCancellationRequested) return Task.CompletedTask;
        _dismissedRoom = roomId;
        MemoryStatus = "已退出 ASPA 房间，离开当前游戏房间后会恢复自动同步。";
        var leaving = RetireTicket();
        Changed?.Invoke();
        return leaving;
    }

    private Task RetireTicket()
    {
        var old = _ticket;
        _ticket = null;
        ++_generation;
        State = null;
        Error = null;
        return old == null ? Task.CompletedTask : NotifyLeaveAsync(old);
    }

    private async Task NotifyLeaveAsync(string ticket)
    {
        try
        {
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                try { await _site.MatchAsync("leave", ticket, new { }, _shutdown.Token); return; }
                catch (LineupSiteException) { await Task.Delay(1000 * (attempt + 1), _shutdown.Token); }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _shutdown.Cancel();
        _ticket = null;
    }

    internal void CloseForAppExit()
    {
        var ticket = _ticket;
        Dispose();
        if (ticket == null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { Task.Run(() => _site.MatchAsync("leave", ticket, new { }, timeout.Token)).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is OperationCanceledException or LineupSiteException) { }
    }
}
