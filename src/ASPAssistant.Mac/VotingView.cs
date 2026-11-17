using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Mac;

internal static class VotingView
{
    public static Control Create(Window owner, Operator op, MacWorkspace workspace)
    {
        if (workspace.CatalogDetails.Season is not { Length: > 0 } season)
            return new TextBlock { Text = "当前数据没有投票赛季。" };
        var site = workspace.Site;
        var skills = workspace.CatalogDetails.Skills.GetValueOrDefault(op.Name)?
            .OrderBy(skill => skill.Slot).ToArray() ?? [];
        var root = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        root.Children.Add(new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(0, 1, 0, 0) });
        root.Children.Add(new TextBlock { Text = "技能与装备投票", FontSize = 16, FontWeight = FontWeight.Bold });
        var results = new TextBlock { Text = "正在读取公开票数…", TextWrapping = TextWrapping.Wrap };
        root.Children.Add(results);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = MacPalette.Secondary };
        root.Children.Add(status);
        var account = new Button { Content = "登录后投票" };
        root.Children.Add(account);
        var skillPicker = new ComboBox
        {
            ItemsSource = skills.Select(skill => $"{skill.Slot} · {skill.Name}").ToArray(),
            SelectedIndex = skills.Length > 0 ? 0 : -1,
            MinWidth = 250
        };
        root.Children.Add(skillPicker);
        var skillVote = new Button { Content = "提交技能票", IsEnabled = false };
        root.Children.Add(skillVote);
        root.Children.Add(new TextBlock { Text = "选择适用装备（最多 5 件）" });
        var equipmentSearch = new TextBox { Watermark = "搜索装备" };
        root.Children.Add(equipmentSearch);
        var equipment = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 160 };
        var selectedNames = new HashSet<string>(StringComparer.Ordinal);
        root.Children.Add(equipment);
        var equipmentVote = new Button { Content = "提交装备票", IsEnabled = false };
        root.Children.Add(equipmentVote);
        LineupSiteClient.OperatorVoteStatus? voteStatus = null;
        bool busy = false;
        bool refreshing = false;

        void RefreshEquipment()
        {
            refreshing = true;
            var query = equipmentSearch.Text?.Trim() ?? "";
            equipment.ItemsSource = workspace.Equipment.Select(item => item.Name)
                .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name).ToArray();
            equipment.SelectedItems?.Clear();
            foreach (var name in selectedNames.Where(name =>
                         name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                equipment.SelectedItems?.Add(name);
            refreshing = false;
        }
        void RefreshControls()
        {
            var signedIn = site.Email != null;
            account.Content = signedIn ? "账号：" + site.Email : "登录后投票";
            skillVote.IsEnabled = !busy && signedIn && voteStatus is { SkillVoted: false } && skills.Length > 0;
            equipmentVote.IsEnabled = !busy && signedIn && voteStatus is { EquipmentVoted: false } &&
                selectedNames.Count is > 0 and <= 5;
            equipmentVote.Content = $"提交装备票（{selectedNames.Count}/5）";
        }
        equipmentSearch.TextChanged += (_, _) => RefreshEquipment();
        equipment.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            var visible = ((IEnumerable<string>?)equipment.ItemsSource)?.ToArray() ?? [];
            selectedNames.ExceptWith(visible);
            selectedNames.UnionWith(equipment.SelectedItems?.OfType<string>() ?? []);
            if (selectedNames.Count > 5)
            {
                status.Text = "最多选择 5 件装备。";
                selectedNames.Remove(equipment.SelectedItems?.OfType<string>().LastOrDefault() ?? "");
                RefreshEquipment();
            }
            RefreshControls();
        };
        RefreshEquipment();
        account.Click += async (_, _) =>
        {
            await new LineupSiteAccountWindow(site).ShowDialog<bool?>(owner);
            await LoadVoteStatusAsync();
        };
        skillVote.Click += async (_, _) =>
        {
            if (skillPicker.SelectedIndex < 0 || voteStatus is not { SkillVoted: false }) return;
            busy = true; RefreshControls();
            try
            {
                await site.VoteForSkillAsync(season, op.Name, skills[skillPicker.SelectedIndex].Slot,
                    CancellationToken.None);
                status.Text = "技能投票已提交。";
                voteStatus = voteStatus with { SkillVoted = true };
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; RefreshControls(); }
        };
        equipmentVote.Click += async (_, _) =>
        {
            if (voteStatus is not { EquipmentVoted: false } || selectedNames.Count is < 1 or > 5) return;
            busy = true; RefreshControls();
            try
            {
                await site.VoteForEquipmentAsync(season, op.Name, selectedNames.Order().ToArray(),
                    CancellationToken.None);
                status.Text = "装备投票已提交。";
                voteStatus = voteStatus with { EquipmentVoted = true };
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; RefreshControls(); }
        };
        var loaded = false;
        root.Loaded += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            try
            {
                var votes = await site.GetOperatorVotesAsync(season, op.Name);
                results.Text = "技能：" + string.Join(" · ", skills.Select(skill =>
                    $"{skill.Name} {votes.Skills.FirstOrDefault(row => row.Choice == skill.Slot)?.Votes ?? 0} 票")) +
                    "\n装备：" + string.Join(" · ", votes.Equipment.Take(5).Select(row => $"{row.Choice} {row.Votes} 票"));
            }
            catch (Exception ex) { results.Text = "公开票数读取失败：" + ex.Message; }
            await LoadVoteStatusAsync();
        };
        RefreshControls();
        return root;

        async Task LoadVoteStatusAsync()
        {
            voteStatus = null;
            if (site.Email != null)
            {
                try { voteStatus = await site.GetOperatorVoteStatusAsync(season, op.Name); }
                catch (Exception ex) { status.Text = ex.Message; }
            }
            RefreshControls();
        }
    }
}
