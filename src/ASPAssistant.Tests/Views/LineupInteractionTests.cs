using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App.Views;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("WPF views")]
public class LineupInteractionTests
{
    [Fact]
    public void VisualEditor_RoutedDropsTargetCorrectMemberAndSlot_AndRealIconsFitThreeOrFourColumns()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
                var operators = store.LoadOperatorsAsync().GetAwaiter().GetResult();
                var (gear, _) = store.LoadEquipmentAsync().GetAwaiter().GetResult();
                operators.Should().HaveCountGreaterThan(10);
                var vm = new LineupEditorViewModel(operators, gear) { Name = "先锋阵容", Author = "博士" };
                var window = new LineupEditorWindow(vm, _ => Task.FromResult(true));
                var content = (FrameworkElement)window.Content;
                Layout(content, 1020, 730);
                var catalog = (LineupCatalogView)window.FindName("Catalog");
                var sourceList = (ListBox)catalog.FindName("OperatorList");
                var zone = (Border)window.FindName("RosterDropZone");
                var idleBackground = zone.Background;
                LineupDragDrop.RunWithHints(window, LineupDragKind.Operator, () =>
                {
                    Layout(content, 1020, 730);
                    zone.BorderBrush.Should().BeSameAs(window.FindResource("AccentBrush"));
                    zone.Background.Should().NotBeSameAs(idleBackground);
                    RenderPreview(content, "lineup-operator-drop-hint.png", 1020, 730);
                });
                Layout(content, 1020, 730);
                zone.Background.Should().BeSameAs(idleBackground);
                var selected = vm.AvailableOperators.Take(6).ToArray();
                foreach (var op in selected)
                    Drop(zone, LineupDragDrop.OperatorFormat, op.Name).Should().Be(DragDropEffects.Copy);
                vm.Members.Should().HaveCount(6);
                Layout(content, 1020, 730);
                var roster = (ListBox)window.FindName("RosterList");
                var member = vm.Members[0];
                var portrait = Descendants<Border>(roster).Single(b => b.Name == "MemberDropTarget" && ReferenceEquals(b.DataContext, member));
                Drop(portrait, LineupDragDrop.EquipmentFormat, gear[0].Name).Should().Be(DragDropEffects.Copy);
                var hintStyle = window.FindResource("EmptyEquipmentHint");
                var detail = (FrameworkElement)window.FindName("MemberEditor");
                LineupDragDrop.RunWithHints(window, LineupDragKind.Equipment, () =>
                {
                    Layout(content, 1020, 730);
                    zone.Background.Should().BeSameAs(idleBackground);
                    var tileHints = Descendants<Border>(roster).Where(border => ReferenceEquals(border.Style, hintStyle)).ToArray();
                    var detailHints = Descendants<Border>(detail).Where(border => ReferenceEquals(border.Style, hintStyle)).ToArray();
                    tileHints.Should().HaveCount(12);
                    tileHints.Count(hint => hint.Opacity == 1).Should().Be(11);
                    detailHints.Select(hint => hint.Opacity).Should().Equal(0, 1);
                    tileHints.Concat(detailHints).Should().OnlyContain(hint => !hint.IsHitTestVisible);
                    RenderPreview(content, "lineup-equipment-drop-hints.png", 1020, 730);
                });
                Layout(content, 1020, 730);
                Descendants<Border>(content).Where(border => ReferenceEquals(border.Style, hintStyle))
                    .Should().OnlyContain(hint => hint.Opacity == 0);
                Drop(portrait, LineupDragDrop.EquipmentFormat, gear[1].Name).Should().Be(DragDropEffects.Copy);
                Drop(portrait, LineupDragDrop.EquipmentFormat, gear[2].Name).Should().Be(DragDropEffects.None);
                member.Equipment2.Should().Be(gear[1].Name);
                Layout(content, 1020, 730);
                var slot = Descendants<Grid>(portrait).Single(g => Equals(g.Tag, "2"));
                Drop(slot, LineupDragDrop.OperatorFormat, vm.AvailableOperators.First().Name).Should().Be(DragDropEffects.None);
                vm.Members.Should().HaveCount(6);
                Drop(slot, LineupDragDrop.EquipmentFormat, gear[2].Name).Should().Be(DragDropEffects.Copy);
                member.Equipment2.Should().Be(gear[2].Name);
                vm.Members[1].Equipment1.Should().BeEmpty();
                roster.SelectedItem.Should().BeSameAs(member);
                ((FrameworkElement)window.FindName("MemberEditor")).DataContext.Should().BeSameAs(member);
                var remove = Descendants<Button>(slot).Single();
                remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                member.Equipment2.Should().BeEmpty();
                Drop(slot, LineupDragDrop.EquipmentFormat, gear[1].Name).Should().Be(DragDropEffects.Copy);
                member.IsStackingCore = true;
                member.IsDamageCore = true;
                Layout(content, 1020, 730);
                IconLoadingTests.PumpUntil(() => Descendants<Image>(sourceList).Count(i => i.Source is not null) > 10
                    && Descendants<Image>(portrait).Count(i => i.Source is not null) == 3);
                vm.Status = "";
                RenderPreview(content, "lineup-visual-editor.png", 1020, 730);
                FirstRowCount(sourceList).Should().Be(4);

