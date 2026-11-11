using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ASPAssistant.App.Services;

namespace ASPAssistant.App.Windows;

public partial class LineupSiteAccountWindow : Window
{
    private const string PngPrefix = "data:image/png;base64,";
    private readonly LineupSiteClient _site;
    private readonly CancellationTokenSource _closed = new();
    private string? _challengeId;
    private string? _mailId;

    internal LineupSiteAccountWindow(LineupSiteClient site)
    {
        _site = site;
        InitializeComponent();
        if (_site.Email is { } email)
        {
            EmailStage.Visibility = Visibility.Collapsed;
            AccountStage.Visibility = Visibility.Visible;
            AccountEmail.Text = email;
        }
        else
        {
            Loaded += async (_, _) =>
            {
                Email.Focus();
                await RunAsync(EmailStage, LoadChallengeAsync);
            };
        }
    }

    private async Task LoadChallengeAsync()
    {
        _challengeId = null;
        Captcha.Source = null;
        ImageAnswer.Clear();
        SendCodeButton.IsEnabled = false;
        var challenge = await _site.GetChallengeAsync(_closed.Token);
        _closed.Token.ThrowIfCancellationRequested();
        if (challenge.Image is not { } image || !image.StartsWith(PngPrefix, StringComparison.Ordinal))
            throw new LineupSiteException("无法显示图片验证码，请换一张重试。");

        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(image[PngPrefix.Length..]));
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            Captcha.Source = bitmap;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            throw new LineupSiteException("无法显示图片验证码，请换一张重试。");
        }

        _challengeId = challenge.Id;
        SendCodeButton.IsEnabled = true;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RunAsync(EmailStage, LoadChallengeAsync);

    private async void OnSendCode(object sender, RoutedEventArgs e)
    {
        if (_challengeId is not { } id) return;
        var email = Email.Text.Trim();
        var answer = ImageAnswer.Text.Trim();
        if (email.Length == 0 || answer.Length != 5)
        {
            Status.Text = "请输入邮箱和 5 位图片验证码。";
            return;
        }

        await RunAsync(EmailStage, async () =>
        {
            try { _mailId = await _site.SendCodeAsync(email, id, answer, _closed.Token); }
            catch (LineupSiteException ex)
            {
                await LoadChallengeAsync();
                Status.Text = ex.Message;
                ImageAnswer.Focus();
                return;
            }

            _closed.Token.ThrowIfCancellationRequested();
            DeliveryNotice.Text = $"验证码已发送至 {email}，请在 10 分钟内输入。";
            EmailStage.Visibility = Visibility.Collapsed;
            VerifyStage.Visibility = Visibility.Visible;
            EmailCode.Clear();
            EmailCode.Focus();
        });
    }

    private async void OnVerify(object sender, RoutedEventArgs e)
    {
        if (_mailId is not { } id) return;
        var code = EmailCode.Text.Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            Status.Text = "请输入 6 位数字验证码。";
            return;
        }
        await RunAsync(VerifyStage, async () =>
        {
            await _site.SignInAsync(id, code, _closed.Token);
            if (!_closed.IsCancellationRequested) DialogResult = true;
        });
    }

    private async void OnBack(object sender, RoutedEventArgs e)
    {
        _mailId = null;
        VerifyStage.Visibility = Visibility.Collapsed;
        EmailStage.Visibility = Visibility.Visible;
        await RunAsync(EmailStage, LoadChallengeAsync);
    }

    private async void OnSignOut(object sender, RoutedEventArgs e) => await RunAsync(AccountStage, async () =>
    {
        await _site.SignOutAsync(_closed.Token);
        if (!_closed.IsCancellationRequested) DialogResult = true;
    });

    private async Task RunAsync(Panel stage, Func<Task> operation)
    {
        stage.IsEnabled = false;
        Status.Text = "";
        try { await operation(); }
        catch (LineupSiteException ex) { Status.Text = ex.Message; }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        finally { stage.IsEnabled = true; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed.Cancel();
        base.OnClosed(e);
    }
}
