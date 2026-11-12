using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Services;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class LineupEditorWindow : Window
{
    private enum Step { Roster, Description, Completed }

    private readonly LineupEditorViewModel _vm;
    private readonly Func<Lineup, Task<bool>> _save;
    private bool _saving;
    private bool _panelCloseApproved;
    private Step _step;
    private Lineup? _savedLineup;
    internal bool IsComplete => _step == Step.Completed;

    public LineupEditorWindow(LineupEditorViewModel vm, Func<Lineup, Task<bool>> save)
    {
        _vm = vm;
        _save = save;
        InitializeComponent();
        DataContext = vm;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30);
        Closing += OnClosing;
    }

    private void OnRemoveMember(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LineupMemberViewModel member) _vm.RemoveMember(member);
    }

    private void OnRosterMemberClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(RosterList, e.OriginalSource as DependencyObject) is ListBoxItem)
            _vm.CatalogPreview = null;
    }

    private void OnReturnToMember(object sender, RoutedEventArgs e) => _vm.CatalogPreview = null;

    private void OnMemberDetailChanged(object sender, DependencyPropertyChangedEventArgs e)
        => MemberDetailScroll?.ScrollToTop();

    private void OnMemberDetailVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && LineupDragDrop.GetActiveKind(this) == LineupDragKind.Equipment)
            ((ScrollViewer)sender).ScrollToTop();
    }

    private void OnCatalogDetailChanged(object sender, DependencyPropertyChangedEventArgs e)
        => ((ScrollViewer)sender).ScrollToTop();

    private void OnClearEquipment(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LineupEquipmentSlotViewModel slot })
        {
            _vm.ClearEquipment(slot.Member, slot.Number);
            e.Handled = true;
        }
    }

    private void OnRosterDragOver(object sender, DragEventArgs e) => SetDropEffect(e, e.Data.GetDataPresent(LineupDragDrop.OperatorFormat));
    private void OnMemberDragOver(object sender, DragEventArgs e) => SetDropEffect(e,
        e.Data.GetDataPresent(LineupDragDrop.OperatorFormat) || e.Data.GetDataPresent(LineupDragDrop.EquipmentFormat));
    private void OnSlotDragOver(object sender, DragEventArgs e) => SetDropEffect(e, e.Data.GetDataPresent(LineupDragDrop.EquipmentFormat));
    private void OnEditorSlotDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(LineupDragDrop.EquipmentFormat) || e.Data.GetDataPresent(LineupDragDrop.OperatorFormat))
            OnSlotDragOver(sender, e);
    }
    private void OnEditorSlotDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(LineupDragDrop.EquipmentFormat) || e.Data.GetDataPresent(LineupDragDrop.OperatorFormat))
            OnSlotDrop(sender, e);
    }

    private void OnNextStep(object sender, RoutedEventArgs e)
    {
        if (_step != Step.Roster) return;
        try
        {
            _vm.BuildLineup(includeDescription: false);
            _vm.ShowRequiredFieldErrors = false;
            _vm.Status = "";
            ShowStep(Step.Description);
        }
        catch (FormatException ex)
        {
            _vm.ShowRequiredFieldErrors = true;
            _vm.Status = ex.Message;
        }
    }

    private void OnPreviousStep(object sender, RoutedEventArgs e)
    {
        if (_step != Step.Description || _saving) return;
        if (!DescriptionWorkspace.TryApplyPendingGraphProperties()) return;
        _vm.Status = "";
        ShowStep(Step.Roster);
    }

    private void ShowStep(Step step)
    {
        if (step == Step.Description) DescriptionWorkspace.RefreshIconPickers();
        _step = step;
        var roster = step == Step.Roster ? Visibility.Visible : Visibility.Collapsed;
        var description = step == Step.Description ? Visibility.Visible : Visibility.Collapsed;
        var completed = step == Step.Completed ? Visibility.Visible : Visibility.Collapsed;
        IdentityFields.Visibility = WorkspaceMemberCount.Visibility = RosterWorkspace.Visibility = NextButton.Visibility = roster;
        DescriptionStage.Visibility = DescriptionWorkspace.Visibility = SaveButton.Visibility = BackButton.Visibility = description;
        CompletedStage.Visibility = ShareButton.Visibility = DoneButton.Visibility = completed;
        CancelButton.Visibility = step == Step.Completed ? Visibility.Collapsed : Visibility.Visible;
        (WorkspaceTitle.Text, WorkspaceHint.Text) = step switch
        {
            Step.Description => ("编辑阵容介绍", "填写阵容思路、装备搭配或操作顺序。"),
            Step.Completed => ("阵容保存完成", "这是其他玩家打开分享阵容时看到的预览。"),
            _ => ("构建你的阵容", "拖入干员组成阵容，再把装备拖到干员头像或指定装备槽。")
        };
    }
    private static void SetDropEffect(DragEventArgs e, bool accepted)
    {
        e.Effects = accepted && e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnRosterDrop(object sender, DragEventArgs e) => ApplyDrop(e);
    private void OnMemberDrop(object sender, DragEventArgs e) => ApplyDrop(e, (sender as FrameworkElement)?.DataContext as LineupMemberViewModel);
    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LineupEquipmentSlotViewModel slot })
            ApplyDrop(e, slot.Member, slot.Number);
    }
    private void ApplyDrop(DragEventArgs e, LineupMemberViewModel? member = null, int? slot = null)
    {
        e.Effects = LineupDragDrop.Apply(_vm, e.Data, member, slot) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_step != Step.Description || _saving) return;
        if (!DescriptionWorkspace.TryValidateSourceVideo() || !DescriptionWorkspace.TryApplyPendingGraphProperties()) return;
        try
        {
            var lineup = _vm.BuildLineup() with { Description = LineupDiagramSource.RemoveLegacyPositions(_vm.Description) };
            _saving = true;
            // Freeze the form while the snapshot is persisted so later edits cannot be lost.
            ((UIElement)Content).IsEnabled = false;
            if (await _save(lineup))
            {
                _savedLineup = lineup;
                _vm.IsDirty = false;
                _vm.Status = "";
                CompletedPreview.ShowLineup(lineup, _vm.FindIcon, _vm.FindStrategyIcon);
                ShowStep(Step.Completed);
            }
        }
        catch (FormatException ex) { _vm.Status = ex.Message; }
        finally { _saving = false; ((UIElement)Content).IsEnabled = true; }
    }

    private async void OnShare(object sender, RoutedEventArgs e)
    {
        if (_savedLineup is not { } lineup || !ShareButton.IsEnabled) return;
        ShareButton.IsEnabled = false;
        try
        {
            var text = _vm.ExportLineup(lineup);
            try
            {
                _vm.Status = "正在复制分享文本…";
                await LineupClipboard.CopyAsync(this, text);
                ShareFallback.Visibility = Visibility.Collapsed;
                _vm.Status = "分享文本已复制到剪切板。";
            }
            catch (Exception ex) when (ex is ExternalException or System.Threading.ThreadStateException)
            {
                ShareFallback.Text = text;
                ShareFallback.Visibility = Visibility.Visible;
                ShareFallback.Focus();
                ShareFallback.SelectAll();
                _vm.Status = ex.HResult == LineupClipboard.CannotOpenClipboard
                    ? "剪贴板仍被占用，可以手动复制分享文本。"
                    : "无法写入剪贴板，可以手动复制分享文本。";
            }
        }
        catch (FormatException ex) { _vm.Status = ex.Message; }
        finally { ShareButton.IsEnabled = true; }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
        => e.Cancel = !_panelCloseApproved && !ConfirmClose();

    internal void ApprovePanelClose() => _panelCloseApproved = true;

    internal void BringForward(string? notice = null)
    {
        if (notice is not null)
            _vm.Status = _saving ? "正在保存阵容，请稍候。" : notice;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // The side panel checks the active draft before shutting down the application.
    public bool ConfirmClose(Func<bool>? confirmDiscard = null)
    {
        if (_saving) { _vm.Status = "正在保存阵容，请稍候再关闭。"; return false; }
        return !_vm.IsDirty || (confirmDiscard ?? (() => MessageBox.Show(this,
            $"阵容“{(string.IsNullOrWhiteSpace(_vm.Name) ? "未命名" : _vm.Name)}”尚未保存，确定放弃本次编辑？", "放弃编辑",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))();
    }
}
