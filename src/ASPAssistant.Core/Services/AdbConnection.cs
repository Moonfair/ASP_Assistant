using System.Net;
using System.Text;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

internal static class AdbConnection
{
    internal static string? GetLocalEndpoint(GameTarget target)
    {
        var serial = target.Kind == GameTargetKind.LdPlayer ? target.AlternateSerial : target.Serial;
        if (serial == null || !GameTargetSelection.IsLocalAdbSerial(serial)) return null;
        if (target.Kind != GameTargetKind.LdPlayer) return serial;

        if (!IPEndPoint.TryParse(serial, out var endpoint) ||
            LocalTcpListener.FindOwner(endpoint.Port) is not { } owner)
            return null;
        User32.GetWindowThreadProcessId(target.Window, out var windowProcess);
        return owner == windowProcess || owner == target.VmPid ? serial : null;
    }

    internal static async Task<string?> ConnectAsync(string adb, string serial, CancellationToken token)
    {
        var devices = await LocalCommand.RunAsync(adb, ["devices"], token).ConfigureAwait(false);
        if (IsOnline(devices, serial)) return serial;

        if (serial.StartsWith("emulator-", StringComparison.Ordinal)) return null;
        await LocalCommand.RunAsync(adb, ["connect", serial], token).ConfigureAwait(false);
        devices = await LocalCommand.RunAsync(adb, ["devices"], token).ConfigureAwait(false);
        return IsOnline(devices, serial) ? serial : null;
    }

    private static bool IsOnline(byte[] output, string serial) =>
        Encoding.UTF8.GetString(output).Split('\n').Any(line => line.TrimEnd() == $"{serial}\tdevice");
}
