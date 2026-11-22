using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Services;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Views;

public partial class MatchView : UserControl
{
    private const string Unknown = "未知";
    private MatchSession? _session;
    private SettingsManager? _settings;
    private OverlayWindow? _overlay;
    private (string? Account, string? Room) _draftOwner;
    private (string? Phase, string? Room) _context;
    private string? _notice;
    private bool _changingPreferences, _clipboardBusy;
    private MemberRow[] _rows = [];

    public MatchView() => InitializeComponent();

    internal void Configure(MatchSession session, SettingsManager settings, IReadOnlyList<Strategy> strategies,
        CovenantCatalog covenants, OverlayWindow overlay)
    {
        _session = session;
        _settings = settings;
        _overlay = overlay;
        _changingPreferences = true;
        try
        {
            var saved = settings.LoadMatchPreferences();
            var availableStrategies = strategies.DistinctBy(item => item.Name).ToArray();
            var availableCovenants = covenants.CoreCovenants.Concat(covenants.AdditionalCovenants).Distinct().ToArray();
            StrategyPicker.ItemsSource = availableStrategies;
            CovenantPicker.ItemsSource = availableCovenants;
            TacticPicker.ItemsSource = new[] { "随便", "兜底", "攻坚" };
            foreach (var item in saved.Strategies.Distinct()
                         .Select(name => availableStrategies.FirstOrDefault(item => item.Name == name)).OfType<Strategy>().Take(4))
                StrategyPicker.SelectedItems.Add(item);
            foreach (var name in saved.Covenants.Distinct().Where(availableCovenants.Contains).Take(4))
                CovenantPicker.SelectedItems.Add(name);
            TacticPicker.SelectedItem = saved.Tactic;
        }
        finally { _changingPreferences = false; }
        ApplyPreferences(save: false);
    }

    internal void Finish()
    {
        _session = null;
        _settings = null;
        _overlay = null;
    }

