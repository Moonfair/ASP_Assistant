using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using static ASPAssistant.App.Services.LineupSiteClient;

namespace ASPAssistant.App.Windows;

public partial class CatalogDetailWindow
{
    private const int MaxEquipmentChoices = 5;
    private readonly CancellationTokenSource _closed = new();
    private EquipmentChoice[] _equipmentChoices = [];
    private OperatorVoteStatus? _voteStatus;
    private OperatorVoteResults? _results;
    private bool _loadingResults;
    private bool _loadingStatus;
    private bool _submitting;
    private int? _skillChoice;

    private void InitializeVoting(IReadOnlyList<Equipment> equipment)
    {
        _equipmentChoices = equipment.OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .Select(item => new EquipmentChoice(item)).ToArray();
        foreach (var choice in _equipmentChoices) choice.PropertyChanged += OnChoiceChanged;
        EquipmentChoices.ItemsSource = _equipmentChoices;
        CollectionViewSource.GetDefaultView(_equipmentChoices).Filter = item =>
            ((EquipmentChoice)item).Item.Name.Contains(EquipmentSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase);

        foreach (var skill in _skills)
        {
            var option = new RadioButton
            {
                Content = $"{skill.Slot} · {skill.Name}",
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                GroupName = "OperatorSkillVote",
                Margin = new Thickness(0, 4, 14, 4)
            };
            option.Checked += (_, _) =>
            {
                _skillChoice = skill.Slot;
                UpdateSubmitButtons();
            };
            SkillChoices.Children.Add(option);
        }

        SkillVoteCard.Visibility = _skills.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        VotesTab.Visibility = Visibility.Visible;
    }

