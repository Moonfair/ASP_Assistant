using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace ASPAssistant.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MacApp>()
        .UseAvaloniaNative()
        .UseSkia()
        .LogToTrace();
}

public sealed class MacApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