    internal void Refresh()
    {
        if (_session is not { } session) return;
        var game = session.Observation;
        var state = session.State;
        var room = game?.RoomId != null && !session.IsRoomDismissed ? game : null;
        var mirror = room != null && state is { Phase: "lobby" or "matching" or "playing" } && state.GameRoomId == room.RoomId
            ? state : null;
        var phase = mirror?.Phase ?? game?.Phase ?? "idle";
        if (session.IsRoomDismissed) phase = "ended";
        else if (game?.Phase == "idle" && state?.Phase is "searching" or "offer" or "ended") phase = state.Phase;
        (string Phase, string? Room) context = (phase, room?.RoomId ?? state?.RoomId);
        if (_context != context)
        {
            _notice = null;
            if (_context.Room != context.Room || room == null) CloseLeavePrompt();
            _context = context;
        }

        if (game != null && _draftOwner != (game.SelfUid, game.RoomId))
        {
            _draftOwner = (game.SelfUid, game.RoomId);
            InvitationDraft.Text = mirror?.Invitation ?? "";
            CloseLeavePrompt();
        }
        var host = room?.HostUid is { Length: > 0 } hostUid && hostUid == room.SelfUid;
        var openLobby = phase == "lobby" && room is { Phase: "lobby", Members.Length: < 4 };
        var recruiting = openLobby && mirror?.Recruiting == true;
        if (recruiting && host && session.IsSynchronized && InvitationDraft.Text.Length == 0 && mirror?.Invitation is { Length: > 0 } published)
            InvitationDraft.Text = published;
        var playing = phase == "playing";
        var offer = phase == "offer";
        var currentOffer = offer && session.IsSynchronized ? state : null;
        var available = !session.Busy && game != null;
        var hasAccount = !string.IsNullOrWhiteSpace(game?.SelfUid) && !string.IsNullOrWhiteSpace(game.Nickname);
        var roster = currentOffer ?? (session.IsSynchronized || session.IsRoomReadPending ? mirror : null);
        var peers = playing && roster != null ? roster.Members.Where(m => m.HasPlugin && m.Id != roster.SelfId).ToArray() : [];
        var allMuted = peers.Length > 0 && peers.All(member => roster!.Muted.Contains(member.Id));

        Heading.Text = phase switch
        {
            "searching" => "正在匹配",
            "offer" when currentOffer == null => "正在核实推荐房间",
            "offer" => "找到招募中的房间",
            "lobby" when recruiting => "正在搜寻队友",
            "lobby" => "游戏房间",
            "matching" => "游戏内匹配中",
            "playing" => "游戏进行中",
            "ended" => "本次匹配已结束",
            _ => "组队匹配"
        };
        var number = room?.RoomId ?? currentOffer?.GameRoomId;
        var identity = string.IsNullOrWhiteSpace(game?.Nickname) ? null : $"游戏昵称  {game.Nickname}";
        var roomNumber = string.IsNullOrWhiteSpace(number) ? null : $"房间号  {number}";
        SetText(GameIdentity, string.Join("  ·  ", new[] { identity, roomNumber }.OfType<string>()));
        string? instruction = phase switch
        {
            _ when !string.IsNullOrWhiteSpace(session.MemoryStatus) => session.MemoryStatus,
            "idle" when !hasAccount => "正在读取游戏账号…",
            "searching" => "正在寻找有空位的房间。",
            "offer" when currentOffer == null => null,
            "offer" => "复制邀请文本，在游戏中加入这个房间。",
            _ when room != null && mirror == null => "正在同步游戏房间…",
            "lobby" when recruiting => "其他玩家可以在匹配中看到这个房间。",
            "lobby" when openLobby && host => "粘贴游戏房间的邀请文本，即可搜寻队友。",
            "playing" when allMuted => "已屏蔽所有队友。取消屏蔽一人即可发送笔迹。",
            "playing" when room?.Members.Length == 1 => "当前房间只有你一人。",
            "playing" when peers.Length == 0 && roster != null => "队友开启插件后即可互相标记。",
            "playing" => "点击“开始标记”或按住 Ctrl 在游戏画面上作画。笔迹保留 10 秒。",
            _ => null
        };
        SetText(Description, instruction);
        Show(SearchButton, (phase is "idle" or "ended") && room == null && !session.IsRoomDismissed);
        SearchButton.IsEnabled = available && hasAccount && game?.Phase == "idle" && StrategyPicker.SelectedItems.Count > 0;
        Show(NextButton, offer);
        NextButton.IsEnabled = available && session.IsSynchronized;
        Show(CancelSearchButton, phase is "searching" or "offer");
        CancelSearchButton.IsEnabled = available;

        Show(InviteEditor, openLobby && host && !recruiting);
        Show(RecruitButton, InviteEditor.Visibility == Visibility.Visible);
        Show(StopRecruitButton, recruiting && host);
        StopRecruitButton.IsEnabled = available;
        PasteButton.IsEnabled = available && !_clipboardBusy;
        UpdateRecruitButton();
        var invitation = currentOffer?.Invitation ?? (recruiting && session.IsSynchronized ? mirror?.Invitation : null);
        Show(SharedInvitation, !string.IsNullOrWhiteSpace(invitation));
        if (PublishedInvitation.Text != (invitation ?? "")) PublishedInvitation.Text = invitation ?? "";
        CopyButton.IsEnabled = available && !_clipboardBusy && session.IsSynchronized && !string.IsNullOrWhiteSpace(invitation);

        Show(ForceStartButton, host && room?.Phase == "lobby" && phase == "lobby" && mirror?.CanForceStart == true);
        ForceStartButton.IsEnabled = available && session.IsSynchronized;
        Show(MarkButton, playing);
        MarkButton.Content = _overlay?.IsMatchMarking == true ? "结束标记" : "开始标记";
        MarkButton.IsEnabled = session.CanSendStroke || _overlay?.IsMatchMarking == true;
        Show(LeaveButton, room != null);
        LeaveButton.IsEnabled = available;
        ApproveLeaveButton.IsEnabled = available;

        UpdateRoster(roster, room, playing);
        Show(RosterHeading, room != null || currentOffer != null);
        RosterTitle.Text = $"房间成员（{_rows.Length}/4）";
        Show(MuteAllButton, playing);
        MuteAllButton.Content = allMuted ? "取消全部屏蔽" : "屏蔽所有人";
        MuteAllButton.IsEnabled = available && session.IsSynchronized && peers.Length > 0;
        Roster.IsEnabled = !session.Busy;
        SetText(Feedback, session.Error ?? (session.Busy ? "正在提交…" : _notice ?? state?.Message));
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void SetText(TextBlock block, string? value)
    {
        block.Text = value ?? "";
        Show(block, !string.IsNullOrWhiteSpace(value));
    }

    private sealed record MemberRow(string Id, string DisplayName, string Color, string Strategy, string Covenant,
        string Tactic, string MuteLabel, string MuteDescription, Visibility MuteVisibility);

    private static string MemberName(string nickname, bool self, bool host) =>
        (string.IsNullOrWhiteSpace(nickname) ? "昵称未知" : nickname) + (self ? "（我）" : "") + (host ? " · 房主" : "");

    private void UpdateRoster(MatchSnapshot? snapshot, GameRoomObservation? room, bool playing)
    {
        MemberRow[] rows;
        if (snapshot != null)
        {
            rows = snapshot.Members.Select(member =>
            {
                var self = member.Id == snapshot.SelfId;
                var name = MemberName(member.Nickname, self, member.Id == snapshot.CaptainId);
                var mute = snapshot.Muted.Contains(member.Id) ? "取消屏蔽" : "屏蔽";
                string Preference(string value) => member.HasPlugin && !string.IsNullOrWhiteSpace(value) ? value : Unknown;
                var tactic = Preference(member.Tactic);
                if (playing && member.HasPlugin && member.AssignedTactic is { Length: > 0 } assigned && assigned != Unknown)
                    tactic += $"（分配为{assigned}）";
                return new MemberRow(member.Id, name, string.IsNullOrWhiteSpace(member.Color) ? "#FFE0E0E0" : member.Color,
                    Preference(member.Strategy), Preference(member.Covenant), tactic, mute, $"{mute}{member.Nickname}的笔迹",
                    playing && !self && member.HasPlugin ? Visibility.Visible : Visibility.Collapsed);
            }).ToArray();
        }
        else
        {
            rows = room?.Members.Select(seat => new MemberRow(seat.Uid,
                MemberName(seat.Nickname, seat.Uid == room.SelfUid, seat.Uid == room.HostUid),
                "#FFE0E0E0", Unknown, Unknown, Unknown, "", "", Visibility.Collapsed)).ToArray() ?? [];
        }
        if (_rows.SequenceEqual(rows)) return;
        _rows = rows;
        Roster.ItemsSource = rows;
    }

    private void ApplyPreferences(bool save)
    {
        var strategies = StrategyPicker.SelectedItems.Cast<Strategy>().Select(item => item.Name).ToArray();
        var covenants = CovenantPicker.SelectedItems.Cast<string>().ToArray();
        var tactic = TacticPicker.SelectedItem as string ?? "随便";
        StrategySummary.Text = strategies.Length == 0 ? "请选择策略" : string.Join("、", strategies);
        CovenantSummary.Text = covenants.Length == 0 ? "随便" : string.Join("、", covenants);
        AnyCovenant.IsEnabled = covenants.Length > 0;
        _session?.SetPreferences(strategies, covenants, tactic);
        if (save && _settings != null)
        {
            try { _settings.SaveMatchPreferences(new MatchPreferences(strategies, covenants, tactic)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Error("Match", "Could not save match preferences", ex);
                _notice = "匹配偏好未能保存到本地。";
            }
        }
        Refresh();
    }

    private void OnPreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingPreferences) return;
        _changingPreferences = true;
        try
        {
            var picker = (ListBox)sender;
            // Keyboard select-all and UI automation can add more than four items in one event.
            foreach (var item in e.AddedItems.Cast<object>().Reverse())
            {
                if (picker.SelectedItems.Count <= 4) break;
                picker.SelectedItems.Remove(item);
            }
        }
        finally { _changingPreferences = false; }
        ApplyPreferences(save: true);
    }

