using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class LineupEditorWindow : Window
{
    private readonly LineupEditorViewModel _vm;

    /// <summary>Set to the saved Lineup if user clicked Save, else null.</summary>
    public Lineup? Result { get; private set; }

    public LineupEditorWindow(LineupEditorViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        LineupNameBox.Text = _vm.Name;
        BuildSlotCards();
        RefreshCovenantPanel();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnLineupNameChanged(object sender, TextChangedEventArgs e)
    {
        _vm.Name = LineupNameBox.Text;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Result = _vm.ToLineup();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    // ── Slot card construction ──────────────────────────────────────────────

    private readonly List<Border> _slotCards = [];

    private void BuildSlotCards()
    {
        _slotCards.Clear();
        SlotsHost.Items.Clear();
        for (int i = 0; i < _vm.Slots.Count; i++)
        {
            var card = CreateSlotCard(i);
            _slotCards.Add(card);
            SlotsHost.Items.Add(card);
        }
    }

    private Border CreateSlotCard(int slotIndex)
    {
        var border = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("CardBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Tag = slotIndex,
        };
        RefreshSlotCard(border, slotIndex);
        return border;
    }

    private void RefreshSlotCard(Border border, int slotIndex)
    {
        var slot = _vm.Slots[slotIndex];
        var stack = new StackPanel();

        // Header
        var headerDock = new DockPanel { LastChildFill = true };

        if (string.IsNullOrEmpty(slot.OperatorName))
        {
            var pickBtn = new Button
            {
                Content = "+ 选择干员",
                Style = (Style)FindResource("EditorSecondaryButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 14, 8, 14),
                FontSize = 12,
            };
            pickBtn.Click += (_, _) => PickOperator(slotIndex);
            stack.Children.Add(pickBtn);
            border.Child = stack;
            return;
        }

        // Filled: portrait + name + remove button
        var iconBorder = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
        };
        var img = new Image { Stretch = Stretch.UniformToFill };
        TryLoadOperatorIcon(slot.OperatorName, img);
        iconBorder.Child = img;
        iconBorder.MouseLeftButtonUp += (_, _) => PickOperator(slotIndex);
        DockPanel.SetDock(iconBorder, Dock.Left);
        headerDock.Children.Add(iconBorder);

        var rightStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

        var nameRow = new DockPanel();
        var removeBtn = new Button
        {
            Content = "✕",
            FontSize = 11,
            Padding = new Thickness(6, 1, 6, 1),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "清空该槽位",
        };
        removeBtn.Click += (_, _) =>
        {
            _vm.SetSlotOperator(slotIndex, "");
            RefreshSlotCard(border, slotIndex);
            RefreshCovenantPanel();
        };
        DockPanel.SetDock(removeBtn, Dock.Right);
        nameRow.Children.Add(removeBtn);

        var nameText = new TextBlock
        {
            Text = slot.OperatorName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameRow.Children.Add(nameText);
        rightStack.Children.Add(nameRow);

        // Equipment chips (2 slots)
        var equipPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        for (int slotI = 0; slotI < LineupEditorViewModel.MaxEquipPerSlot; slotI++)
        {
            string? eqName = slotI < slot.Equipments.Count ? slot.Equipments[slotI] : null;
            var chip = new Button
            {
                Content = eqName ?? "+ 装备",
                Style = (Style)FindResource("ChipButton"),
                ToolTip = eqName ?? "添加装备",
            };
            int captured = slotI;
            chip.Click += (_, _) => PickEquipment(slotIndex, captured);
            if (eqName is not null) chip.Tag = "active";
            equipPanel.Children.Add(chip);
        }
        rightStack.Children.Add(equipPanel);

        headerDock.Children.Add(rightStack);
        stack.Children.Add(headerDock);

        // Tags
        var tagsLabel = new TextBlock
        {
            Text = "标签",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 8, 0, 2),
        };
        stack.Children.Add(tagsLabel);
        var tagPanel = new WrapPanel();
        foreach (LineupTag tag in Enum.GetValues<LineupTag>())
        {
            var chip = new Button
            {
                Content = tag.ToString(),
                Style = (Style)FindResource("ChipButton"),
            };
            if (slot.Tags.Contains(tag)) chip.Tag = "active";
            chip.Click += (_, _) =>
            {
                _vm.ToggleSlotTag(slotIndex, tag);
                RefreshSlotCard(border, slotIndex);
                RefreshCovenantPanel();
            };
            tagPanel.Children.Add(chip);
        }
        stack.Children.Add(tagPanel);

        border.Child = stack;
    }

    private void PickOperator(int slotIndex)
    {
        var current = _vm.Slots[slotIndex].OperatorName;
        var items = _vm.AllOperators
            .OrderBy(o => o.Tier)
            .ThenBy(o => o.Name, StringComparer.CurrentCulture)
            .Select(o => new SimplePickerDialog.PickerItem(
                o.Name,
                $"[T{o.Tier}] {o.Name}",
                o.IconPath,
                FormatOperatorCovenants(o)));

        var dlg = new SimplePickerDialog("选择干员", items, current,
            "搜索干员名 / 单击直接选中")
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.SetSlotOperator(slotIndex, dlg.Result ?? "");
            RefreshSlotCard(_slotCards[slotIndex], slotIndex);
            RefreshCovenantPanel();
        }
    }

    private static string FormatOperatorCovenants(Operator op)
    {
        var parts = new List<string>(op.CoreCovenants.Count + op.AdditionalCovenants.Count);
        parts.AddRange(op.CoreCovenants);
        parts.AddRange(op.AdditionalCovenants);
        return string.Join(" · ", parts);
    }

    private void PickEquipment(int slotIndex, int equipIndex)
    {
        var slot = _vm.Slots[slotIndex];
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        var current = equipIndex < slot.Equipments.Count ? slot.Equipments[equipIndex] : null;

        var items = _vm.AllEquipment
            .OrderBy(eq => eq.Tier)
            .ThenBy(eq => eq.Name, StringComparer.CurrentCulture)
            .Select(eq => new SimplePickerDialog.PickerItem(
                eq.Name,
                $"[T{eq.Tier}] {eq.Name}",
                eq.IconPath));

        var dlg = new SimplePickerDialog("选择装备", items, current,
            "搜索装备名 / 双击或确定")
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var picked = dlg.Result;
        if (picked is null) return;

        // Apply: replace at equipIndex, or remove if cleared
        var newEquips = new List<string>(slot.Equipments);
        if (string.IsNullOrEmpty(picked))
        {
            if (equipIndex < newEquips.Count)
                newEquips.RemoveAt(equipIndex);
        }
        else
        {
            // Disallow duplicates within the slot
            var existingIdx = newEquips.IndexOf(picked);
            if (existingIdx >= 0 && existingIdx != equipIndex)
                newEquips.RemoveAt(existingIdx);

            if (equipIndex < newEquips.Count)
                newEquips[equipIndex] = picked;
            else
                newEquips.Add(picked);

            while (newEquips.Count > LineupEditorViewModel.MaxEquipPerSlot)
                newEquips.RemoveAt(0);
        }
        _vm.Slots[slotIndex].Equipments.Clear();
        foreach (var n in newEquips) _vm.Slots[slotIndex].Equipments.Add(n);
        // Force collection-changed notification by reassigning the slot
        _vm.Slots[slotIndex] = new LineupSlot
        {
            OperatorName = slot.OperatorName,
            Equipments   = [.. newEquips],
            Tags         = [.. slot.Tags],
        };
        _vm.Recalculate();

        RefreshSlotCard(_slotCards[slotIndex], slotIndex);
        RefreshCovenantPanel();
    }

    private void TryLoadOperatorIcon(string opName, Image image)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "operators", $"{opName}.png");
        if (!File.Exists(iconPath)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            image.Source = bmp;
        }
        catch
        {
            // ignore – just leave the placeholder background visible
        }
    }

    // ── Covenant panel ──────────────────────────────────────────────────────

    private void RefreshCovenantPanel()
    {
        ActivatedSummaryText.Text = $"激活 {_vm.ActivatedCount} 个盟约 · 已选 {_vm.FilledSlotCount}/9 干员";
        // The ItemsControl re-binds against ActiveStats automatically.
        CovenantStatsList.ItemsSource = null;
        CovenantStatsList.ItemsSource = _vm.ActiveStats;
        EditorHint.Text = "提示：盟约阈值缺失（?）请在 data/covenants.user.json 中手动补齐。";
    }

    private void OnCovenantRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not bool activated) return;
        if (activated)
        {
            b.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4F, 0xC3, 0xF7));
            b.BorderBrush = (Brush)FindResource("AccentBrush");
            b.BorderThickness = new Thickness(1);
        }
        else
        {
            b.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            b.BorderThickness = new Thickness(0);
        }
    }
}
