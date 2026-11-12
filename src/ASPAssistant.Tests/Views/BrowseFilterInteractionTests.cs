using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
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
            List<Operator> operators = [
                new() { Name = "高阶", Tier = 3, CoreCovenants = ["甲"], AdditionalCovenants = ["丙"],
                    Normal = new() { Traits = [new() { TraitType = "作战", TraitDescription = "<获得时>攻击+1" }] } },
                new() { Name = "低阶", Tier = 1, CoreCovenants = ["乙"], AdditionalCovenants = ["丁"],
                    Elite = new() { Traits = [new() { TraitType = "经济", TraitDescription = "<结束时>金币+1" }] } },
                new() { Name = "中阶", Tier = 2 }
            ];
            var loads = 0;
            var catalog = new OperatorCatalog(operators, () =>
            {
                loads++;
                return Task.FromResult<List<Operator>>([new() { Name = "自选示例", SelfSelected = true }]);
            });
            var vm = new OperatorBrowseViewModel(catalog);
            vm.LoadOperators(operators);
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
            var selfSelected = (ListBox)view.FindName("SelfSelectedFilterList");
            selfSelected.SelectedIndex.Should().Be(0);
            loads.Should().Be(0);
            selfSelected.SelectedIndex = 1;
            loads.Should().Be(1);
            vm.FilteredOperators.Select(op => op.Name).Should().Equal("自选示例");
            ClickTag(view, "自选干员");
            selfSelected.SelectedIndex.Should().Be(0);
            vm.FilteredOperators.Should().HaveCount(3);

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
            VerifyDisabledCovenantsRemainFilterable();
            VerifySideFilterLayout();
            VerifyLineupPanelFollowsGameWindow();
            VerifyTabsWithoutFiltersKeepDockedSide();
            VerifyExpandedFiltersAvoidGameWindow();
            VerifyCovenantIcons();
            VerifyShortWindowAllowsFiltering();
            RenderFilterPreviews();
        }
        finally { app.Shutdown(); }
    }

    private static void VerifySideFilterLayout()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ASPAssistant.App;component/Resources/Logo.xaml", UriKind.Relative)
        });
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators([
            new() { Name = "甲", Tier = 1, CoreCovenants = ["炎"] },
            new() { Name = "乙", Tier = 2, CoreCovenants = ["莱塔尼亚"] }
        ]);
        var gearVm = new EquipmentBrowseViewModel();
        gearVm.LoadEquipment([new() { Name = "装备甲", Tier = 1 }, new() { Name = "装备乙", Tier = 2 }]);
        var window = new ASPAssistant.App.Windows.SidePanelWindow(vm, gearVm, new(), new(), new())
            { Left = 900, Top = 20, Height = 800 };
        LayoutFilterWindow(window);
        var toggle = window.FindName("LayoutToggle") as System.Windows.Controls.Primitives.ToggleButton;
        toggle.Should().NotBeNull("a layout switch should select the new sidebar layout");
        var view = (OperatorBrowseView)window.FindName("OperatorView");
        var core = (ListBox)view.FindName("CoreCovenantFilterList");
        var filterToggle = (System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle");
        core.Items.Cast<object>().Should().Contain("炎", "the catalog should be bound before interaction");
        core.SelectedItems.Add("炎");
        core.SelectedItems.Cast<string>().Should().Equal("炎");
        filterToggle.IsChecked = true;
        double right = window.Left + window.Width;
        double listWidth = window.Width;
        double contentWidth = ((FrameworkElement)window.FindName("MainSurface")).ActualWidth;
        toggle!.IsChecked = true;
        window.Width.Should().BeGreaterThan(listWidth);
        LayoutFilterWindow(window);
        var sidePanel = (FrameworkElement)window.FindName("SideFilterPanel");
        var collapseButton = (Button)window.FindName("CollapseSideFiltersButton");
        var filterTitle = VisualChildren<TextBlock>(sidePanel).First(text => text.Text == "筛选");
        collapseButton.TranslatePoint(new Point(collapseButton.ActualWidth, 0), sidePanel).X
            .Should().BeLessThan(filterTitle.TranslatePoint(new Point(), sidePanel).X);
        ((FrameworkElement)window.FindName("MainSurface")).ActualWidth.Should().BeApproximately(contentWidth, 0.1);
        (window.Left + window.Width).Should().BeApproximately(right, 0.1);
        var scroll = (ScrollViewer)window.FindName("SideFilterScroll");
        scroll.Content.Should().BeSameAs(view.FindName("FilterContentPanel"));
        var tags = (ItemsControl)view.FindName("FilterTagsControl");
        var tagsHost = (ContentControl)window.FindName("SideFilterTags");
        var tagsScroll = (ScrollViewer)window.FindName("SideFilterTagScroll");
        tagsHost.Content.Should().BeSameAs(tags);
        tags.HorizontalAlignment.Should().Be(HorizontalAlignment.Left);
        tagsScroll.Visibility.Should().Be(Visibility.Visible);
        VisualChildren<Button>(tags).Single(button => Equals(button.Content, "炎"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.ActiveFilterTags.Should().BeEmpty();
        tagsScroll.Visibility.Should().Be(Visibility.Collapsed);
        core.SelectedItems.Add("炎");
        LayoutFilterWindow(window);
        core.SelectedItems.Cast<string>().Should().Equal("炎");
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("甲");
        var settingsPage = (FrameworkElement)window.FindName("SettingsPage");
        settingsPage.Visibility.Should().Be(Visibility.Collapsed);
        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        LayoutFilterWindow(window);
        settingsPage.Visibility.Should().Be(Visibility.Visible);
        ((FrameworkElement)window.FindName("MainContent")).Visibility.Should().Be(Visibility.Collapsed);
        ((TextBlock)window.FindName("SettingsVersion")).Text.Should().StartWith("当前版本 v");
        ((Button)window.FindName("SettingsCheckUpdateButton")).Content.Should().Be("检查更新");
        window.Width.Should().BeApproximately(listWidth, 0.1);
        scroll.Content.Should().BeNull();
        toggle.IsChecked.Should().BeTrue();
        ((Button)window.FindName("BackFromSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        settingsPage.Visibility.Should().Be(Visibility.Collapsed);
        scroll.Content.Should().BeSameAs(view.FindName("FilterContentPanel"));
        core.SelectedItems.Cast<string>().Should().Equal("炎");
        (window.Left + window.Width).Should().BeApproximately(right, 0.1);
        collapseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        filterToggle.IsChecked.Should().BeFalse();
        tagsHost.Content.Should().BeSameAs(tags);
        ((TextBlock)window.FindName("FilterRailCount")).Text.Should().Be("1");
        LayoutFilterWindow(window);
        ((FrameworkElement)window.FindName("MainSurface")).ActualWidth.Should().BeApproximately(contentWidth, 0.1);
        window.Width.Should().BeLessThan(listWidth + 100);
        (window.Left + window.Width).Should().BeApproximately(right, 0.1);
        core.SelectedItems.Cast<string>().Should().Equal("炎");
        ((Button)window.FindName("ExpandSideFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        filterToggle.IsChecked.Should().BeTrue();
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedIndex = 1;
        var gearView = (EquipmentBrowseView)window.FindName("EquipmentView");
        var tiers = (ListBox)gearView.FindName("TierFilterList");
        LayoutFilterWindow(window);
        tiers.SelectedItems.Add(2);
        tabs.SelectedIndex = 2;
        window.Width.Should().BeApproximately(listWidth, 0.1);
        tagsHost.Content.Should().BeNull();
        tags.Parent.Should().NotBeNull();
        tags.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
        tabs.SelectedIndex = 0;
        scroll.Content.Should().BeSameAs(view.FindName("FilterContentPanel"));
        core.SelectedItems.Cast<string>().Should().Equal("炎");
        for (int i = 0; i < 3; i++)
        {
            toggle.IsChecked = false;
            window.Width.Should().BeApproximately(listWidth, 0.1);
            toggle.IsChecked = true;
            vm.FilteredOperators.Select(o => o.Name).Should().Equal("甲");
        }
        ((Button)window.FindName("ResetSideFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.FilteredOperators.Should().HaveCount(2);
        core.SelectedIndex.Should().Be(0);
        tabs.SelectedIndex = 1;
        tiers.SelectedItems.Cast<int>().Should().Equal(2);
        gearVm.FilteredEquipment.Select(g => g.Name).Should().Equal("装备乙");
        toggle.IsChecked = false;
        (window.Left + window.Width).Should().BeApproximately(right, 0.1);

        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        settingsPage.Visibility.Should().Be(Visibility.Visible);
        window.ImportWebsiteLineupAsync(new string('a', 64)).GetAwaiter().GetResult();
        settingsPage.Visibility.Should().Be(Visibility.Collapsed);
        ((FrameworkElement)window.FindName("MainContent")).Visibility.Should().Be(Visibility.Visible);
        tabs.SelectedItem.Should().BeSameAs(window.FindName("LineupTab"));
    }

    private static void VerifyLineupPanelFollowsGameWindow()
    {
        var window = new ASPAssistant.App.Windows.SidePanelWindow(new(), new(), new(), new(), new())
            { Width = 280, UseSideFilters = true };
        ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("LineupTab");
        var monitor = User32.GetMonitorRect(0, 0);
        var game = new RECT
        {
            Left = monitor.Left + 20, Right = monitor.Right - 300,
            Top = monitor.Top + 20, Bottom = monitor.Bottom - 60
        };
        window.UpdatePosition(game, gameActuallyMoved: true);
        window.Left.Should().Be(game.Right, "the 280-wide lineup panel fits in the 300-wide margin");

        window.Width = 360;
        game.Right = monitor.Right - 330;
        window.UpdatePosition(game, gameActuallyMoved: true);
        (window.Left + window.Width).Should().Be(game.Right, "the wider panel fits against the game's inside edge");

        game.Left = monitor.Left;
        game.Right = monitor.Right;
        window.UpdatePosition(game, gameActuallyMoved: true);
        (window.Left + window.Width).Should().Be(game.Right, "the panel remains on-screen in fullscreen");
    }

    private static void VerifyTabsWithoutFiltersKeepDockedSide()
    {
        var window = new ASPAssistant.App.Windows.SidePanelWindow(new(), new(), new(), new(), new());
        var mainWidth = window.Width;
        window.UseSideFilters = true;
        var tabs = (TabControl)window.FindName("MainTabs");
        var expand = (Button)window.FindName("ExpandSideFiltersButton");
        var monitor = User32.GetMonitorRect(0, 0);
        var game = new RECT
        {
            Left = monitor.Left + 680, Right = monitor.Right - (int)mainWidth - 12,
            Top = monitor.Top + 20, Bottom = monitor.Bottom - 60
        };
        window.UpdatePosition(game, gameActuallyMoved: true);
        (window.Left + window.Width).Should().Be(game.Left, "the filter rail only fits on the left");

        tabs.SelectedIndex = 2; // Tracking
        window.Width.Should().Be(mainWidth);
        (window.Left + window.Width).Should().Be(game.Left, "hiding the rail keeps the main panel beside the same edge");
        window.UpdatePosition(game, gameActuallyMoved: false);
        (window.Left + window.Width).Should().Be(game.Left, "polling the same game bounds keeps the panel in place");

        tabs.SelectedIndex = 1; // Equipment
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Width.Should().BeGreaterThan(mainWidth);
        (window.Left + window.Width).Should().Be(game.Left);
        tabs.SelectedItem = window.FindName("LineupTab");
        window.Width.Should().Be(mainWidth);
        (window.Left + window.Width).Should().Be(game.Left, "the lineup tab keeps the same edge when expanded filters are hidden");
        window.UpdatePosition(game, gameActuallyMoved: false);
        (window.Left + window.Width).Should().Be(game.Left);
        tabs.SelectedIndex = 1;
        (window.Left + window.Width).Should().Be(game.Left, "returning to equipment restores filters beside the main panel");

        tabs.SelectedItem = window.FindName("LineupTab");
        game.Left = monitor.Left + 20;
        game.Right = monitor.Right - 680;
        window.UpdatePosition(game, gameActuallyMoved: true);
        window.Left.Should().Be(game.Right, "moving the game can still move the assistant to the other side");
        window.Width.Should().Be(mainWidth);
    }

    private static void VerifyExpandedFiltersAvoidGameWindow()
    {
        var window = new ASPAssistant.App.Windows.SidePanelWindow(new(), new(), new(), new(), new())
            { Height = 800 };
        var mainWidth = window.Width;
        window.UseSideFilters = true;
        var expand = (Button)window.FindName("ExpandSideFiltersButton");
        var expandArrow = (TextBlock)window.FindName("ExpandSideFiltersArrow");
        var collapse = (Button)window.FindName("CollapseSideFiltersButton");
        var filters = (FrameworkElement)window.FindName("SideFilterPanel");
        var results = (FrameworkElement)window.FindName("MainSurface");
        var monitor = User32.GetMonitorRect(0, 0);
        var game = new RECT
        {
            Left = monitor.Left + 680, Right = monitor.Right - 20,
            Top = monitor.Top + 20, Bottom = monitor.Bottom - 60
        };
        window.UpdatePosition(game, gameActuallyMoved: true);
        expandArrow.Text.Should().Be("‹", "the filters have room to open to the left");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        collapse.Content.Should().Be("› 收起");
        (window.Left + window.Width).Should().Be(game.Left, "the expanded window fits to the left of the game");
        window.UpdatePosition(game, gameActuallyMoved: false);
        (window.Left + window.Width).Should().Be(game.Left, "the next tracking update must keep the expanded placement");
        LayoutFilterWindow(window);
        filters.TranslatePoint(new Point(filters.ActualWidth, 0), results).X.Should().Be(0);

        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        game.Left = monitor.Left + 20;
        game.Right = monitor.Right - 680;
        window.UpdatePosition(game, gameActuallyMoved: true);
        expandArrow.Text.Should().Be("›", "the filters have room to open to the right");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        collapse.Content.Should().Be("‹ 收起");
        window.Left.Should().Be(game.Right, "both the list and the expanded filters fit outside the game on the right");
        LayoutFilterWindow(window);
        filters.TranslatePoint(new Point(), results).X.Should().Be(results.ActualWidth);

        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        game.Right = monitor.Right - (int)mainWidth - 40;
        window.UpdatePosition(game, gameActuallyMoved: true);
        expandArrow.Text.Should().Be("‹", "only the rail fits outside, so the filters open inward from the right");
        var widthWithRail = window.Width;
        widthWithRail.Should().BeGreaterThan(mainWidth);
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        collapse.Content.Should().Be("› 收起");
        window.Width.Should().Be(widthWithRail, "expanding keeps the existing footprint when only the rail fits externally");
        window.Left.Should().Be(game.Right);

        game.Right = monitor.Right - (int)mainWidth - 12;
        window.UpdatePosition(game, gameActuallyMoved: true);
        window.Left.Should().Be(game.Right, "the right margin still fits the main window");
        window.Width.Should().Be(mainWidth, "expanded filters stay inside the window when its full width cannot fit outside the game");
        LayoutFilterWindow(window);
        filters.ActualWidth.Should().BeGreaterThan(200);
        filters.TranslatePoint(new Point(filters.ActualWidth, 0), results).X.Should().BeLessThanOrEqualTo(results.ActualWidth + 28);

        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Left.Should().Be(game.Right, "the collapsed rail must also stay outside the game");
        window.Width.Should().Be(mainWidth);
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdatePosition(game, gameActuallyMoved: false);
        window.Left.Should().Be(game.Right);
        window.Width.Should().Be(mainWidth);

        game.Right = monitor.Right - 60;
        window.UpdatePosition(game, gameActuallyMoved: true);
        (window.Left + window.Width).Should().Be(game.Right, "inset filters follow the game when neither margin fits the panel");

        game.Left = monitor.Left;
        game.Right = monitor.Right;
        window.UpdatePosition(game, gameActuallyMoved: true);
        window.Width.Should().Be(mainWidth, "fullscreen filters must not increase the assistant's footprint");
        (window.Left + window.Width).Should().Be(monitor.Right);
        collapse.Content.Should().Be("› 收起");
        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        expandArrow.Text.Should().Be("‹", "the right-hand rail opens to the left in fullscreen");

        game.Left = monitor.Left + (int)mainWidth + 40;
        game.Right = monitor.Right - 20;
        window.UpdatePosition(game, gameActuallyMoved: true);
        expandArrow.Text.Should().Be("›", "the left-hand rail opens inward when there is no room to expand outward");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        collapse.Content.Should().Be("‹ 收起");

        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        game.Right = monitor.Right - (int)mainWidth - 12;
        window.UpdatePosition(game, gameActuallyMoved: true);
        (window.Left + window.Width).Should().Be(game.Left);
        expandArrow.Text.Should().Be("›", "expanding moves the filters from the left of the game to the right");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Left.Should().Be(game.Right);
        collapse.Content.Should().Be("‹ 收起", "collapsing returns the rail to the left of the game");
    }

    private static void VerifyShortWindowAllowsFiltering()
    {
        var store = new ASPAssistant.Core.Data.JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(store.LoadOperatorsAsync().GetAwaiter().GetResult());
        var view = new OperatorBrowseView { DataContext = vm };
        ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle")).IsChecked = true;
        view.Measure(new Size(310, 500));
        view.Arrange(new Rect(0, 0, 310, 500));
        view.UpdateLayout();
        var results = VisualChildren<ScrollViewer>((ItemsControl)view.FindName("OperatorListControl")).Single();
        results.ViewportHeight.Should().BeGreaterThan(100, "top filters must leave a usable results area in short windows");
    }

    private static void VerifyCovenantIcons()
    {
        var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "covenants.json")));
        foreach (var name in catalog.RootElement.GetProperty("coreCovenants").EnumerateArray()
            .Concat(catalog.RootElement.GetProperty("additionalCovenants").EnumerateArray()))
        {
            var icon = new CovenantIcon { Covenant = name.GetString(), Width = 16, Height = 16, Foreground = Brushes.Cyan };
            icon.Visibility.Should().Be(Visibility.Visible, $"{name} should have its offline icon");
            icon.Measure(new Size(16, 16));
            icon.Arrange(new Rect(0, 0, 16, 16));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(icon);
            var pixels = new byte[16 * 16 * 4];
            bitmap.CopyPixels(pixels, 16 * 4, 0);
            Enumerable.Range(0, 256).Count(i => pixels[i * 4 + 3] > 0).Should().BeGreaterThan(0);
        }
        var brokenName = "broken-covenant-" + Guid.NewGuid().ToString("N");
        var brokenPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "covenants", brokenName + ".png");
        try
        {
            File.WriteAllBytes(brokenPath, [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82]);
            var damaged = new CovenantIcon();
            Action load = () => damaged.Covenant = brokenName;
            load.Should().NotThrow("a corrupt optional icon must fall back to the text label");
            damaged.Visibility.Should().Be(Visibility.Collapsed);
            Action remove = () => File.Delete(brokenPath);
            remove.Should().NotThrow("failed decoding must release the file handle");
        }
        finally
        {
            try { File.Delete(brokenPath); }
            catch (IOException) { /* A failed decoder may retain its URI stream until collection. */ }
        }
        var missing = new CovenantIcon { Covenant = "不存在的盟约" };
        missing.Visibility.Should().Be(Visibility.Collapsed);
        missing.Covenant = new OperatorFilterTag(OperatorFilterKind.Trait, "炎", "炎");
        missing.Visibility.Should().Be(Visibility.Collapsed, "non-covenant labels must not borrow a matching-name icon");
        missing.Covenant = new OperatorFilterTag(OperatorFilterKind.CoreCovenant, "炎", "炎");
        missing.Visibility.Should().Be(Visibility.Visible);
        missing.Covenant = "../outside";
        missing.Visibility.Should().Be(Visibility.Collapsed);
    }

    private static void RenderFilterPreviews()
    {
        var directory = Environment.GetEnvironmentVariable("ASPA_FILTER_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var store = new ASPAssistant.Core.Data.JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(store.LoadOperatorsAsync().GetAwaiter().GetResult());
        var gearVm = new EquipmentBrowseViewModel();
        gearVm.LoadEquipment(store.LoadEquipmentAsync().GetAwaiter().GetResult().Item1);
        var window = new ASPAssistant.App.Windows.SidePanelWindow(vm, gearVm, new(), new(), new())
            { Left = 900, Top = 20, Height = 820 };
        LayoutFilterWindow(window);
        var view = (OperatorBrowseView)window.FindName("OperatorView");
        ((ListBox)view.FindName("CoreCovenantFilterList")).SelectedItems.Add("莱塔尼亚");
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle");
        window.UseSideFilters = true;
        toggle.IsChecked = true;
        Render("sidebar-expanded.png");
        toggle.IsChecked = false;
        Render("sidebar-collapsed.png");
        window.UseSideFilters = false;
        toggle.IsChecked = true;
        Render("top-filters.png");
        window.UseSideFilters = true;
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 1;
        ((System.Windows.Controls.Primitives.ToggleButton)((EquipmentBrowseView)window.FindName("EquipmentView")).FindName("FilterToggle")).IsChecked = true;
        Render("equipment-sidebar.png");
        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render("settings-page.png");
        void Render(string name)
        {
            LayoutFilterWindow(window);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && VisualChildren<Image>((FrameworkElement)window.Content).Any(i => i.Name == "IconImage" && i.Source == null))
            {
                Thread.Sleep(10);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            ((FrameworkElement)window.Content).UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(window.Width * 1.5), (int)(window.Height * 1.5), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render((FrameworkElement)window.Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name));
            encoder.Save(file);
        }
    }

    private static void LayoutFilterWindow(Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
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

    private static void VerifyDisabledCovenantsRemainFilterable()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators([
            new() { Name = "甲", Tier = 1, CoreCovenants = ["炎", "萨尔贡"], AdditionalCovenants = ["灵巧", "精准"] },
            new() { Name = "乙", Tier = 2, CoreCovenants = ["炎"], AdditionalCovenants = ["灵巧"] }
        ]);
        var view = new OperatorBrowseView { DataContext = vm };
        ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle")).IsChecked = true;
        LayoutView(view);
        var core = (ListBox)view.FindName("CoreCovenantFilterList");
        var other = (ListBox)view.FindName("OtherCovenantFilterList");
        var coreChip = (ListBoxItem)core.ItemContainerGenerator.ContainerFromItem("萨尔贡");
        var otherChip = (ListBoxItem)other.ItemContainerGenerator.ContainerFromItem("精准");
        coreChip.Opacity.Should().Be(1);
        otherChip.Opacity.Should().Be(1);

        vm.CovenantAvailability = new(new HashSet<string> { "炎" }, new HashSet<string> { "灵巧" });
        LayoutView(view);
        coreChip.Opacity.Should().BeLessThan(1);
        otherChip.Opacity.Should().BeLessThan(1);
        coreChip.ToolTip.Should().Be("本局禁用");
        coreChip.IsEnabled.Should().BeTrue();
        ((ListBoxItem)core.ItemContainerGenerator.ContainerFromItem("炎")).Opacity.Should().Be(1);
        ((ListBoxItem)core.ItemContainerGenerator.ContainerFromIndex(0)).Opacity.Should().Be(1);

        core.SelectedItems.Add("萨尔贡");
        other.SelectedItems.Add("精准");
        LayoutView(view);
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("甲");
        coreChip.IsSelected.Should().BeTrue();
        coreChip.Opacity.Should().BeLessThan(1);
        var tags = (ItemsControl)view.FindName("FilterTagsControl");
        var coreTag = VisualChildren<Button>(tags).Single(b => Equals(b.Content, "萨尔贡"));
        var otherTag = VisualChildren<Button>(tags).Single(b => Equals(b.Content, "精准"));
        coreTag.Opacity.Should().BeLessThan(1);
        otherTag.Opacity.Should().BeLessThan(1);
        coreTag.ToolTip!.ToString().Should().Contain("本局禁用").And.Contain("点击移除");
        var card = VisualChildren<OperatorCard>(view).Single();
        var labels = VisualChildren<TextBlock>(card).Where(t => t.Text is "炎" or "萨尔贡" or "灵巧" or "精准")
            .ToDictionary(t => t.Text, t => (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(t)));
        labels["萨尔贡"].Opacity.Should().BeLessThan(labels["炎"].Opacity);
        labels["精准"].Opacity.Should().BeLessThan(labels["灵巧"].Opacity);

        vm.CovenantAvailability = null;
        LayoutView(view);
        coreChip.Opacity.Should().Be(1);
        otherChip.Opacity.Should().Be(1);
        coreChip.ToolTip.Should().BeNull();
        labels["萨尔贡"].Opacity.Should().Be(1);
        labels["精准"].Opacity.Should().Be(1);
        coreTag.Opacity.Should().Be(1);
        otherTag.Opacity.Should().Be(1);
        coreChip.IsSelected.Should().BeTrue();
        vm.FilteredOperators.Select(o => o.Name).Should().Equal("甲");

        vm.CovenantAvailability = new(new HashSet<string> { "炎" }, new HashSet<string> { "灵巧" });
        ((System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle")).IsChecked = false;
        LayoutView(view);
        coreTag.Opacity.Should().BeLessThan(1);
        ClickTag(view, "萨尔贡", OperatorFilterKind.CoreCovenant);
        core.SelectedIndex.Should().Be(0);
        other.SelectedItems.Cast<string>().Should().Contain("精准");
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
