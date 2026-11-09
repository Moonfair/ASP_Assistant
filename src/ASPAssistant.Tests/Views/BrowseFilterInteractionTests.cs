using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class BrowseFilterInteractionTests
{
    [Fact]
    public void BrowseViews_MultiselectChipsAndAllStayInSyncWithResults()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyViews(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyViews()
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ASPAssistant.App;component/Resources/Styles.xaml", UriKind.Relative)
        });
        try
        {
            var vm = new OperatorBrowseViewModel();
            vm.LoadOperators([
                new() { Name = "高阶", Tier = 3, CoreCovenants = ["甲"], AdditionalCovenants = ["丙"],
                    Normal = new() { Traits = [new() { TraitType = "作战", TraitDescription = "<获得时>攻击+1" }] } },
                new() { Name = "低阶", Tier = 1, CoreCovenants = ["乙"], AdditionalCovenants = ["丁"],
                    Elite = new() { Traits = [new() { TraitType = "经济", TraitDescription = "<结束时>金币+1" }] } },
                new() { Name = "中阶", Tier = 2 }
            ]);
            var view = new OperatorBrowseView { DataContext = vm };
            ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle")).IsChecked = true;
            view.Measure(new Size(600, 1400));
            view.Arrange(new Rect(0, 0, 600, 1400));
            view.UpdateLayout();

            var tier = (ListBox)view.FindName("TierFilterList");
            var trait = (ListBox)view.FindName("TraitFilterList");
            var timing = (ListBox)view.FindName("TriggerTimingFilterList");
            var core = (ListBox)view.FindName("CoreCovenantFilterList");
            var other = (ListBox)view.FindName("OtherCovenantFilterList");
            foreach (var list in new[] { tier, trait, timing, core, other })
                list.SelectionMode.Should().Be(SelectionMode.Multiple);
            ((ListBox)view.FindName("AvailabilityFilterList")).SelectionMode.Should().Be(SelectionMode.Single);
            ((ListBox)view.FindName("StackingFilterList")).SelectionMode.Should().Be(SelectionMode.Single);

            tier.SelectedItems.Add(1);
            tier.SelectedItems.Add(3);
            tier.SelectedItems.Cast<object>().Should().Equal(1, 3);
            vm.FilteredOperators.Select(o => o.Name).Should().Equal("低阶", "高阶");
            vm.ActiveFilterTags.Select(t => t.Label).Should().Equal("Ⅰ", "Ⅲ");
            tier.SelectedItems.Remove(1);
            vm.FilteredOperators.Select(o => o.Name).Should().Equal("高阶");
            tier.SelectedItems.Remove(3);
            tier.SelectedIndex.Should().Be(0);
            vm.FilteredOperators.Should().HaveCount(3);

            foreach (var list in new[] { trait, timing, core, other })
            {
                list.SelectedItems.Add(list.Items[1]);
                list.SelectedItems.Add(list.Items[2]);
                list.SelectedItems.Count.Should().Be(2);
                vm.FilteredOperators.Select(o => o.Name).Should().Equal("低阶", "高阶");
                ClickAll(list);
                list.SelectedIndex.Should().Be(0);
                list.SelectedItems.Count.Should().Be(1);
                vm.FilteredOperators.Should().HaveCount(3);
            }

            tier.SelectedItems.Add(3);
            ClickAll(tier);
            vm.FilteredOperators.Select(o => o.Name).Should().Equal("低阶", "中阶", "高阶");
            vm.ActiveFilterTags.Select(t => t.Label).Should().BeEmpty();

            // Summary clicks must remove just one selection, even with the panel closed.
            ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle")).IsChecked = false;
            tier.SelectedItems.Add(1);
            tier.SelectedItems.Add(3);
            ClickTag(view, "Ⅰ");
            tier.SelectedItems.Cast<object>().Should().Equal(3);
            vm.FilteredOperators.Select(o => o.Name).Should().Equal("高阶");
            ClickTag(view, "Ⅲ");
            tier.SelectedIndex.Should().Be(0);
            vm.FilteredOperators.Should().HaveCount(3);

            foreach (var list in new[] { trait, timing, core, other })
            {
                list.SelectedItems.Add(list.Items[1]);
                list.SelectedItems.Add(list.Items[2]);
                var value = (string)list.Items[1];
                ClickTag(view, list == timing ? $"<{value}>" : value);
                list.SelectedItems.Cast<object>().Should().Equal(list.Items[2]);
                ClickTag(view, list == timing ? $"<{list.Items[2]}>" : (string)list.Items[2]);
                list.SelectedIndex.Should().Be(0);
                vm.FilteredOperators.Should().HaveCount(3);
            }
            foreach (var (listName, label) in new[] {
                ("AvailabilityFilterList", "本局可用"), ("StackingFilterList", "叠层") })
            {
                var list = (ListBox)view.FindName(listName);
                list.SelectedIndex = 1;
                ClickTag(view, label);
                list.SelectedIndex.Should().Be(0);
                vm.FilteredOperators.Should().HaveCount(3);
            }

            var equipmentVm = new EquipmentBrowseViewModel();
            equipmentVm.LoadEquipment([
                new() { Name = "普通", Tier = 1 },
                new() { Name = "转职", Tier = 2 },
                new() { Name = "高阶", Tier = 3 }
            ], [], ["转职"]);
            var equipmentView = new EquipmentBrowseView { DataContext = equipmentVm };
            ((System.Windows.Controls.Primitives.ToggleButton)equipmentView.FindName("FilterToggle")).IsChecked = true;
            equipmentView.Measure(new Size(600, 1400));
            equipmentView.Arrange(new Rect(0, 0, 600, 1400));
            equipmentView.UpdateLayout();
            var equipmentTier = (ListBox)equipmentView.FindName("TierFilterList");
            var type = (ListBox)equipmentView.FindName("TypeFilterList");
            equipmentTier.SelectionMode.Should().Be(SelectionMode.Multiple);
            type.SelectionMode.Should().Be(SelectionMode.Multiple);
            equipmentTier.SelectedItems.Add(1);
            equipmentTier.SelectedItems.Add(2);
            type.SelectedItems.Add("普通");
            type.SelectedItems.Add("转职");
            equipmentVm.FilteredEquipment.Select(e => e.Name).Should().Equal("普通", "转职");
            ClickAll(equipmentTier);
            ClickAll(type);
            equipmentVm.FilteredEquipment.Should().HaveCount(3);

            VerifyCenteredOperatorSurvivesTagRemoval(expanded: false);
            VerifyCenteredOperatorSurvivesTagRemoval(expanded: true);
            VerifyDuplicateLabelsAndEmptyResults();
        }
        finally { app.Shutdown(); }
    }

    private static void ClickAll(ListBox list)
    {
        list.UpdateLayout();
        var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Source = item
        });
    }

    private static void ClickTag(OperatorBrowseView view, string label, OperatorFilterKind? kind = null)
    {
        view.UpdateLayout();
        var tags = (ItemsControl)view.FindName("FilterTagsControl");
        var button = VisualChildren<Button>(tags).SingleOrDefault(b => Equals(b.Content, label) &&
            (kind == null || b.DataContext is OperatorFilterTag tag && tag.Kind == kind));
        button.Should().NotBeNull($"the active tag {label} should be clickable");
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        view.UpdateLayout();
    }

    private static void VerifyCenteredOperatorSurvivesTagRemoval(bool expanded)
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(Enumerable.Range(0, 90).Select(i => new Operator
        {
            Name = $"干员{i:00}", Tier = i / 30 + 1,
            Normal = new() { Traits = [new() { TraitType = "用于验证筛选摘要换行的长词条测试用标签",
                TraitDescription = string.Join("\n", Enumerable.Repeat("增加攻击力", 1 + i % 5)) }] }
        }).ToList());
        var view = new OperatorBrowseView { DataContext = vm };
        LayoutView(view);
        var tier = (ListBox)view.FindName("TierFilterList");
        tier.SelectedItems.Add(3);
        if (expanded)
            ((ListBox)view.FindName("TraitFilterList")).SelectedItems.Add("用于验证筛选摘要换行的长词条测试用标签");
        LayoutView(view);
        var results = (ItemsControl)view.FindName("OperatorListControl");
        var scroll = VisualChildren<ScrollViewer>(results).Single();
        scroll.ScrollToVerticalOffset(1800);
        LayoutView(view);
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle");
        toggle.IsChecked = expanded;
        LayoutView(view);
        if (expanded)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 40);
            LayoutView(view);
        }
        var filterRow = (RowDefinition)view.FindName("FilterContentRow");
        var originalFilterHeight = filterRow.Height;
        var tags = (ItemsControl)view.FindName("FilterTagsControl");
        double originalHeaderHeight = tags.ActualHeight;
        var anchor = VisualChildren<OperatorCard>(results)
            .Where(c => c.TranslatePoint(new Point(0, c.ActualHeight), scroll).Y > 0 &&
                c.TranslatePoint(new Point(), scroll).Y < scroll.ViewportHeight)
            .MinBy(c => Math.Abs(c.TranslatePoint(new Point(0, c.ActualHeight / 2), scroll).Y - scroll.ViewportHeight / 2))!;
        var op = (Operator)anchor.DataContext;
        double originalY = anchor.TranslatePoint(new Point(0, anchor.ActualHeight / 2), view).Y;

        ClickTag(view, "Ⅲ");
        LayoutView(view);
        var restored = VisualChildren<OperatorCard>(results).SingleOrDefault(c => ReferenceEquals(c.DataContext, op));
        restored.Should().NotBeNull("the centered operator should remain visible when earlier tiers reappear");
        restored!.TranslatePoint(new Point(0, restored.ActualHeight / 2), view).Y
            .Should().BeApproximately(originalY, 2, "the operator should stay at the same screen position");
        toggle.IsChecked.Should().Be(expanded, "restoring scroll must not collapse the filter panel");
        if (expanded)
        {
            tags.ActualHeight.Should().BeLessThan(originalHeaderHeight, "removing the tier should unwrap the header");
            filterRow.Height.Should().Be(originalFilterHeight, "programmatic scrolling should preserve partial filter collapse");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 10);
            LayoutView(view);
            filterRow.Height.Value.Should().BeApproximately(originalFilterHeight.Value - 10, 1,
                "the next user scroll should continue collapsing from the preserved panel height");
        }

        // Removing an OR selection can remove the anchor itself. The closest surviving
        // old result (the first tier-3 card here) should still be visible.
        toggle.IsChecked = false;
        tier.SelectedItems.Add(1);
        tier.SelectedItems.Add(3);
        LayoutView(view);
        scroll.ScrollToVerticalOffset(1800);
        LayoutView(view);
        ClickTag(view, "Ⅰ");
        LayoutView(view);
        var nearestSurvivor = VisualChildren<OperatorCard>(results)
            .Single(c => ((Operator)c.DataContext).Name == "干员60");
        nearestSurvivor.TranslatePoint(new Point(0, nearestSurvivor.ActualHeight / 2), scroll).Y
            .Should().BeInRange(0, scroll.ViewportHeight);
        vm.FilteredOperators.Should().HaveCount(30);

        // Rapid removal of the remaining tag must supersede any earlier pending restore.
        ClickTag(view, "Ⅲ");
        tier.SelectedItems.Add(2);
        ClickTag(view, "Ⅱ");
        LayoutView(view);
        vm.FilteredOperators.Should().HaveCount(90);
        tier.SelectedIndex.Should().Be(0);

        tier.SelectedItems.Add(3);
        LayoutView(view);
        scroll.ScrollToVerticalOffset(1800);
        LayoutView(view);
        ClickTag(view, "Ⅲ");
        view.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent
        });
        scroll.ScrollToTop();
        LayoutView(view);
        scroll.VerticalOffset.Should().BeApproximately(0, 1, "new user scrolling must cancel the queued position correction");
    }

    private static void VerifyDuplicateLabelsAndEmptyResults()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators([
            new() { Name = "甲", Tier = 1, CoreCovenants = ["作战"],
                Normal = new() { Traits = [new() { TraitType = "作战", TraitDescription = "攻击+1" }] } },
            new() { Name = "乙", Tier = 2, CoreCovenants = ["作战"] }
        ]);
        var view = new OperatorBrowseView { DataContext = vm };
        LayoutView(view);
        var core = (ListBox)view.FindName("CoreCovenantFilterList");
        var trait = (ListBox)view.FindName("TraitFilterList");
        var tier = (ListBox)view.FindName("TierFilterList");
        core.SelectedItems.Add("作战");
        trait.SelectedItems.Add("作战");
        ClickTag(view, "作战", OperatorFilterKind.Trait);
        core.SelectedItems.Cast<object>().Should().Contain("作战");
        trait.SelectedIndex.Should().Be(0);
        vm.FilteredOperators.Should().HaveCount(2);

        trait.SelectedItems.Add("作战");
        tier.SelectedItems.Add(2);
        LayoutView(view);
        vm.FilteredOperators.Should().BeEmpty();
        ClickTag(view, "Ⅱ");
        LayoutView(view);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("甲");
        vm.SearchText = "不存在";
        ClickTag(view, "作战", OperatorFilterKind.CoreCovenant);
        LayoutView(view);
        vm.FilteredOperators.Should().BeEmpty();
        vm.SearchText.Should().Be("不存在");
    }

    private static void LayoutView(FrameworkElement view)
    {
        view.Measure(new Size(310, 640));
        view.Arrange(new Rect(0, 0, 310, 640));
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
}
