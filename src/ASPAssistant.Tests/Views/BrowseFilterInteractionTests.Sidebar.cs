using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public partial class BrowseFilterInteractionTests
{
    // Runs in the existing WPF fixture: one Application/STA per process.
    private static void VerifySidebarWorkflow()
    {
        var operators = new OperatorBrowseViewModel();
        operators.LoadOperators([new() { Name = "测试干员", Tier = 1 }]);
        var equipment = new EquipmentBrowseViewModel();
        equipment.LoadEquipment([new() { Name = "长名称测试装备", Tier = 2 }]);
        var tracking = new TrackingViewModel();
        var window = new SidePanelWindow(operators, equipment, tracking, new(), new())
            { Height = 620, UseSideFilters = false };
        var tabs = (TabControl)window.FindName("MainTabs");
        var pages = tabs.Items.Cast<TabItem>().ToArray();
        pages.Select(t => t.Header).Should().Equal("干员", "装备", "追踪", "敌人", "阵容", "匹配", "数据");
        pages.Where(t => t.Visibility == Visibility.Visible).Select(t => t.Header)
            .Should().Equal("干员", "装备", "追踪", "敌人", "阵容", "匹配");
        window.SetMemoryReadingAllowed(true);
        foreach (var width in new[] { 280, 310, 400 })
        {
            window.Width = width;
            LayoutFilterWindow(window);
            var visible = tabs.Items.Cast<TabItem>().Where(t => t.Visibility == Visibility.Visible).ToList();
            visible.Select(t => t.Header).Should().Equal("干员", "装备", "追踪", "敌人", "阵容", "匹配", "数据");
            var first = visible[0];
            var origin = first.TranslatePoint(new Point(), tabs);
            first.ActualWidth.Should().BeGreaterThanOrEqualTo(56);
            first.ActualHeight.Should().BeGreaterThanOrEqualTo(26);
            for (int i = 0; i < visible.Count; ++i)
            {
                var tab = visible[i];
                tab.IsEnabled.Should().BeTrue();
                tab.FontSize.Should().Be(first.FontSize);
                tab.ActualWidth.Should().BeApproximately(first.ActualWidth, 1);
                tab.ActualHeight.Should().BeApproximately(first.ActualHeight, 1);
                var pos = tab.TranslatePoint(new Point(), tabs);
                pos.X.Should().BeApproximately(origin.X + i % 4 * first.ActualWidth, 1);
                pos.Y.Should().BeApproximately(origin.Y + i / 4 * first.ActualHeight, 1);
            }
            window.GameStateVm.Hud = new(14, 5, 123, 16);
            LayoutFilterWindow(window);
            var metrics = VisualChildren<TextBlock>((FrameworkElement)window.FindName("MatchStatusBar")).ToArray();
            metrics.Select(t => t.Text.Trim()).Should().BeEquivalentTo(
                "回合 14", "商店等级 5", "升级费用 16", "金币 123", "下回合 18");
            metrics.Should().OnlyContain(t => t.DesiredSize.Width <= t.ActualWidth + 1);
        }

        foreach (var tab in tabs.Items.Cast<TabItem>())
        {
            tab.IsSelected = true;
            LayoutFilterWindow(window);
            tabs.SelectedItem.Should().BeSameAs(tab);
            tabs.SelectedContent.Should().BeSameAs(tab.Content);
            VisualChildren<UserControl>(tabs).Should().Contain((UserControl)tab.Content);
        }
        tabs.SelectedItem = window.FindName("LineupTab"); // via lineup import/shortcut
        LayoutFilterWindow(window);
        tabs.Items.Cast<TabItem>().Single(t => t.IsSelected).Header.Should().Be("阵容");

        var list = (TrackingView)window.FindName("TrackingView");
        tabs.SelectedIndex = 2;
        LayoutFilterWindow(window);
        list.ActualWidth.Should().BeApproximately(tabs.ActualWidth, 1, "a short list still fills the panel");
        var empty = (FrameworkElement)list.FindName("EmptyTracking");
        empty.Visibility.Should().Be(Visibility.Visible);
        Click((Button)list.FindName("BrowseOperatorsButton"));
        tabs.SelectedIndex.Should().Be(0);

        CheckCatalog((OperatorBrowseView)window.FindName("OperatorView"), "搜索干员名称", "干员");
        tabs.SelectedIndex = 1;
        LayoutFilterWindow(window);
        CheckCatalog((EquipmentBrowseView)window.FindName("EquipmentView"), "搜索装备名称", "装备");
        tracking.TrackedOperators.Should().ContainSingle();
        tracking.TrackedEquipment.Should().ContainSingle();

        tabs.SelectedIndex = 2;
        LayoutFilterWindow(window);
        empty.Visibility.Should().Be(Visibility.Collapsed);
        foreach (var button in VisualChildren<Button>(list).Where(b => AutomationProperties.GetName(b) == "取消追踪").ToArray())
            Click(button);
        LayoutFilterWindow(window);
        tracking.AllEntries.Should().BeEmpty();
        empty.Visibility.Should().Be(Visibility.Visible);
        Click((Button)list.FindName("BrowseEquipmentButton"));
        tabs.SelectedIndex.Should().Be(1);
        equipment.SearchText.Should().Be("装备", "changing pages preserves the query");

        void CheckCatalog(FrameworkElement view, string label, string match)
        {
            var search = VisualChildren<TextBox>(view).Single(t => AutomationProperties.GetName(t) == label);
            search.Text = "无此目标";
            LayoutFilterWindow(window);
            var noResults = (FrameworkElement)view.FindName("NoResults");
            noResults.Visibility.Should().Be(Visibility.Visible);
            view.ActualWidth.Should().BeApproximately(tabs.ActualWidth, 1, "search does not shrink with no results");
            Click(VisualChildren<Button>(view).Single(b => AutomationProperties.GetName(b) == "清除搜索"));
            search.Text.Should().BeEmpty();
            search.Text = match;
            LayoutFilterWindow(window);
            noResults.Visibility.Should().Be(Visibility.Collapsed);
            Click(VisualChildren<Button>(view).Single(b => b.Name == "TrackButton"));
        }

        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
