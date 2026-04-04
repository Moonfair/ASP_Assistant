using System.Runtime.InteropServices;

namespace ASPAssistant.App;

public static class Program
{
    // Use Win32 MessageBox directly — PresentationFramework is not loaded yet at this point.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;

    [STAThread]
    public static void Main()
    {
        // PresentationFramework (WPF) requires Windows 10 build 17763 (version 1809) or later.
        // Check BEFORE loading any WPF assembly so a failed load doesn't silently terminate the process.
        // OperatingSystem.IsWindowsVersionAtLeast reads from ntdll RtlGetVersion — reflects the real OS version.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var v = Environment.OSVersion.Version;
            MessageBoxW(
                nint.Zero,
                $"ASPAssistant 需要 Windows 10 1809（版本 17763）或更高版本才能运行。\n\n" +
                $"当前系统版本：{v.Major}.{v.Minor}.{v.Build}\n\n" +
                $"请将 Windows 升级至 1809 或更高版本后重试。",
                "系统版本不兼容 — ASPAssistant",
                MB_OK | MB_ICONERROR);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
