using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ASPAssistant.Mac;

internal sealed class LineupSiteAccountWindow : Window
{
    private const string PngPrefix = "data:image/png;base64,";
    private readonly LineupSiteClient _site;
    private readonly CancellationTokenSource _closed = new();
    private readonly StackPanel _body = new() { Spacing = 10, Margin = new Thickness(18) };
    private readonly TextBlock _status = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private string? _challengeId;
    private string? _mailId;
    private string? _email;

    public LineupSiteAccountWindow(LineupSiteClient site)
    {
        _site = site;
        Title = "卫戍阵容库账号";
        Width = 420;
        Height = 450;
        MinWidth = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Content = _body };
        Closed += (_, _) => _closed.Cancel();
        Opened += async (_, _) =>
        {
            if (_site.Email is { } email) ShowSignedIn(email);
            else await ShowEmailStageAsync();
        };
    }

    private async Task ShowEmailStageAsync()
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = "登录卫戍阵容库", FontSize = 18, FontWeight = FontWeight.Bold });
        var email = new TextBox { Watermark = "邮箱", Text = _email };
        var image = new Image { Height = 96, HorizontalAlignment = HorizontalAlignment.Left };
        var answer = new TextBox { Watermark = "5 位图片验证码", MaxLength = 5 };
        var refresh = new Button { Content = "换一张验证码" };
        var send = new Button { Content = "发送邮件验证码", IsEnabled = false };
        _body.Children.Add(email);
        _body.Children.Add(image);
        _body.Children.Add(refresh);
        _body.Children.Add(answer);
        _body.Children.Add(send);
        _body.Children.Add(_status);
        refresh.Click += async (_, _) => await LoadChallengeAsync();
        send.Click += async (_, _) =>
        {
            var address = email.Text?.Trim() ?? "";
            if (address.Length == 0 || (answer.Text?.Trim().Length ?? 0) != 5 || _challengeId is null)
            { _status.Text = "请输入邮箱和 5 位图片验证码。"; return; }
            await RunAsync(async () =>
            {
                _mailId = await _site.SendCodeAsync(address, _challengeId, answer.Text!.Trim(), _closed.Token);
                _email = address;
                ShowCodeStage();
            });
        };
        await LoadChallengeAsync();

        async Task LoadChallengeAsync()
        {
            _challengeId = null;
            send.IsEnabled = false;
            image.Source = null;
            answer.Text = "";
            await RunAsync(async () =>
            {
                var challenge = await _site.GetChallengeAsync(_closed.Token);
                if (!challenge.Image.StartsWith(PngPrefix, StringComparison.Ordinal))
                    throw new LineupSiteException("图片验证码格式无效。");
                using var stream = new MemoryStream(Convert.FromBase64String(challenge.Image[PngPrefix.Length..]));
                image.Source = new Bitmap(stream);
                _challengeId = challenge.Id;
                send.IsEnabled = true;
            });
        }
    }

    private void ShowCodeStage()
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = $"验证码已发送至 {_email}，请在 10 分钟内输入。", TextWrapping = TextWrapping.Wrap });
        var code = new TextBox { Watermark = "6 位邮件验证码", MaxLength = 6 };
        var verify = new Button { Content = "登录" };
        var back = new Button { Content = "返回" };
        _body.Children.Add(code);
        _body.Children.Add(verify);
        _body.Children.Add(back);
        _body.Children.Add(_status);
        verify.Click += async (_, _) =>
        {
            var value = code.Text?.Trim() ?? "";
            if (value.Length != 6 || !value.All(char.IsAsciiDigit))
            { _status.Text = "请输入 6 位数字验证码。"; return; }
            await RunAsync(async () =>
            {
                await _site.SignInAsync(_mailId!, value, _closed.Token);
                Close(true);
            });
        };
        back.Click += async (_, _) => await ShowEmailStageAsync();
    }

    private void ShowSignedIn(string email)
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = "已登录 " + email, FontSize = 17, TextWrapping = TextWrapping.Wrap });
        var signOut = new Button { Content = "退出登录" };
        signOut.Click += async (_, _) => await RunAsync(async () =>
        {
            await _site.SignOutAsync(_closed.Token);
            Close(true);
        });
        _body.Children.Add(signOut);
        _body.Children.Add(_status);
    }

    private async Task RunAsync(Func<Task> action)
    {
        _body.IsEnabled = false;
        _status.Text = "";
        try { await action(); }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _body.IsEnabled = true; }
    }
}
