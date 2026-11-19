using System.Security.Cryptography;
using System.Windows.Threading;

namespace ASPAssistant.App.Services;

/// <summary>One anonymous matchmaking ticket. All UI and timer entry points run on the side panel's dispatcher.</summary>
internal sealed class MatchSession : IDisposable
{
    private readonly LineupSiteClient _site;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _requests = new(1);
    private readonly DispatcherTimer _timer = new();
    private string? _ticket;
    private (string Action, object Body)? _pendingJoin;
    private long _snapshotTick;
    private bool _pollFailed;

    internal MatchSession(LineupSiteClient site)
    {
        _site = site;
        _timer.Interval = TimeSpan.FromMilliseconds(800);
        _timer.Tick += OnTick;
    }

    internal MatchSnapshot? State { get; private set; }
    internal string? Error { get; private set; }
    internal bool Busy { get; private set; }
    internal event Action? Changed;
    internal long ServerTimeNow => State is { } state
        ? state.ServerNow + Environment.TickCount64 - _snapshotTick
        : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal int ConfirmationSecondsRemaining => State?.Deadline is { } deadline
        ? (int)Math.Clamp(Math.Ceiling((deadline - ServerTimeNow) / 1000d), 0, 20) : 0;
    internal bool CanSendStroke => !Busy && State is { Phase: "playing" } s &&
        s.Members.Any(m => m.Id != s.SelfId && !s.Muted.Contains(m.Id));

    internal Task JoinAsync(string nickname, string strategy, string covenant, string tactic, string? invitation = null)
    {
        if (Busy || State is { Phase: not "ended" } || _shutdown.IsCancellationRequested)
            return Task.CompletedTask;

        _ticket ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        string action = invitation is null ? "join" : "join-room";
        var body = new { nickname, strategy, covenant, tactic, invitation };
        _pendingJoin = (action, body);
        State = null;
        _timer.Start();
        return ActAsync(action, body);
    }

    internal Task ConfirmAsync() => ActAsync("confirm", new { });
    internal Task InviteAsync(string invitation) => ActAsync("invite", new { invitation });
    internal Task ReadyAsync() => ActAsync("ready", new { });
    internal Task LeaveAsync() => ActAsync("leave", new { });
    internal Task MuteAsync(string[] muted) => ActAsync("mute", new { muted });

    private async Task ActAsync(string action, object body)
    {
        if (Busy || _ticket is not { } ticket || _shutdown.IsCancellationRequested) return;
        Busy = true;
        Error = null;
        Changed?.Invoke();
        try
        {
            await _requests.WaitAsync(_shutdown.Token);
            try
            {
                if (_ticket == ticket) await SendAsync(ticket, action, body);
            }
            finally { _requests.Release(); }
        }
        catch (LineupSiteException ex)
        {
            Error = ex.Message;
            if (_pendingJoin?.Action == action)
            {
                if ((int?)ex.Status is >= 400 and < 500) EndTicket();
                else
                {
                    // Retry the same join, since its reply may have been lost or its request delayed.
                    State = new MatchSnapshot { Phase = "recovering", ServerNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                    _snapshotTick = Environment.TickCount64;
                }
            }
            if (action == "leave")
            {
                State = new MatchSnapshot { Phase = "ended", Message = "已退出，正在通知队友。", ServerNow = ServerTimeNow };
                _snapshotTick = Environment.TickCount64;
                Error = null;
                EndTicket();
                _ = RetryLeaveAsync(ticket);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally
        {
            Busy = false;
            if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
        }
    }

    internal async Task StrokeAsync(string id, MatchPoint[] points)
    {
        if (!CanSendStroke || _ticket is not { } ticket || _shutdown.IsCancellationRequested) return;
        var created = Environment.TickCount64;
        try
        {
            await _requests.WaitAsync(_shutdown.Token);
            try
            {
                if (ticket != _ticket || !CanSendStroke || Environment.TickCount64 - created >= 10000) return;
                await SendAsync(ticket, "stroke", new { id, points });
            }
            finally { _requests.Release(); }
        }
        catch (LineupSiteException ex) { Error = ex.Message; }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
    }

    private async void OnTick(object? sender, EventArgs args) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (Busy || _ticket is not { } ticket || _shutdown.IsCancellationRequested || !_requests.Wait(0)) return;
        try
        {
            if (_pendingJoin is { } join) await SendAsync(ticket, join.Action, join.Body);
            else
            {
                var knownStrokes = State?.Strokes.Select(s => s.Id).ToArray() ?? [];
                await SendAsync(ticket, "state", new { knownStrokes });
            }
        }
        catch (LineupSiteException ex)
        {
            Error = ex.Message;
            _pollFailed = true;
            if (_pendingJoin is not null && (int?)ex.Status is >= 400 and < 500)
            {
                State = null;
                EndTicket();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally { _requests.Release(); }
        if (!_shutdown.IsCancellationRequested) Changed?.Invoke();
    }

    private async Task SendAsync(string ticket, string action, object body)
    {
        long sentAt = Environment.TickCount64;
        var snapshot = await _site.MatchAsync(action, ticket, body, _shutdown.Token);
        if (_shutdown.IsCancellationRequested || ticket != _ticket) return;
        if (snapshot.Phase is not ("queued" or "confirming" or "creating" or "joining" or "playing" or "ended") ||
            snapshot.Members is null || snapshot.Muted is null || snapshot.Strokes is null)
            throw new LineupSiteException("匹配服务返回的房间状态无效。");

        // A poll includes IDs of strokes we already hold. The server echoes those without resending their points.
        if (snapshot.Strokes.Any(s => s.Points.Length == 0))
        {
            var known = State?.Strokes.ToDictionary(s => (s.PlayerId, s.Id));
            snapshot = snapshot with
            {
                Strokes = snapshot.Strokes.Select(s => s.Points.Length > 0 ? s :
                    known?.GetValueOrDefault((s.PlayerId, s.Id))).OfType<MatchStroke>().ToArray()
            };
        }
        if (_pollFailed || action != "state" || State?.Phase != snapshot.Phase) Error = null;
        _pollFailed = false;
        _pendingJoin = null;
        State = snapshot;
        // Anchor server deadlines to the midpoint of this round trip, independent of the PC's wall clock.
        _snapshotTick = sentAt + (Environment.TickCount64 - sentAt) / 2;
        if (snapshot.Phase == "ended") EndTicket();
        else _timer.Interval = TimeSpan.FromMilliseconds(snapshot.Phase == "playing" ? 400 : 800);
    }

    private void EndTicket()
    {
        _timer.Stop();
        _ticket = null;
        _pendingJoin = null;
    }

    private async Task RetryLeaveAsync(string ticket)
    {
        // This request belongs to the old ticket even if the player immediately queues again.
        foreach (int delay in new[] { 1000, 5000, 15000 })
        {
            try
            {
                await Task.Delay(delay, _shutdown.Token);
                await _site.MatchAsync("leave", ticket, new { }, _shutdown.Token);
                return;
            }
            catch (LineupSiteException) { }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
        }
        // The server also removes members that stop polling.
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
        if (ticket is null) return;
        // The dispatcher is shutting down. Give a clean exit a short chance to notify the room.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            Task.Run(() => _site.MatchAsync("leave", ticket, new { }, timeout.Token))
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (LineupSiteException) { }
    }
}
