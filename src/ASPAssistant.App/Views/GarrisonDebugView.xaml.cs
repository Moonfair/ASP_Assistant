using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.GameModes.GarrisonProtocol;

namespace ASPAssistant.App.Views;

public partial class GarrisonDebugView : UserControl
{
    private const string Unknown = "尚未读取";
    private BattleDataView? _feed;
    private BattleUpdate _latest = new(BattleFeedState.WaitingForClient);
    private IReadOnlyDictionary<string, Dictionary<string, string>> _catalog = new Dictionary<string, Dictionary<string, string>>();
    private IReadOnlyDictionary<string, string> _operators = new Dictionary<string, string>();

    public GarrisonDebugView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            Present(_latest);
        };
    }

    internal void Attach(BattleDataView feed)
    {
        _feed = feed;
        feed.Updated += update =>
        {
            _latest = update;
            if (IsVisible) Present(update);
        };
    }

    internal void SetNames(IReadOnlyDictionary<string, Dictionary<string, string>> catalog,
        IReadOnlyDictionary<string, string> operators)
    {
        (_catalog, _operators) = (catalog, operators);
        if (IsVisible) Present(_latest);
    }

    private void OnConnect(object sender, RoutedEventArgs e) => _feed?.Reconnect();

    internal void Present(BattleUpdate update)
    {
        var data = update.Garrison;
        FeedStatus.Text = update switch
        {
            { Source: GameTargetKind.MuMu } => "MuMu 仅采集战斗伤害；回合、商店等级和金币仍来自画面识别。",
            { GarrisonInterrupted: true } => "正在重新同步卫戍协议数据…",
            { Garrison: not null } => "已读取卫戍协议内存",
            { State: BattleFeedState.PermissionRequired } => "Windows 尚未授予游戏内存读取权限。",
            { State: BattleFeedState.UnsupportedBuild } => "当前客户端版本尚未适配。支持 Windows 2.7.91。",
            { State: BattleFeedState.WindowsOnly } => "当前来源的回合、商店等级和金币来自画面识别。",
            { State: BattleFeedState.Connecting } => "正在连接官方 PC 客户端…",
            { State: BattleFeedState.ConnectionFailed } => "内存连接已中断。",
            { State: BattleFeedState.WaitingForClient } => "等待官方 PC 客户端",
            _ => "已连接，等待卫戍协议对局。"
        };
        ConnectButton.Visibility = update.Source != GameTargetKind.MuMu && update.State is (BattleFeedState.PermissionRequired or BattleFeedState.ConnectionFailed)
            ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Content = update.State == BattleFeedState.PermissionRequired ? "授权读取" : "重新连接";
        var memory = GarrisonHud.FromMemory(data);
        RoundValue.Text = Value(memory.Round);
        LevelValue.Text = Value(memory.ShopLevel);
        CoinValue.Text = Value(memory.Gold);
        PlayerDetails.Text = data == null ? "" : $"本方玩家 #{data.PlayerIndex}　生命 {Value(data.Health)}\n" +
            $"玩家记录　商店 {Value(data.PlayerLevel)} 级　金币 {Value(data.PlayerCoins)}";
        Teammates.Text = Lines(data?.Teammates, "当前未发现队友", t => FormatTeammate(t, data?.ActivityId));
        var offers = data?.Shop;
        Shop.Text = (offers == null ? "" : $"升级 {offers.UpgradeCost}　刷新 {offers.RefreshCost}　免费刷新 {offers.FreeRefreshes}\n")
            + Lines(offers?.Offers, "没有商品", o => $"槽 {o.Slot}　{(o.ChessId == "" ? "空位" : Name(o.ChessId))}" +
                (o.ChessId == "" ? "" : $"　{o.Price} 金币{(o.Frozen ? " · 已冻结" : "")}"));
        Characters.Text = Lines(data?.Characters, "没有持有干员", c =>
        {
            var slots = Slots(c.EquipmentSlots);
            var pos = data?.Positions?.FirstOrDefault(p => p.InstanceId == c.InstanceId);
            return $"#{c.InstanceId}　{Name(c.ChessId)}\n{Location(pos)}　售价 {c.SellPrice}　装备槽 {slots}";
        });
        Equipment.Text = Lines(data?.Equipment, "没有持有装备", item =>
        {
            var owner = data?.Characters?.FirstOrDefault(c => c.EquipmentSlots?.Contains(item.InstanceId) == true);
            var worn = owner != null ? $"穿戴于 #{owner.InstanceId} {Label(owner.ChessId)}" :
                data?.Characters?.All(c => c.EquipmentSlots != null) == true ? "未穿戴" : "穿戴关系未知";
            return $"#{item.InstanceId}　{Name(item.ChessId)}\n{worn}";
        });
        Magic.Text = Lines(data?.Magic, "没有持有道具", m => $"#{m.InstanceId}　{Name(m.ChessId)}");
        Positions.Text = Lines(data?.Positions, "没有位置记录",
            p => $"#{p.InstanceId}　{Location(p)}　位置码 {p.Position}　类型 {p.Kind}　朝向 {p.Direction}");
        Clock.Text = update.Sample is { } frame
            ? $"帧 {frame.Frame}　游戏时间 {frame.GameSeconds:0.0} 秒{(frame.Ended ? " · 已结束" : "")}" : Unknown;

        string Label(string id) => ChessName(id, data?.ActivityId, data?.SelectedCharacters);
        string Name(string id)
        {
            var name = Label(id);
            return name == id ? id : $"{name} · {id}";
        }
    }

    private string ChessName(string id, string? activity, Dictionary<string, string>? selected)
    {
        if (selected != null)
        {
            if (selected.TryGetValue(id, out var character)) return _operators.GetValueOrDefault(character, character);
            if (id.EndsWith("_b", StringComparison.Ordinal) && selected.TryGetValue(id[..^1] + "a", out character))
                return _operators.GetValueOrDefault(character, character) + "（精锐）";
        }
        return activity != null && _catalog.TryGetValue(activity, out var season) ? season.GetValueOrDefault(id, id) : id;
    }

    private string FormatTeammate(GarrisonTeammate player, string? activity)
    {
        var coins = player.RawCoins switch { null => "未知", 0 => "未知（原始值 0）", _ => $"{player.RawCoins}（原始值）" };
        var rows = new List<string> {
            $"队友 #{player.PlayerIndex}　生命 {Value(player.Health)}　连胜 {Value(player.Streak)}",
            $"商店等级 {Value(player.ShopLevel)}　金币 {coins}",
            $"对局状态码 {Value(player.GameState)}　连接状态码 {Value(player.ConnectionState)}"
        };
        AppendRoster("玩家记录", player.Roster, true);
        AppendRoster("队伍概览（缓存）", player.Overview, false);
        return string.Join("\n", rows);

        void AppendRoster(string source, GarrisonTeamRoster? roster, bool showPositions)
        {
            rows.Add(source);
            rows.Add("干员　" + Lines(roster?.Characters, "无", c =>
                $"#{c.InstanceId} {Name(c.ChessId)}　装备槽 {Slots(c.EquipmentSlots)}"));
            rows.Add("装备　" + Lines(roster?.Equipment, "无", item =>
            {
                var owner = roster?.Characters?.FirstOrDefault(c => c.EquipmentSlots?.Contains(item.InstanceId) == true);
                var worn = owner != null ? $"穿戴于 #{owner.InstanceId}" :
                    roster?.Characters?.All(c => c.EquipmentSlots != null) == true ? "未穿戴" : "穿戴关系未知";
                return $"#{item.InstanceId} {Name(item.ChessId)} · {worn}";
            }));
            if (showPositions) rows.Add("站位　" + Lines(roster?.Positions, "无",
                p => $"#{p.InstanceId} {Location(p)}　位置码 {p.Position}　朝向 {p.Direction}"));
            rows.Add("盟约　" + Lines(roster?.Bonds, "无", b =>
                $"{b.Id} · {(b.Active ? "已激活" : "未激活")}　{b.Characters} 人　常驻 {b.PersistentStacks} / 临时 {b.TemporaryStacks} 层"));
        }

        string Name(string id) => ChessName(id, activity, player.SelectedCharacters);
    }

    private static string Slots(int[]? slots) => slots == null ? "未知" : slots.Length == 0 ? "无" : string.Join(" / ", slots);
    private static string Value(int? value) => value?.ToString() ?? "—";
    // AutoChessMapAreaManager.TryConvertPosition uses nonpositive hand indices and packed board coordinates.
    private static string Location(GarrisonPosition? pos) => pos switch
    {
        null => "位置未知",
        { Position: > 0 } => $"场上（行 {pos.Position >> 16}，列 {pos.Position & 0xffff}）",
        _ => $"备战区（索引 {-(long)pos.Position}）"
    };
    private static string Lines<T>(T[]? rows, string empty, Func<T, string> format) =>
        rows == null ? Unknown : rows.Length == 0 ? empty : string.Join("\n", rows.Select(format));
}
