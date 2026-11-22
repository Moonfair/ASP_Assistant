using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.App.Views;

public partial class EnemyView : UserControl
{
    // https://prts.wiki/w/卫戍协议·规程/PRTS盟约记录#领袖
    private static readonly string[] BossNames =
    [
        "假想敌：胄", "假想敌：铳", "假想敌：管", "盐风主教昆图斯", "阿利斯泰尔，帝国余晖",
        "“萨米的意志”", "假想敌：械", "假想敌：茧", "假想敌：鳍", "矩兽"
    ];
    private readonly RoundRoute[] _rounds = [new(14, "最终领袖"), new(15, "隐秘核心（若触发）")];
    private PreparationEnemyInfo? _info;

    public EnemyView()
    {
        InitializeComponent();
        ShowSelectedRound();
        UpdateInfo(null);
    }

    public void UpdateInfo(PreparationEnemyInfo? info)
    {
        var previous = _info;
        _info = info;
        if (info == null)
        {
            RoundPicker.SelectedIndex = 0;
            foreach (var round in _rounds) round.Side = 0;
            BossPicker.SelectedIndex = 0;
            BattlefieldPicker.SelectedIndex = 0;
        }
        EnemyTypes.Text = info is { EnemyTypes.Count: > 0 }
            ? string.Join("、", info.EnemyTypes) + (info.EnemyTypes.Count < 3 ? "（识别中）" : "")
            : "待识别";
        if (previous?.Boss != info?.Boss || BossPicker.ItemsSource == null) UpdateBossChoices();
        if (previous?.Battlefield != info?.Battlefield || BattlefieldPicker.ItemsSource == null) UpdateBattlefieldChoices();
        RefreshRoutes();
    }

    private void UpdateBossChoices()
    {
        int selection = Math.Max(0, BossPicker.SelectedIndex);
        var label = $"自动识别 · {_info?.Boss ?? "待识别"}";
        BossPicker.ItemsSource = new BossChoice[] { new(null, label) }
            .Concat(BossNames.Select(name => new BossChoice(name, name)));
        BossPicker.SelectedIndex = selection;
    }

    private void UpdateBattlefieldChoices()
    {
        int selection = Math.Max(0, BattlefieldPicker.SelectedIndex);
        var label = _info?.Battlefield is int map ? $"自动识别 · #{map:D2}" : "自动识别 · 待识别";
        BattlefieldPicker.ItemsSource = new BattlefieldChoice[] { new(null, label) }
            .Concat(BossRoutes.Battlefields.Select(number => new BattlefieldChoice(number, $"#{number:D2}")));
        BattlefieldPicker.SelectedIndex = selection;
    }

    private void OnRoundChanged(object sender, SelectionChangedEventArgs e) => ShowSelectedRound();

    private void ShowSelectedRound()
    {
        // SelectedIndex can be set while the XAML namescope is still being constructed.
        if (Routes is not null && RoundPicker.SelectedIndex is 0 or 1)
            Routes.ItemsSource = new[] { _rounds[RoundPicker.SelectedIndex] };
    }

    private void OnBattlefieldChanged(object sender, SelectionChangedEventArgs e) => RefreshRoutes();
    private void OnBossChanged(object sender, SelectionChangedEventArgs e) => RefreshRoutes();

    private void RefreshRoutes()
    {
        string? boss = (BossPicker.SelectedItem as BossChoice)?.Name ?? _info?.Boss;
        int? battlefield = (BattlefieldPicker.SelectedItem as BattlefieldChoice)?.Number ?? _info?.Battlefield;
        EmptyMessage.Visibility = _info == null && (boss == null || battlefield == null) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var round in _rounds) round.Update(boss, _info?.Mode, battlefield);
    }

    private sealed record BossChoice(string? Name, string Label);
    private sealed record BattlefieldChoice(int? Number, string Label);

    private sealed class RoundRoute(int round, string name) : ObservableObject
    {
        private int _side;
        private BossRoute? _route;
        private string? _imageFile;
        private string _status = "";

        public string Title => name;
        public string SidePickerName => $"第 {round} 回合站位";
        public IReadOnlyList<string> SideChoices { get; } = ["站位待确认 · 看两侧", "我在左侧", "我在右侧"];
        public int Side
        {
            get => _side;
            set
            {
                if (!SetProperty(ref _side, value)) return;
                OnPropertyChanged(nameof(LeftVisibility));
                OnPropertyChanged(nameof(RightVisibility));
            }
        }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public Visibility RouteVisibility => LeftImage == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility StatusVisibility => LeftImage == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LeftVisibility => Side == 2 ? Visibility.Collapsed : Visibility.Visible;
        public Visibility RightVisibility => Side == 1 ? Visibility.Collapsed : Visibility.Visible;
        public BitmapSource? LeftImage { get; private set; }
        public BitmapSource? RightImage { get; private set; }
        public string? LeftOrder => _route?.Order(left: true);
        public string? RightOrder => _route?.Order(left: false);

        public void Update(string? boss, string? mode, int? battlefield)
        {
            _route = battlefield is int map ? BossRoutes.For(boss, mode, map, round) : null;
            Status = (boss, battlefield) switch
            {
                _ when round == 14 && mode == "极速模拟" => "极速模拟在第 9 回合迎战领袖",
                _ when round == 15 && !BossRoutes.HasHiddenRound(mode) => "当前难度没有隐秘核心回合",
                ("矩兽", _) when round == 15 => "矩兽没有隐秘核心形态",
                (null, _) => "等待识别敌方领袖",
                (_, null) => "请选择战场编号",
                _ when _route == null => "当前领袖暂无参考路线",
                _ => "路线图文件不可用"
            };
            if (_imageFile == _route?.ImageFile) return;
            _imageFile = _route?.ImageFile;
            LeftImage = RightImage = null;
            if (_imageFile != null)
            {
                string path = Path.Combine(AppContext.BaseDirectory, "data", "boss-routes", _imageFile);
                try
                {
                    var image = new BitmapImage(new Uri(path));
                    int halfWidth = image.PixelWidth / 2;
                    LeftImage = new CroppedBitmap(image, new Int32Rect(0, 0, halfWidth, image.PixelHeight));
                    RightImage = new CroppedBitmap(image, new Int32Rect(halfWidth, 0, halfWidth, image.PixelHeight));
                }
                catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException)
                {
                    LeftImage = RightImage = null;
                }
            }
            OnPropertyChanged(nameof(LeftImage));
            OnPropertyChanged(nameof(RightImage));
            OnPropertyChanged(nameof(LeftOrder));
            OnPropertyChanged(nameof(RightOrder));
            OnPropertyChanged(nameof(RouteVisibility));
            OnPropertyChanged(nameof(StatusVisibility));
        }
    }
}
