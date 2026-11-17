using System.Diagnostics;

namespace ASPAssistant.Mac;

internal static class MacPassportGrant
{
    public static async Task<string?> RequestAsync(CancellationToken cancellationToken)
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "aspa-passport-login");
        if (!File.Exists(helper)) throw new FileNotFoundException("缺少鹰角登录组件", helper);
        using var process = Process.Start(new ProcessStartInfo(helper)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("无法打开鹰角登录窗口");
        try
        {
            var output = await process.StandardOutput.ReadLineAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output is { Length: > 6 } && output.StartsWith("grant:", StringComparison.Ordinal)
                ? output[6..] : null;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