    private async void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Sections) || !ReferenceEquals(Sections?.SelectedItem, VotesTab) ||
            _site is not { } site || _operator is not { } op || _details.Season is not { } season) return;
        await Task.WhenAll(LoadResultsAsync(site, season, op.Name), LoadStatusAsync(site, season, op.Name));
    }

    private async void OnRefreshResults(object sender, RoutedEventArgs e)
    {
        if (_site is { } site && _operator is { } op && _details.Season is { } season)
            await LoadResultsAsync(site, season, op.Name);
    }

    private async void OnRetryStatus(object sender, RoutedEventArgs e)
    {
        if (_site is { } site && _operator is { } op && _details.Season is { } season)
            await LoadStatusAsync(site, season, op.Name);
    }

    private async Task LoadResultsAsync(LineupSiteClient site, string season, string name)
    {
        if (_loadingResults) return;
        _loadingResults = true;
        ResultsStatus.Text = "正在读取公开结果…";
        try
        {
            _results = await site.GetOperatorVotesAsync(season, name);
            if (_closed.IsCancellationRequested) return;
            RenderResults(_results);
            ResultsStatus.Text = "票数为公开汇总，投票后稍后更新。";
        }
        catch (LineupSiteException ex)
        {
            if (!_closed.IsCancellationRequested)
                ResultsStatus.Text = _results is null ? ex.Message : $"保留上次结果。{ex.Message}";
        }
        finally { _loadingResults = false; }
    }

    private void RenderResults(OperatorVoteResults results)
    {
        SkillResults.Children.Clear();
        foreach (var skill in _skills)
        {
            var votes = results.Skills.FirstOrDefault(row => row.Choice == skill.Slot)?.Votes ?? 0;
            SkillResults.Children.Add(Body($"{skill.Slot} · {skill.Name}　{votes} 票"));
        }

        EquipmentResults.Children.Clear();
        foreach (var row in results.Equipment.Take(MaxEquipmentChoices))
        {
            var gear = _equipmentChoices.FirstOrDefault(choice => choice.Item.Name == row.Choice)?.Item;
            if (gear is null)
            {
                EquipmentResults.Children.Add(Body($"{row.Choice}　{row.Votes} 票"));
                continue;
            }
            var icon = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
            DeferredIcon.SetPath(icon, gear.IconPath);
            var text = Body($"{row.Choice}　{row.Votes} 票");
            text.VerticalAlignment = VerticalAlignment.Center;
            var label = new DockPanel();
            label.Children.Add(icon);
            label.Children.Add(text);
            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };
            button.Click += (_, _) => new CatalogDetailWindow(gear, _details) { Owner = this }.ShowDialog();
            EquipmentResults.Children.Add(button);
        }
        if (EquipmentResults.Children.Count == 0) EquipmentResults.Children.Add(Body("还没有装备票"));
    }

    private async Task LoadStatusAsync(LineupSiteClient site, string season, string name)
    {
        if (_loadingStatus || _submitting) return;
        RetryStatus.Visibility = Visibility.Collapsed;
        VoteStatusText.Text = "";
        if (site.Email is null)
        {
            _voteStatus = null;
            RenderVotingState();
            return;
        }

        _loadingStatus = true;
        if (_voteStatus is null) VoteStatusText.Text = "正在确认投票状态…";
        RenderVotingState();
        try
        {
            _voteStatus = await site.GetOperatorVoteStatusAsync(season, name);
            if (!_closed.IsCancellationRequested) VoteStatusText.Text = "";
        }
        catch (LineupSiteException ex)
        {
            if (_closed.IsCancellationRequested) return;
            _voteStatus = null;
            VoteStatusText.Text = ex.Message;
            RetryStatus.Visibility = site.Email is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            _loadingStatus = false;
            if (!_closed.IsCancellationRequested) RenderVotingState();
        }
    }

    private void RenderVotingState()
    {
        var signedIn = _site?.Email is not null;
        SignInPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        SkillVoteForm.Visibility = signedIn && _voteStatus is { SkillVoted: false } ? Visibility.Visible : Visibility.Collapsed;
        EquipmentVoteForm.Visibility = signedIn && _voteStatus is { EquipmentVoted: false } ? Visibility.Visible : Visibility.Collapsed;
        if (_voteStatus?.SkillVoted == true) SkillFeedback.Text = "感谢你的技能投票！";
        if (_voteStatus?.EquipmentVoted == true) EquipmentFeedback.Text = "感谢你的装备推荐！";
        UpdateSubmitButtons();
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        if (_site is not { } site || _operator is not { } op || _details.Season is not { } season) return;
        if (new LineupSiteAccountWindow(site) { Owner = this }.ShowDialog() != true ||
            _closed.IsCancellationRequested) return;
        _voteStatus = null;
        SkillFeedback.Text = "";
        EquipmentFeedback.Text = "";
        await LoadStatusAsync(site, season, op.Name);
    }

    private void OnEquipmentSearch(object sender, TextChangedEventArgs e)
    {
        if (EquipmentChoices?.ItemsSource is not { } items) return;
        var view = CollectionViewSource.GetDefaultView(items);
        view.Refresh();
        NoGearResults.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EquipmentChoice.Selected)) UpdateSubmitButtons();
    }

    private void UpdateSubmitButtons()
    {
        var count = _equipmentChoices.Count(choice => choice.Selected);
        foreach (var choice in _equipmentChoices)
            choice.CanSelect = choice.Selected || count < MaxEquipmentChoices;
        var canSubmit = !_submitting && !_loadingStatus;
        SkillChoices.IsEnabled = canSubmit;
        EquipmentPicker.IsEnabled = canSubmit;
        SkillSubmit.IsEnabled = canSubmit && _skillChoice is not null;
        EquipmentSubmit.IsEnabled = canSubmit && count > 0;
        EquipmentPicker.Header = count == 0 ? "选择适用装备（最多 5 件）" : $"已选 {count}/5 件装备";
        EquipmentSubmit.Content = count == 0 ? "提交装备票" : $"提交装备票（{count} 件）";
    }

    private async void OnSkillVote(object sender, RoutedEventArgs e)
    {
        if (_site is not { } site || _operator is not { } op || _details.Season is not { } season ||
            _skillChoice is not { } skill || _voteStatus is not { SkillVoted: false }) return;
        await SubmitVoteAsync(SkillFeedback, () => site.VoteForSkillAsync(season, op.Name, skill, _closed.Token),
            status => status with { SkillVoted = true });
    }

    private async void OnEquipmentVote(object sender, RoutedEventArgs e)
    {
        if (_site is not { } site || _operator is not { } op || _details.Season is not { } season ||
            _voteStatus is not { EquipmentVoted: false }) return;
        var names = _equipmentChoices.Where(choice => choice.Selected).Select(choice => choice.Item.Name).ToArray();
        if (names.Length is < 1 or > MaxEquipmentChoices) return;
        await SubmitVoteAsync(EquipmentFeedback, () => site.VoteForEquipmentAsync(season, op.Name, names, _closed.Token),
            status => status with { EquipmentVoted = true });
    }

    private async Task SubmitVoteAsync(TextBlock feedback, Func<Task> submit, Func<OperatorVoteStatus, OperatorVoteStatus> record)
    {
        if (_submitting || _loadingStatus || _voteStatus is not { } status) return;
        _submitting = true;
        UpdateSubmitButtons();
        feedback.Text = "正在提交…";
        try
        {
            await submit();
            _voteStatus = record(status);
        }
        catch (LineupSiteException ex) { feedback.Text = ex.Message; }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        finally
        {
            _submitting = false;
            if (!_closed.IsCancellationRequested) RenderVotingState();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed.Cancel();
        _closed.Dispose();
        base.OnClosed(e);
    }

    private sealed class EquipmentChoice(Equipment item) : ObservableObject
    {
        private bool _selected;
        private bool _canSelect = true;

        public Equipment Item { get; } = item;
        public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
        public bool CanSelect { get => _canSelect; set => SetProperty(ref _canSelect, value); }
    }
}
