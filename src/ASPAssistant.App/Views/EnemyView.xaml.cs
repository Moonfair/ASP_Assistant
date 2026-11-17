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
    private readonly RoundRoute[] _rounds = [new(14, "最终领袖"), new(15, "隐秘核心（若触发）")];
    private PreparationEnemyInfo? _info;

    public EnemyView()
    {
        InitializeComponent();
        Routes.ItemsSource = _rounds;
        UpdateBattlefieldChoices();
    }

    public void UpdateInfo(PreparationEnemyInfo? info)
    {
        var previous = _info;
        _info = info;
        EmptyMessage.Visibility = info == null ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = info == null ? Visibility.Collapsed : Visibility.Visible;
        if (info == null)
        {
            foreach (var round in _rounds) round.Side = 0;
            BattlefieldPicker.SelectedIndex = 0;
        }
        BossName.Text = info?.Boss ?? "待识别";
        EnemyTypes.Text = info is { EnemyTypes.Count: > 0 }
            ? string.Join("、", info.EnemyTypes) + (info.EnemyTypes.Count < 3 ? "（识别中）" : "")
            : "待识别";
        if (previous?.Battlefield != info?.Battlefield) UpdateBattlefieldChoices();
        RefreshRoutes();
    }

    private void UpdateBattlefieldChoices()
    {
        int selection = Math.Max(0, BattlefieldPicker.SelectedIndex);
        var label = _info?.Battlefield is int map ? $"自动识别 · #{map:D2}" : "自动识别 · 待识别";
        BattlefieldPicker.ItemsSource = new BattlefieldChoice[] { new(null, label) }
            .Concat(BossRoutes.Battlefields.Select(number => new BattlefieldChoice(number, $"#{number:D2}")));
        BattlefieldPicker.SelectedIndex = selection;
    }

    private void OnBattlefieldChanged(object sender, SelectionChangedEventArgs e) => RefreshRoutes();

    private void RefreshRoutes()
    {
        int? battlefield = (BattlefieldPicker.SelectedItem as BattlefieldChoice)?.Number ?? _info?.Battlefield;
        foreach (var round in _rounds) round.Update(_info, battlefield);
    }

    private sealed record BattlefieldChoice(int? Number, string Label);

    private sealed class RoundRoute(int round, string name) : ObservableObject
    {
        private int _side;
        private BossRoute? _route;
        private string? _imageFile;
        private string _status = "";

        public string Title => $"第 {round} 回合 · {name}";
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

        public void Update(PreparationEnemyInfo? info, int? battlefield)
        {
            _route = battlefield is int map ? BossRoutes.For(info?.Boss, info?.Mode, map, round) : null;
            Status = (info?.Boss, battlefield) switch
            {
                _ when round == 14 && info?.Mode == "极速模拟" => "极速模拟在第 9 回合迎战领袖",
                _ when round == 15 && !BossRoutes.HasHiddenRound(info?.Mode) => "当前难度没有隐秘核心回合",
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