                roster.SelectedItem = vm.Members[1];
                Layout(content, 1020, 730);
                ((FrameworkElement)window.FindName("MemberEditor")).DataContext.Should().BeSameAs(vm.Members[1]);
                Descendants<CheckBox>(content).Single(c => Equals(c.Content, "叠层核心")).IsChecked = true;
                vm.Members[1].IsStackingCore.Should().BeTrue();
                member.IsStackingCore.Should().BeTrue();
                Descendants<TextBlock>(detail).Select(t => t.Text).Should().Contain(["普通效果", "精英效果"]);
                var preview = (ScrollViewer)window.FindName("CatalogDetailScroll");
                var previewOperator = vm.AvailableOperators.First();
                sourceList.SelectedItem = previewOperator;
                Layout(content, 1020, 730);
                preview.DataContext.Should().BeSameAs(previewOperator);
                Descendants<TextBlock>(preview).Select(t => t.Text)
                    .Should().Contain(text => text.Contains(previewOperator.Elite.Traits.First().TraitDescription));
                vm.SelectedMember.Should().BeSameAs(vm.Members[1]);
                RenderPreview(content, "lineup-catalog-operator-details.png", 1020, 730);
                ((TabControl)catalog.FindName("CatalogTabs")).SelectedIndex = 1;
                Layout(content, 1020, 730);
                var equipmentList = (ListBox)catalog.FindName("EquipmentList");
                IconLoadingTests.PumpUntil(() => Descendants<Image>(equipmentList).Count(i => i.Source is not null) > 2);
                RenderPreview(content, "lineup-equipment-grid.png", 1020, 730);
                var previewGear = gear.First(item => item.FusionFormula.Length > 0);
                equipmentList.SelectedItem = previewGear;
                Layout(content, 1020, 730);
                preview.DataContext.Should().BeSameAs(previewGear);
                Descendants<TextBlock>(preview).Select(t => t.Text).Should().Contain([
                    previewGear.Normal.EffectDescription, $"合成配方：{previewGear.FusionFormula}"]);
                if (previewGear.Elite.EffectDescription.Length > 0)
                    Descendants<TextBlock>(preview).Select(t => t.Text).Should().Contain(previewGear.Elite.EffectDescription);
                else
                    Descendants<TextBlock>(preview).Select(t => t.Text).Should().Contain("暂无效果资料");
                RenderPreview(content, "lineup-catalog-equipment-details.png", 1020, 730);
                LineupDragDrop.RunWithHints(window, LineupDragKind.Equipment, () =>
                {
                    Layout(content, 1020, 730);
                    preview.Visibility.Should().Be(Visibility.Collapsed);
                    Descendants<Border>(detail).Where(border => ReferenceEquals(border.Style, hintStyle))
                        .Should().OnlyContain(hint => hint.Opacity == 1);
                });
                Layout(content, 1020, 730);
                preview.Visibility.Should().Be(Visibility.Visible);
                Descendants<Button>(preview).Single(button => Equals(button.Content, "返回阵容干员"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                vm.CatalogPreview.Should().BeNull();

                ((TabControl)catalog.FindName("CatalogTabs")).SelectedIndex = 0;
                ((Expander)catalog.FindName("OperatorFilters")).IsExpanded = true;
                var tiers = (ListBox)catalog.FindName("TierFilters");
                Layout(content, 1020, 730);
                Descendants<TextBlock>((ListBoxItem)tiers.ItemContainerGenerator.ContainerFromIndex(0)).Select(t => t.Text).Should().Contain("全部");
                var filterScroll = (ScrollViewer)catalog.FindName("OperatorFilterScroll");
                var chip = (ListBoxItem)tiers.ItemContainerGenerator.ContainerFromIndex(0);
                foreach (var delta in new[] { -120, -120, 120, 120 })
                {
                    var before = filterScroll.VerticalOffset;
                    chip.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                    {
                        RoutedEvent = Mouse.MouseWheelEvent
                    });
                    Layout(content, 1020, 730);
                    ((filterScroll.VerticalOffset - before) * -Math.Sign(delta)).Should().BeGreaterThan(0,
                        "each wheel tick over a chip must reach the outer filter scroll");
                }
                tiers.SelectedItems.Add(operators.Max(o => o.Tier));
                Layout(content, 1020, 730);
                vm.AvailableOperators.Should().OnlyContain(o => o.Tier == operators.Max(x => x.Tier));
                RenderPreview(content, "lineup-full-filters.png", 1020, 730);
                tiers.SelectedItems.Clear();
                ((Expander)catalog.FindName("OperatorFilters")).IsExpanded = false;
                Layout(content, 800, 600);
                FirstRowCount(sourceList).Should().Be(3);
                RenderPreview(content, "lineup-visual-editor-small.png", 800, 600);
                ((Expander)catalog.FindName("OperatorFilters")).IsExpanded = true;
                Layout(content, 800, 600);
                sourceList.ActualHeight.Should().BeGreaterThanOrEqualTo(85, "expanded filters must leave an avatar row usable at minimum size");
                RenderPreview(content, "lineup-small-filters.png", 800, 600);
                vm.IsDirty = false;
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static int FirstRowCount(ListBox list)
    {
        var items = Descendants<ListBoxItem>(list).Take(8).ToList();
        var y = items[0].TranslatePoint(new Point(), list).Y;
        return items.Count(i => Math.Abs(i.TranslatePoint(new Point(), list).Y - y) < 1);
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        element.UpdateLayout();
    }

    private static DragDropEffects Drop(UIElement target, string format, string value, bool preview = false, Point? position = null)
    {
        var data = new DataObject(format, value);
        // WPF constructs routed drag events internally; use that same event shape without moving the user's mouse.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, target, position ?? new Point(8, 8)], null)!;
        args.RoutedEvent = preview ? DragDrop.PreviewDropEvent : DragDrop.DropEvent;
        target.RaiseEvent(args);
        return args.Effects;
    }

    [Fact]
    public void Editor_ControlsUpdateSavedContent_AndLibraryFitsNarrowPanel()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new LineupEditorViewModel(
                    [new() { Name = "地灵" }, new() { Name = "能天使", Tier = 1, IconPath = "icons/operators/能天使.png" }],
                    [new() { Name = "护盾" }, new() { Name = "长剑", Tier = 1 }, new() { Name = "法杖", Tier = 2, IconPath = "icons/equipment/维式重锤.png" }]);
                var markdown = """
                    # 输出顺序
                    - **核心** {{op:能天使}}
                    - *推荐装备* {{gear:法杖}}
                    - <u>优先上场</u>，==叠满后输出==

                    ```lineup
                    flowchart TD
                    A[{{op:能天使}}]
                    B[{{gear:法杖}}]
                    A -->|装备| B
                    highlight A
                    note A: 核心干员
                    ```
                    """;
                Lineup? persisted = null;
                var allowSave = false;
                var window = new LineupEditorWindow(editor, lineup =>
                {
                    if (allowSave) persisted = lineup;
                    return Task.FromResult(allowSave);
                });
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1020, 730));
                content.Arrange(new Rect(0, 0, 1020, 730));
                content.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var nextButton = (Button)window.FindName("NextButton");
                var saveButton = (Button)window.FindName("SaveButton");
                nextButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(content, 1020, 730);
                persisted.Should().BeNull();
                editor.Status.Should().NotBeEmpty();
                saveButton.Visibility.Should().Be(Visibility.Collapsed);
                var nameOutline = (Border)window.FindName("MissingNameOutline");
                var authorOutline = (Border)window.FindName("MissingAuthorOutline");
                var rosterZone = (Border)window.FindName("RosterDropZone");
                nameOutline.Opacity.Should().Be(1);
                authorOutline.Opacity.Should().Be(1);
                rosterZone.BorderBrush.Should().BeSameAs(window.FindResource("MissingFieldBrush"));
                ((TextBox)window.FindName("NameInput")).Text = "测试阵容";
                var authorInput = (TextBox)window.FindName("AuthorInput");
                authorInput.Text = "  ";
                Layout(content, 1020, 730);
                nameOutline.Opacity.Should().Be(0);
                authorOutline.Opacity.Should().Be(1);
                authorInput.Text = "测试作者";
                var catalog = (LineupCatalogView)window.FindName("Catalog");
                var list = (ListBox)catalog.FindName("OperatorList");
                list.Items.Count.Should().Be(2);
                list.SelectedItem = editor.CatalogOperators.Single(item => item.Name == "能天使");
                ((Button)catalog.FindName("AddOperatorButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                content.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                editor.Members.Should().ContainSingle();
                authorOutline.Opacity.Should().Be(0);
                rosterZone.BorderBrush.Should().NotBeSameAs(window.FindResource("MissingFieldBrush"));
                var checks = Descendants<CheckBox>(content).ToList();
                checks.Should().HaveCount(2);
                checks.ForEach(c => c.IsChecked = true);
                var slots = Descendants<ComboBox>((FrameworkElement)window.FindName("MemberEditor")).ToList();
                slots.Should().HaveCount(2);
                slots.ForEach(c => c.Text = "长剑");
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                content.UpdateLayout();
                var firstSlot = (TextBox)slots[0].Template.FindName("PART_EditableTextBox", slots[0]);
                Drop(firstSlot, LineupDragDrop.EquipmentFormat, "法杖", preview: true).Should().Be(DragDropEffects.Copy);
                editor.Members[0].Equipment1.Should().Be("法杖");
                editor.Members[0].Equipment2.Should().Be("长剑");
                var secondSlot = (TextBox)slots[1].Template.FindName("PART_EditableTextBox", slots[1]);
                Drop(secondSlot, LineupDragDrop.EquipmentFormat, "法杖", preview: true).Should().Be(DragDropEffects.Copy);
                editor.Members[0].Equipment2.Should().Be("法杖");
                slots[1].Text = "长剑";
                nextButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var description = (LineupDescriptionView)window.FindName("DescriptionWorkspace");
                Layout(content, 1020, 730);
                saveButton.Visibility.Should().Be(Visibility.Visible);
                var strip = (LineupMemberStrip)window.FindName("CurrentMemberStrip");
                strip.CanDragIcons.Should().BeTrue();
                Descendants<TextBlock>(strip).Select(text => text.Text).Should().Contain("能天使");
                persisted.Should().BeNull();
                var sourceEditor = (TextBox)description.FindName("SourceEditor");
                var introductionEditor = (TextBox)description.FindName("IntroductionEditor");
                var operatorPicker = (ComboBox)description.FindName("OperatorPicker");
                var equipmentPicker = (ComboBox)description.FindName("EquipmentPicker");
                operatorPicker.Items.Cast<Operator>().Select(item => item.Name).Should().Equal("能天使", "地灵");
                equipmentPicker.Items.Cast<string>().Should().Equal("长剑", "法杖", "护盾");
                var surface = (ScrollViewer)description.FindName("DiagramSurface");
                ((TextBlock)surface.Content).Text.Should().Contain("从上方拖动干员头像");
                Drop(surface, LineupDragDrop.OperatorFormat, "能天使", preview: true, position: new Point(220, 120))
                    .Should().Be(DragDropEffects.Copy);
                sourceEditor.Text.Should().Contain("N1[{{op:能天使}}]").And.NotContain("asp-pos");
                var firstNode = ((Canvas)surface.Content).Children.OfType<Border>().Single();
                Canvas.GetLeft(firstNode).Should().BeApproximately(220 - LineupDiagram.CardWidth / 2, 1);
                Canvas.GetTop(firstNode).Should().BeApproximately(120 - LineupDiagram.CardHeight / 2, 1);
                Layout(content, 1020, 730);
                Drop(surface, LineupDragDrop.EquipmentFormat, "法杖", preview: true, position: new Point(350, 200))
                    .Should().Be(DragDropEffects.Copy);
                sourceEditor.Text.Should().Contain("N2[{{gear:法杖}}]").And.NotContain("asp-pos");
                introductionEditor.Text.Should().BeEmpty();
                Drop(surface, LineupDragDrop.EquipmentFormat, "不存在", preview: true)
                    .Should().Be(DragDropEffects.None);
                introductionEditor.Text = "第一行\n第二行";
                introductionEditor.SelectAll();
                Descendants<Button>(description).Single(button => Equals(button.Tag, "bold"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                introductionEditor.Text.Replace("\r\n", "\n").Should().Be("**第一行**\n**第二行**");
                sourceEditor.Text.Should().Contain("N2[{{gear:法杖}}]");
                sourceEditor.Text = "flowchart TD\nCenter[{{op:能天使}}]";
                IconLoadingTests.PumpUntil(() => surface.Content is Canvas canvas &&
                    canvas.Children.OfType<Border>().Any(node => Equals(node.Tag, "Center")));
                Layout(content, 1020, 730);
                var viewport = (FrameworkElement)surface.Template.FindName("PART_ScrollContentPresenter", surface);
                Border CenterNode() => ((Canvas)surface.Content).Children.OfType<Border>()
                    .Single(node => Equals(node.Tag, "Center"));
                var cardCenter = new Point(LineupDiagram.CardWidth / 2, LineupDiagram.CardHeight / 2);
                var centered = CenterNode().TranslatePoint(cardCenter, viewport);
                centered.X.Should().BeApproximately(viewport.ActualWidth / 2, 1);
                centered.Y.Should().BeApproximately(viewport.ActualHeight / 2, 1);
                ((Canvas)surface.Content).Background.Should().BeSameAs(Brushes.Transparent);
                RenderPreview(content, "lineup-centered-single-node.png", 1020, 730);
                var zoom = (Slider)description.FindName("DiagramZoom");
                zoom.Value = 0.8;
                Layout(content, 1020, 730);
                surface.ScrollableWidth.Should().Be(0);
                surface.ScrollableHeight.Should().Be(0);
                centered = CenterNode().TranslatePoint(cardCenter, viewport);
                var unpannedSource = sourceEditor.Text;
                description.SetDiagramPan(new Vector(80, 28));
                var panned = CenterNode().TranslatePoint(cardCenter, viewport);
                panned.X.Should().BeApproximately(centered.X + 80, 1);
                panned.Y.Should().BeApproximately(centered.Y + 28, 1);
                sourceEditor.Text.Should().Be(unpannedSource);
                Drop(surface, LineupDragDrop.EquipmentFormat, "法杖", preview: true, position: new Point(120, 92))
                    .Should().Be(DragDropEffects.Copy);
                Layout(content, 1020, 730);
                var anchored = CenterNode().TranslatePoint(cardCenter, viewport);
                anchored.X.Should().BeApproximately(panned.X, 1);
                anchored.Y.Should().BeApproximately(panned.Y, 1);
                var newNode = ((Canvas)surface.Content).Children.OfType<Border>().Single(node => Equals(node.Tag, "N1"));
                var droppedAt = newNode.TranslatePoint(cardCenter, viewport);
                droppedAt.X.Should().BeApproximately(120, 1);
                droppedAt.Y.Should().BeApproximately(92, 1);
                ((Button)description.FindName("ResetDiagramViewButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var pan = (TranslateTransform)((Canvas)surface.Content).RenderTransform;
                pan.X.Should().Be(0);
                pan.Y.Should().Be(0);
                zoom.Value = 1;
                var (introduction, diagram) = LineupDiagramSource.SplitDescription(markdown);
                introductionEditor.Text = introduction;
                var modeButton = (Button)description.FindName("DiagramModeButton");
                modeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                ((FrameworkElement)description.FindName("DiagramSourceHost")).Visibility.Should().Be(Visibility.Visible);
                ((FrameworkElement)description.FindName("DiagramCanvasHost")).Visibility.Should().Be(Visibility.Collapsed);
                sourceEditor.Text = string.Join("\n", LineupDiagramSource.FindBlocks(diagram, out _).Single().Lines);
                introductionEditor.Text.Should().Be(introduction);
                modeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(content, 1020, 730);
                IconLoadingTests.PumpUntil(() => surface.Content is Canvas);
                IconLoadingTests.PumpUntil(() => ((Canvas)surface.Content).Children.OfType<Border>().Any(node => Equals(node.Tag, "A")));
                var initialGraph = (Canvas)surface.Content;
                initialGraph.Children.OfType<Border>().Should().HaveCount(2);
                void ClickGraphButton(string name) =>
                    ((Button)description.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Border FindNode(string id) => ((Canvas)surface.Content).Children.OfType<Border>().Single(node => Equals(node.Tag, id));
                void ClickNode(string id) => FindNode(id)
                    .RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.MouseLeftButtonDownEvent
                    });
                void ClickNodeMenu(string id, string label) => FindNode(id).ContextMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, label)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                MenuItem[] OpenNodeMenu(string buttonName)
                {
                    var button = (Button)description.FindName(buttonName);
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    var items = button.ContextMenu.Items.OfType<MenuItem>().ToArray();
                    button.ContextMenu.IsOpen = false;
                    return items;
                }
                OpenNodeMenu("AddOperatorNode").Select(item => item.Header).Should().Equal("能天使");
                var equipmentMenu = OpenNodeMenu("AddEquipmentNode");
                equipmentMenu.Select(item => item.Header).Should().Equal("法杖", "长剑");
                equipmentMenu.Single(item => Equals(item.Header, "法杖")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                sourceEditor.Text.Should().Contain("N1[{{gear:法杖}}]");
                ClickNode("B");
                ClickNodeMenu("N1", "从此节点创建连线");
                ClickNode("A");
                var graphText = (TextBox)description.FindName("GraphPropertyText");
                graphText.Text = "提供增益";
                ClickGraphButton("ApplyGraphPropertiesButton");
                sourceEditor.Text.Should().Contain("N1 -->|提供增益| A");
                ClickNode("N1");
                graphText.Text = "备用装备";
                ((CheckBox)description.FindName("HighlightNode")).IsChecked = true;
                ClickGraphButton("ApplyGraphPropertiesButton");
                sourceEditor.Text.Should().Contain("note N1: 备用装备").And.Contain("highlight N1").And.NotContain("# 输出顺序");
                ClickNodeMenu("B", "删除节点");
                sourceEditor.Text.Should().NotContain("B[{{gear:法杖}}]").And.NotContain("A -->|装备| B");
                introductionEditor.Text.Should().Be(introduction);
                var editedDiagram = sourceEditor.Text;
                var editedMarkdown = editor.Description;
                editedMarkdown.Should().StartWith(introduction).And.Contain(editedDiagram);
                var stripPortrait = Descendants<Image>(strip).First();
                DeferredIcon.GetPath(stripPortrait).Should().Be("icons/operators/能天使.png");
                IconLoadingTests.PumpUntil(() => stripPortrait.Source is not null);
                Layout(content, 800, 600);
                RenderPreview(content, "lineup-description-narrow.png", 800, 600);
                Layout(content, 1020, 730);
                RenderPreview(content, "lineup-description.png", 1020, 730);
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(content, 1020, 730);
                var saved = editor.BuildLineup();
                saved.Name.Should().Be("测试阵容");
                saved.Author.Should().Be("测试作者");
                saved.Members[0].IsDamageCore.Should().BeTrue();
                saved.Members[0].IsStackingCore.Should().BeTrue();
                saved.Description.Should().Be(editedMarkdown);
                saved.Members[0].Equipment1.Should().Be("法杖");
                saved.Members[0].Equipment2.Should().Be("长剑");
                window.ConfirmClose(() => false).Should().BeFalse("a rejected discard must block both editor and owner closure");
                window.ConfirmClose(() => true).Should().BeTrue();
                editor.IsDirty.Should().BeTrue("preflight must not discard drafts before all editors approve closure");
                ((TextBox)slots[0].Template.FindName("PART_EditableTextBox", slots[0])).Text.Should().Be("法杖");
                ((TextBox)slots[1].Template.FindName("PART_EditableTextBox", slots[1])).Text.Should().Be("长剑");
                editor.Status = "";
                RenderPreview(content, "lineup-editor.png", 1020, 730);
                nextButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                sourceEditor.Text.Should().Be(editedDiagram);
                saveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                persisted.Should().BeNull();
                window.IsComplete.Should().BeFalse("a failed save must leave the introduction open for retry");
                saveButton.Visibility.Should().Be(Visibility.Visible);
                editor.IsDirty.Should().BeTrue();
                allowSave = true;
                saveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                persisted.Should().BeEquivalentTo(saved);
                editor.IsDirty.Should().BeFalse();
                window.IsComplete.Should().BeTrue();
                ((Button)window.FindName("ShareButton")).Visibility.Should().Be(Visibility.Visible);
                var completed = (LineupPreviewView)window.FindName("CompletedPreview");
                Layout(content, 1020, 730);
                Descendants<TextBlock>(completed).Select(text => text.Text).Should().Contain(["测试阵容", "作者：测试作者", "能天使"]);
                ((FlowDocumentScrollViewer)completed.FindName("Introduction")).Document!.Blocks.Count.Should().Be(3);
                RenderPreview(content, "lineup-save-completed.png", 1020, 730);
                window.Close();

                var view = new LineupView { Width = 278, Height = 500 };
                var directory = Path.Combine(Path.GetTempPath(), "ASPA-LineupTests", Guid.NewGuid().ToString("N"));
                var store = new LineupStore(directory);
                var codec = new LineupShareService(["能天使"], ["长剑", "法杖"]);
                var importedDraft = codec.Import(codec.Export(saved));
                var importedEditor = new LineupEditorViewModel(editor.CatalogOperators,
                    [new() { Name = "长剑" }, new() { Name = "法杖" }], importedDraft) { Name = "导入后编辑" };
                store.SaveAsync([saved, importedEditor.BuildLineup()]).GetAwaiter().GetResult();
                var vm = new LineupLibraryViewModel(store);
                vm.LoadAsync().GetAwaiter().GetResult();
                File.Delete(Path.Combine(directory, "lineups.json"));
                Directory.Delete(directory);
                view.DataContext = vm;
                view.Measure(new Size(278, 500));
                view.Arrange(new Rect(0, 0, 278, 500));
                view.UpdateLayout();
                var categoryTabs = (ListBox)view.FindName("CategoryTabs");
                var cards = (ItemsControl)view.FindName("LineupCards");
                cards.Items.Count.Should().Be(2);
                categoryTabs.SelectedIndex = 1;
                cards.Items.Cast<Lineup>().Select(item => item.Name).Should().Equal("测试阵容");
                categoryTabs.SelectedIndex = 2;
                cards.Items.Cast<Lineup>().Select(item => item.Name).Should().Equal("导入后编辑");
                categoryTabs.SelectedIndex = 0;
                view.UpdateLayout();
                Lineup? trackRequested = null;
                view.TrackRequested += lineup => trackRequested = lineup;
                Descendants<Button>(view).First(button => Equals(button.Content, "追踪阵容"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                trackRequested.Should().BeEquivalentTo(saved);
                RenderPreview(view, "lineup-library.png", 278, 500);
                var fired = false;
                view.NewRequested += () => fired = true;
                ((Button)view.FindName("NewButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                fired.Should().BeTrue();
                foreach (var button in Descendants<Button>(view).Where(b => b.IsVisible))
                {
                    var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                    bounds.Left.Should().BeGreaterThanOrEqualTo(0);
                    bounds.Right.Should().BeLessThanOrEqualTo(278);
                }
                view.ShareService = codec;
                Lineup? imported = null;
                view.ImportPreviewRequested += lineup => imported = lineup;
                view.ImportText(codec.Export(saved));
                imported.Should().BeEquivalentTo(saved with { IsImported = true }, o => o.Excluding(x => x.Id));
                imported!.Id.Should().NotBe(saved.Id);
                vm.Lineups.Should().HaveCount(2, "import is a draft until saved");
                imported = null;
                view.ImportText("broken");
                imported.Should().BeNull();
                vm.Status.Should().NotBeEmpty();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderPreview(FrameworkElement element, string filename, int width, int height)
    {
        var directory = Environment.GetEnvironmentVariable("ASPA_LINEUP_PREVIEW");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(26, 26, 46)), null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, filename));
        encoder.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
