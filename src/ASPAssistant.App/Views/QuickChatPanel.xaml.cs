using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Services;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;

namespace ASPAssistant.App.Views;

public partial class QuickChatPanel : UserControl
{
    private enum Picker { Closed, Template, Word }
    internal sealed record HistoryEntry(QuickPhrase Phrase, bool Available)
    {
        public string Text => Phrase.Text;
        public string Hint => Available ? "再次发送这条喊话" : "当前房间没有这个昵称";
    }
    private string[] _nicknames = [];
    private bool _wordIsNickname;
    private sealed record FeedLine(string Sender, string Text);
    private MatchSession? _session;
    private SettingsManager? _settings;
    private IReadOnlyList<QuickPhrase> _recent = [];
    private FeedLine[] _feed = [];
    private Picker _picker;
    private bool _changingPicker;
    private string? _room, _word;
    private string _template = "";

    public QuickChatPanel() => InitializeComponent();

    internal void Configure(MatchSession session, SettingsManager settings)
    {
        _session = session;
        _settings = settings;
        _template = QuickChatCatalog.Templates[0];
        _recent = settings.LoadRecentShouts().Select(s => new QuickPhrase(s.Template, s.Word, s.IsRoomNickname))
            .Where(QuickChatCatalog.IsKnownPhrase).DistinctBy(p => p.Text).Take(5).ToArray();
        UpdateHistory();
        UpdateDraft();
    }

    internal void Finish()
    {
        _session = null;
        _settings = null;
        ClosePicker();
    }