    private void OnTacticChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_changingPreferences) ApplyPreferences(save: true);
    }

    private void OnAnyCovenant(object sender, RoutedEventArgs e) => CovenantPicker.UnselectAll();

    private void OnPreferenceWheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = (ScrollViewer)sender;
        if (e.Delta == 0 || (e.Delta > 0 ? scroll.VerticalOffset > 0 : scroll.VerticalOffset < scroll.ScrollableHeight))
            return;
        e.Handled = true;
        PageScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = Mouse.MouseWheelEvent });
    }

    private void UpdateRecruitButton() =>
        RecruitButton.IsEnabled = RecruitButton.Visibility == Visibility.Visible && _session is { Busy: false, IsSynchronized: true,
            State.Phase: "lobby" } && !_clipboardBusy && StrategyPicker.SelectedItems.Count > 0 && !string.IsNullOrWhiteSpace(InvitationDraft.Text);

    private void OnDraftChanged(object sender, TextChangedEventArgs e) => UpdateRecruitButton();

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (_session != null && SearchButton.IsEnabled) await _session.SearchAsync();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_session != null && NextButton.IsEnabled) await _session.NextAsync();
    }

    private async void OnCancelSearch(object sender, RoutedEventArgs e)
    {
        if (_session != null && CancelSearchButton.IsEnabled) await _session.CancelSearchAsync();
    }

    private async void OnRecruit(object sender, RoutedEventArgs e)
    {
        if (_session != null && RecruitButton.IsEnabled) await _session.RecruitAsync(InvitationDraft.Text);
    }

    private async void OnStopRecruit(object sender, RoutedEventArgs e)
    {
        if (_session != null && StopRecruitButton.IsEnabled) await _session.StopRecruitAsync();
    }

    private async void OnForceStart(object sender, RoutedEventArgs e)
    {
        if (_session != null && ForceStartButton.IsEnabled) await _session.ForceStartAsync();
    }

    private void OnMark(object sender, RoutedEventArgs e)
    {
        if (_overlay == null || !MarkButton.IsEnabled) return;
        var wasMarking = _overlay.IsMatchMarking;
        _notice = null;
        _overlay.ToggleMatchMarking();
        if (!wasMarking && !_overlay.IsMatchMarking)
            _notice = _overlay.IsCalibratingMarkers ? "请先在设置中结束标记位置校准，再开启作画。"
                : "暂时无法开启标记。请检查游戏来源，并让游戏窗口保持可见。";
        Refresh();
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        if (_clipboardBusy || Window.GetWindow(this) is not { } window || InviteEditor.Visibility != Visibility.Visible) return;
        var owner = _draftOwner;
        var originalDraft = InvitationDraft.Text;
        _clipboardBusy = true;
        Refresh();
        try
        {
            var text = await LineupClipboard.ReadAsync(window, InvitationDraft.MaxLength);
            if (_session == null || _draftOwner != owner || InviteEditor.Visibility != Visibility.Visible || InvitationDraft.Text != originalDraft) return;
            if (text.Length > InvitationDraft.MaxLength) _notice = "邀请文本超过 1024 个字符。";
            else if (string.IsNullOrWhiteSpace(text)) _notice = "剪贴板中没有邀请文本。";
            else
            {
                InvitationDraft.Text = text;
                _notice = "已粘贴邀请文本。";
            }
        }
        catch (ExternalException) { _notice = "读取剪贴板失败，请重试。"; }
        finally { _clipboardBusy = false; Refresh(); }
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!CopyButton.IsEnabled || Window.GetWindow(this) is not { } window) return;
        var invitation = PublishedInvitation.Text;
        var context = _context;
        _clipboardBusy = true;
        Refresh();
        try
        {
            await LineupClipboard.CopyAsync(window, invitation);
            if (context == _context) _notice = "已复制房间邀请文本。";
        }
        catch (ExternalException) { _notice = "复制失败，请重试。"; }
        finally { _clipboardBusy = false; Refresh(); }
    }

    private async void OnMuteMember(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || _session is not { Busy: false, IsSynchronized: true, State: { Phase: "playing" } state }
            || !state.Members.Any(m => m.Id == id && m.HasPlugin && id != state.SelfId)) return;
        var muted = state.Muted.ToHashSet();
        if (!muted.Remove(id)) muted.Add(id);
        await _session.MuteAsync([.. muted]);
    }

    private async void OnMuteAll(object sender, RoutedEventArgs e)
    {
        if (!MuteAllButton.IsEnabled || _session?.State is not { Phase: "playing" } state) return;
        var peers = state.Members.Where(member => member.Id != state.SelfId && member.HasPlugin).Select(member => member.Id).ToArray();
        await _session.MuteAsync(peers.All(id => state.Muted.Contains(id)) ? [] : peers);
    }

    private void OnAskLeave(object sender, RoutedEventArgs e)
    {
        if (!LeaveButton.IsEnabled || LeaveButton.Visibility != Visibility.Visible) return;
        Page.IsEnabled = false;
        Show(LeavePrompt, true);
        StayButton.Focus();
    }

    private void CloseLeavePrompt()
    {
        Page.IsEnabled = true;
        Show(LeavePrompt, false);
    }

    private void OnStay(object sender, RoutedEventArgs e)
    {
        CloseLeavePrompt();
        LeaveButton.Focus();
    }

    private async void OnLeave(object sender, RoutedEventArgs e)
    {
        if (LeavePrompt.Visibility != Visibility.Visible) return;
        CloseLeavePrompt();
        if (_session != null) await _session.LeaveAsync();
    }
}
