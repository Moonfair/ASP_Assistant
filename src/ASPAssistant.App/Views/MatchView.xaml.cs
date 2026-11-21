using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ASPAssistant.App.Services;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class MatchView : UserControl
{
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MatchSession? _session;
    private SettingsManager? _settings;
    private OverlayWindow? _overlay;
    private string? _phase, _roomId, _notice;
    private string _savedNickname = "";
    private bool _editingNickname, _clipboardBusy, _confirmationElapsed;
    private MemberRow[] _rows = [];

    public MatchView()
    {
        InitializeComponent();
        _countdown.Tick += (_, _) =>
        {
            if (_session?.ConfirmationSecondsRemaining > 0) UpdateCountdown();
            else
            {
                Refresh();
                ConfirmationExpired?.Invoke();
            }
        };
    }

    internal bool ShowConfirmationTakeover => _phase == "confirming" && !_confirmationElapsed;
    internal event Action? ConfirmationExpired;

    internal void Configure(MatchSession session, SettingsManager settings, IReadOnlyList<Strategy> strategies,
        CovenantCatalog covenants, OverlayWindow overlay)
    {
        _settings = settings;
        _overlay = overlay;
        _savedNickname = settings.LoadMatchNickname();
        Nickname.Text = _savedNickname;
        StrategyPicker.ItemsSource = strategies.DistinctBy(item => item.Name).ToArray();
        CovenantPicker.ItemsSource = covenants.CoreCovenants.Concat(covenants.AdditionalCovenants).Distinct().ToArray();
        TacticPicker.ItemsSource = new[] { "随便", "兜底", "攻坚" };
        TacticPicker.SelectedIndex = 0;
        UpdatePreferenceSummaries();
        _session = session;
        Refresh();
    }

    internal void Finish()
    {
        _countdown.Stop();
        SaveNickname();
    }

    internal void FocusConfirmation() => ConfirmButton.Focus();

    internal void Refresh()
    {
        if (_session is null) return;
        var state = _session.State;
        var phase = state?.Phase;
        if (phase != _phase || state?.RoomId != _roomId)
        {
            _editingNickname = false;
            _confirmationElapsed = false;
            _notice = null;
            if (state?.RoomId != _roomId) InvitationDraft.Clear();
            if (phase is null or "ended" || state?.RoomId != _roomId) CloseLeavePrompt();
            PageScroll.ScrollToTop();
            _roomId = state?.RoomId;
            _phase = phase;
        }

        var form = phase is null or "ended";
        var customForm = form && CustomTeam.IsChecked == true;
        var confirming = phase == "confirming";
        if (confirming && _session.ConfirmationSecondsRemaining == 0) _confirmationElapsed = true;
        var captain = state is not null && state.SelfId == state.CaptainId;
        var creating = phase == "creating" && captain;
        var joining = phase == "joining";
        var playing = phase == "playing";
        var self = state?.Members.FirstOrDefault(member => member.Id == state.SelfId);

        Heading.Text = phase switch
        {
            "recovering" => "正在核实匹配状态",
            "queued" => "正在匹配",
            "confirming" when _confirmationElapsed => "匹配确认",
            "confirming" => "匹配成功，请确认",
            "creating" => captain ? "创建游戏房间" : "等待队长创建房间",
            "joining" => captain || self?.Ready == true ? "等待队友加入房间" : "加入游戏房间",
            "playing" => "游戏进行中",
            "ended" => "本次匹配已结束",
            _ => customForm ? "自建队伍" : "组队匹配"
        };
        var instruction = phase switch
        {
            "recovering" => "正在向服务器核实匹配请求。你可以继续等待或退出。",
            "queued" => "正在寻找队友。你可以切换页签，匹配成功时会收到确认提醒。",
            "confirming" when _confirmationElapsed => "确认时间已到，正在同步房间状态。",
            "confirming" => self?.Confirmed == true ? "你已确认，正在等待其他队员。" : "请在倒计时结束前确认参加。",
            "creating" => captain ? "你是队长。请在游戏中创建房间，并在下方提交邀请文本。" : "队长提交后，这里会显示房间邀请文本。",
            "joining" when !captain && self?.Ready != true => "复制邀请文本并在游戏中加入房间，然后点击“我已就位”。",
            "joining" when state!.Members.Length < 4 => "将房间分享文本发给队友，在「自建队伍」中输入同一段文本即可加入。四人就位后开始。",
            "playing" => "点击“开始标记”或按住 Ctrl 在游戏画面上作画。笔迹保留 10 秒。",
            _ when customForm => "输入游戏房间的分享文本。第一个提交的人成为队长，其余队员使用同一段文本加入。",
            _ => null
        };
        SetText(Description, string.Join(" ", new[] { instruction, state?.Message }.Where(text => !string.IsNullOrWhiteSpace(text))));
        Show(Profile, form || _editingNickname);
        Show(TeamMode, form);
        TeamMode.IsEnabled = !_session.Busy && !_clipboardBusy;
        NicknameLabel.Text = form ? "昵称" : "下次匹配使用的昵称";
        EditProfileButton.Content = _editingNickname ? "完成修改" : "修改昵称";
        Show(EditProfileButton, !form);
        Show(Preferences, form);
        Show(JoinButton, form);
        JoinButton.Content = customForm ? "创建或加入队伍" : "发起匹配";
        Show(ConfirmButton, confirming);
        Show(Countdown, confirming);
        Show(CreateRoom, creating || customForm);
        InvitationLabel.Text = customForm ? "游戏房间分享文本" : "房间邀请文本";
        Show(SendInvitationButton, creating);
        Show(PublishedRoom, joining);
        if (PublishedInvitation.Text != state?.Invitation) PublishedInvitation.Text = state?.Invitation ?? "";
        Show(ReadyButton, joining && !captain);
        ReadyButton.Content = self?.Ready == true ? "已就位" : "我已就位";
        ReadyButton.IsEnabled = !_session.Busy && self?.Ready != true;
        Show(MarkButton, playing);
        MarkButton.Content = _overlay?.IsMatchMarking == true ? "结束标记" : "开始标记";
        Show(LeaveButton, !form);
        LeaveButton.IsEnabled = !_session.Busy;
        ApproveLeaveButton.IsEnabled = !_session.Busy;
        Show(RosterHeading, !form && state?.Members.Length > 0);
        RosterTitle.Text = joining ? $"队伍成员（{state!.Members.Length}/4）" : "队伍成员";
        Show(MuteAllButton, playing);

        UpdateMembers(form ? null : state);
        UpdateInputs();
        if (confirming) UpdateCountdown();
        if (ShowConfirmationTakeover) _countdown.Start();
        else _countdown.Stop();
        SetText(Feedback, _session.Error ?? (_session.Busy ? "正在提交…" : _notice));
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void SetText(TextBlock block, string? value)
    {
        block.Text = value ?? "";
        Show(block, !string.IsNullOrWhiteSpace(value));
    }

    private void UpdateInputs()
    {
        if (_session is null) return;
        JoinButton.IsEnabled = !_session.Busy && !_clipboardBusy && !string.IsNullOrWhiteSpace(Nickname.Text) &&
            StrategyPicker.SelectedItems.Count > 0 && (CustomTeam.IsChecked != true || !string.IsNullOrWhiteSpace(InvitationDraft.Text));
        SendInvitationButton.IsEnabled = !_session.Busy && !_clipboardBusy && !string.IsNullOrWhiteSpace(InvitationDraft.Text);
        PasteButton.IsEnabled = !_session.Busy && !_clipboardBusy;
        CopyButton.IsEnabled = !_clipboardBusy && !string.IsNullOrEmpty(PublishedInvitation.Text);
    }

    private void UpdateCountdown()
    {
        if (_session?.State is not { Phase: "confirming" } state) return;
        var seconds = _confirmationElapsed ? 0 : _session.ConfirmationSecondsRemaining;
        Countdown.Text = $"{seconds} 秒";
        var confirmed = state.Members.Any(member => member.Id == state.SelfId && member.Confirmed);
        ConfirmButton.Content = confirmed ? "已确认" : seconds == 0 ? "确认已截止" : "确认匹配";
        ConfirmButton.IsEnabled = !confirmed && seconds > 0 && !_session.Busy;
    }

    private sealed record MemberRow(string Id, string DisplayName, string Color, string Preferences, string Status,
        string MuteLabel, string MuteDescription, Visibility MuteVisibility);

    private void UpdateMembers(MatchSnapshot? state)
    {
        var peers = state?.Members.Where(member => member.Id != state.SelfId).ToArray() ?? [];
        var allMuted = peers.Length > 0 && peers.All(member => state!.Muted.Contains(member.Id));
        MuteAllButton.Content = allMuted ? "取消全部屏蔽" : "屏蔽所有人";
        MuteAllButton.IsEnabled = peers.Length > 0 && _session?.Busy == false;
        Roster.IsEnabled = _session?.Busy == false;
        MarkButton.IsEnabled = _session?.CanSendStroke == true || _overlay?.IsMatchMarking == true;
        if (state?.Phase == "playing" && allMuted)
            SetText(Description, "已屏蔽所有队友，无法发送笔迹。取消屏蔽一人即可继续。");
        else if (state?.Phase == "playing" && peers.Length == 0)
            SetText(Description, "其他队友均已离开房间。");

        var rows = state?.Members.Select(member =>
        {
            var self = member.Id == state.SelfId;
            var name = member.Nickname + (self ? "（我）" : "") + (member.Id == state.CaptainId ? " · 队长" : "");
            var status = state.Phase switch
            {
                "confirming" => member.Confirmed ? "已确认" : "待确认",
                "creating" => member.Id == state.CaptainId ? "正在创建房间" : "等待队长",
                "joining" => member.Ready || member.Id == state.CaptainId ? "已就位" : "待加入",
                "playing" => "已就位",
                _ => "匹配中"
            };
            if (state.Phase != "queued" && !string.IsNullOrWhiteSpace(member.AssignedTactic))
                status = $"分配：{member.AssignedTactic}  ·  {status}";
            var mute = state.Muted.Contains(member.Id) ? "取消屏蔽" : "屏蔽";
            return new MemberRow(member.Id, name, string.IsNullOrWhiteSpace(member.Color) ? "#FFE0E0E0" : member.Color,
                $"策略：{member.Strategy}  ·  盟约：{member.Covenant}  ·  意向：{member.Tactic}", status,
                mute, $"{mute}{member.Nickname}的笔迹", !self && state.Phase == "playing" ? Visibility.Visible : Visibility.Collapsed);
        }).ToArray() ?? [];
        if (_rows.SequenceEqual(rows)) return;
        _rows = rows;
        Roster.ItemsSource = rows;
    }

    private void OnInputChanged(object sender, RoutedEventArgs e) => UpdateInputs();
    private string[] SelectedStrategies() => StrategyPicker.SelectedItems.Cast<Strategy>().Select(item => item.Name).ToArray();
    private string[] SelectedCovenants() => CovenantPicker.SelectedItems.Cast<string>().ToArray();

    private void UpdatePreferenceSummaries()
    {
        var strategies = SelectedStrategies();
        var covenants = SelectedCovenants();
        StrategySummary.Text = strategies.Length == 0 ? "请选择策略" : string.Join("、", strategies);
        CovenantSummary.Text = covenants.Length == 0 ? "随便" : string.Join("、", covenants);
        AnyCovenant.IsEnabled = covenants.Length > 0;
    }

    private void OnPreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        var picker = (ListBox)sender;
        // Keyboard select-all and UI automation can select multiple items in one event.
        foreach (var item in e.AddedItems.Cast<object>().Reverse())
        {
            if (picker.SelectedItems.Count <= 4) break;
            picker.SelectedItems.Remove(item);
        }
        UpdatePreferenceSummaries();
        UpdateInputs();
    }

    private void OnAnyCovenant(object sender, RoutedEventArgs e) => CovenantPicker.UnselectAll();

    private void OnPreferenceWheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = (ScrollViewer)sender;
        if (e.Delta == 0 || (e.Delta > 0 ? scroll.VerticalOffset > 0 : scroll.VerticalOffset < scroll.ScrollableHeight))
            return;

        // At either end, let the surrounding form continue in the same direction.
        e.Handled = true;
        PageScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = Mouse.MouseWheelEvent });
    }
    private void OnTeamModeChanged(object sender, RoutedEventArgs e) => Refresh();
    private void OnNicknameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => SaveNickname();

    private void OnNicknameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SaveNickname();
        if (_editingNickname) ToggleProfileEditor();
        else
        {
            StrategySection.IsExpanded = true;
            StrategyPicker.Focus();
        }
        e.Handled = true;
    }

    private void SaveNickname()
    {
        var nickname = Nickname.Text.Trim();
        if (_settings is null || nickname == _savedNickname) return;
        try
        {
            _settings.SaveMatchNickname(nickname);
            _savedNickname = nickname;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Match", "Could not save nickname", ex);
            Notify("昵称未能保存到本地。");
        }
    }

    private void OnEditProfile(object sender, RoutedEventArgs e) => ToggleProfileEditor();
    private void ToggleProfileEditor()
    {
        if (_editingNickname) SaveNickname();
        _editingNickname = !_editingNickname;
        Refresh();
        if (_editingNickname)
        {
            Nickname.Focus();
            Nickname.SelectAll();
        }
    }

    private void Notify(string text)
    {
        _notice = text;
        SetText(Feedback, text);
    }

    private async void OnJoin(object sender, RoutedEventArgs e)
    {
        if (_session is null || !JoinButton.IsEnabled) return;
        SaveNickname();
        await _session.JoinAsync(Nickname.Text.Trim(), SelectedStrategies(), SelectedCovenants(), (string)TacticPicker.SelectedItem,
            CustomTeam.IsChecked == true ? InvitationDraft.Text.Trim() : null);
    }

    private async void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_session is not null && ConfirmButton.IsEnabled) await _session.ConfirmAsync();
    }

    private async void OnSendInvitation(object sender, RoutedEventArgs e)
    {
        if (_session is not null && SendInvitationButton.IsEnabled) await _session.InviteAsync(InvitationDraft.Text.Trim());
    }

    private async void OnReady(object sender, RoutedEventArgs e)
    {
        if (_session is not null && ReadyButton.IsEnabled) await _session.ReadyAsync();
    }

    private void OnMark(object sender, RoutedEventArgs e)
    {
        if (_overlay is null || !MarkButton.IsEnabled) return;
        var wasMarking = _overlay.IsMatchMarking;
        _notice = null;
        _overlay.ToggleMatchMarking();
        if (!wasMarking && !_overlay.IsMatchMarking)
            Notify(_overlay.IsCalibratingMarkers
                ? "请先在设置中结束标记位置校准，再开启作画。"
                : "暂时无法开启标记。请检查游戏来源，并让游戏窗口保持可见。");
        else Refresh();
    }

    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        if (_clipboardBusy || Window.GetWindow(this) is not { } window || CreateRoom.Visibility != Visibility.Visible) return;
        var roomId = _session?.State?.RoomId;
        _clipboardBusy = true;
        Refresh();
        try
        {
            var text = await LineupClipboard.ReadAsync(window, InvitationDraft.MaxLength);
            if (_session?.State?.RoomId != roomId || CreateRoom.Visibility != Visibility.Visible) return;
            if (text.Length > InvitationDraft.MaxLength) Notify("邀请文本超过 1024 个字符。");
            else if (string.IsNullOrWhiteSpace(text)) Notify("剪贴板中没有邀请文本。");
            else
            {
                InvitationDraft.Text = text;
                Notify("已粘贴邀请文本。");
            }
        }
        catch (ExternalException) { Notify("读取剪贴板失败，请重试。"); }
        finally { _clipboardBusy = false; Refresh(); }
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_clipboardBusy || Window.GetWindow(this) is not { } window || _session?.State?.Invitation is not { Length: > 0 } invitation) return;
        _clipboardBusy = true;
        UpdateInputs();
        try
        {
            await LineupClipboard.CopyAsync(window, invitation);
            Notify("已复制房间邀请文本。");
        }
        catch (ExternalException) { Notify("复制失败，请重试。"); }
        finally { _clipboardBusy = false; UpdateInputs(); }
    }

    private async void OnMuteMember(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || _session?.State is not { Phase: "playing" } state || _session.Busy) return;
        var muted = state.Muted.ToHashSet();
        if (!muted.Remove(id)) muted.Add(id);
        await _session.MuteAsync([.. muted]);
    }

    private async void OnMuteAll(object sender, RoutedEventArgs e)
    {
        if (_session?.State is not { Phase: "playing" } state || _session.Busy) return;
        var peers = state.Members.Where(member => member.Id != state.SelfId).Select(member => member.Id).ToArray();
        await _session.MuteAsync(peers.All(id => state.Muted.Contains(id)) ? [] : peers);
    }

    private void OnAskLeave(object sender, RoutedEventArgs e)
    {
        LeaveQuestion.Text = _session?.State?.Phase is "recovering" or "queued" ? "确定退出匹配吗？" : "确定退出房间吗？";
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
        CloseLeavePrompt();
        if (_session is not null) await _session.LeaveAsync();
    }
}