    internal void Refresh(bool playing, MatchSnapshot? roster)
    {
        Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        // A brief read pause keeps this room's composition, but cannot transmit.
        var room = playing ? _session?.Observation?.RoomId : null;
        if (_room != room)
        {
            _room = room;
            ClosePicker();
            _word = null;
            HistoryNotice.Visibility = Visibility.Collapsed;
            UpdateDraft();
        }

        var nicknames = playing && roster?.GameRoomId == room && room != null ? roster?.RoomNicknames ?? [] : [];
        if (!_nicknames.SequenceEqual(nicknames))
        {
            _nicknames = nicknames;
            if (_wordIsNickname && _word != null && !_nicknames.Contains(_word))
            {
                _word = null;
                _wordIsNickname = false;
                UpdateDraft();
            }
            if (_picker == Picker.Word) Filter();
            UpdateHistory();
        }

        var rows = roster is { Phase: "playing" } && playing
            ? roster.Shouts.Where(s => !roster.Muted.Contains(s.PlayerId)).TakeLast(3)
                .Select(s => new FeedLine(s.PlayerId == roster.SelfId ? "我" :
                    roster.Members.FirstOrDefault(m => m.Id == s.PlayerId)?.Nickname ?? "队友", s.Text)).ToArray() : [];
        if (!_feed.SequenceEqual(rows))
        {
            _feed = rows;
            Feed.ItemsSource = rows;
            FeedPanel.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        UpdateActions();
    }

    private void UpdateDraft()
    {
        TemplateLabel.Text = _template;
        WordLabel.Text = _word ?? "选择词汇";
        var parts = _template.Split('□', 2);
        Prefix.Text = parts[0];
        Slot.Text = _word ?? "＿＿";
        Suffix.Text = parts.Length > 1 ? parts[1] : "";
        UpdateActions();
    }

    private void UpdateActions()
    {
        var remaining = _session?.ShoutCooldownMs ?? 0;
        var canSend = _session?.CanSendShout == true && remaining == 0;
        Send.IsEnabled = canSend && _word != null;
        History.IsEnabled = canSend;
        Send.Content = remaining > 0 ? $"冷却 {(remaining + 999) / 1000}s" : "发送";
        Send.ToolTip = remaining > 0 ? "普通发送和一键重发共用 2 秒冷却。" :
            !canSend ? "需要同步到对局，并有可通信的插件队友。" :
            _word == null ? "请先选择填入的词汇。" : "发送给本房间的插件队友";
        ToolTipService.SetShowOnDisabled(Send, true);
    }

    private void UpdateHistory()
    {
        History.ItemsSource = _recent.Select(p => new HistoryEntry(p, QuickChatCatalog.IsValid(p, _nicknames))).ToArray();
        HistoryCount.Text = $"{_recent.Count} / 5";
        HistoryEmpty.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPickTemplate(object sender, RoutedEventArgs e) => TogglePicker(Picker.Template);
    private void OnPickWord(object sender, RoutedEventArgs e) => TogglePicker(Picker.Word);

    private void TogglePicker(Picker mode)
    {
        if (_picker == mode) { ClosePicker(); return; }
        OpenPicker(mode);
    }

    private void OpenPicker(Picker mode)
    {
        _picker = mode;
        _changingPicker = true;
        try
        {
            Types.ItemsSource = new[] { "全部" }.Concat(mode == Picker.Word
                ? QuickChatCatalog.Groups.Keys.Prepend(QuickChatCatalog.RoomNicknameCategory) : QuickChatCatalog.TemplateGroups.Keys).ToArray();
            Types.SelectedIndex = 0;
            Query.Clear();
            QueryHint.Text = mode == Picker.Word ? "搜索词汇，可只输部分字" : "搜索句式，可只输部分字";
            AutomationProperties.SetName(Query, QueryHint.Text);
            PickTemplate.Tag = mode == Picker.Template ? "tracked" : null;
            PickWord.Tag = mode == Picker.Word ? "tracked" : null;
            Chooser.Visibility = Visibility.Visible;
        }
        finally { _changingPicker = false; }
        Filter();
        Query.Focus();
    }

    private void ClosePicker()
    {
        _picker = Picker.Closed;
        Chooser.Visibility = Visibility.Collapsed;
        PickTemplate.Tag = PickWord.Tag = null;
    }

    private void OnQuery(object sender, TextChangedEventArgs e) => Filter();
    private void OnTypeChanged(object sender, SelectionChangedEventArgs e) => Filter();

    private void Filter()
    {
        if (_changingPicker || _picker == Picker.Closed) return;
        var type = Types.SelectedIndex > 0 ? Types.SelectedItem as string : null;
        var items = _picker == Picker.Word ? QuickChatCatalog.Search(type, Query.Text, _nicknames) :
            QuickChatCatalog.SearchTemplates(type, Query.Text);
        QueryHint.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Results.ItemsSource = items;
        Results.SelectedIndex = -1;
        if (items.Count > 0) Results.ScrollIntoView(items[0]);
        NoResults.Text = type == QuickChatCatalog.RoomNicknameCategory && _nicknames.Length == 0
            ? "暂未同步到房间昵称。" : "无匹配结果，试试更短的关键词。";
        NoResults.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnResultClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target &&
            ItemsControl.ContainerFromElement(Results, target) is ListBoxItem { Content: string value })
        {
            CommitChoice(value);
            e.Handled = true;
        }
    }

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && Results.Items.Count != 0)
        {
            Results.SelectedIndex = 0;
            Results.UpdateLayout();
            (Results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        }
        else if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(Query.Text) && Results.Items.Count == 1)
            CommitChoice((string)Results.Items[0]);
        else if (e.Key == Key.Escape) CancelPicker();
        else return;
        e.Handled = true;
    }

    private void OnResultKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && Results.SelectedItem is string value) CommitChoice(value);
        else if (e.Key == Key.Escape) CancelPicker();
        else return;
        e.Handled = true;
    }

    private void CancelPicker()
    {
        var focus = _picker == Picker.Word ? PickWord : PickTemplate;
        ClosePicker();
        focus.Focus();
    }

    internal void CommitChoice(string value)
    {
        if (_picker == Picker.Closed || !Results.Items.Contains(value)) return;
        if (_picker == Picker.Template)
        {
            if (_template != value) _word = null;
            _template = value;
            UpdateDraft();
            OpenPicker(Picker.Word);
        }
        else
        {
            _wordIsNickname = Equals(Types.SelectedItem, QuickChatCatalog.RoomNicknameCategory) || !QuickChatCatalog.IsStaticWord(value);
            _word = value;
            ClosePicker();
            UpdateDraft();
            if (!Send.Focus()) PickWord.Focus();
        }
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (Send.IsEnabled && _word != null) await Submit(new(_template, _word, _wordIsNickname));
    }

    private async void OnRepeat(object sender, RoutedEventArgs e)
    {
        if (History.IsEnabled && sender is Button { DataContext: HistoryEntry { Available: true } row }) await Submit(row.Phrase);
    }

    private async Task Submit(QuickPhrase phrase)
    {
        var session = _session;
        if (session == null || !await session.SendShoutAsync(phrase) || !ReferenceEquals(session, _session)) return;
        _recent = QuickChatCatalog.Remember(_recent, phrase);
        UpdateHistory();
        try
        {
            _settings?.SaveRecentShouts(_recent.Select(p => new RecentShout(p.Template, p.Word, p.IsRoomNickname)));
            HistoryNotice.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("QuickChat", "Could not save recent shouts", ex);
            HistoryNotice.Text = "已发出；最近记录未能保存到本地。";
            HistoryNotice.Visibility = Visibility.Visible;
        }
        UpdateActions();
    }
}
