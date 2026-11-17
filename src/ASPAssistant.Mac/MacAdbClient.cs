using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace ASPAssistant.Mac;

public sealed class MacAdbClient(string executable)
{
    public string Executable { get; } = executable;

    public static string? FindExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        return new[] { "/opt/homebrew/bin/adb", "/usr/local/bin/adb" }
            .Concat(path.Select(folder => Path.Combine(folder, "adb")))
            .FirstOrDefault(File.Exists);
    }

    public static IReadOnlyList<string> ParseDevices(string output) => output
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split('\t', 2))
        .Where(parts => parts.Length == 2 &&
            (parts[1] == "device" || parts[1].StartsWith("device ", StringComparison.Ordinal)))
        .Select(parts => parts[0])
        .ToArray();

    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 &&
        bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
        BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]) > 0 &&
        BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]) > 0;

    public async Task<IReadOnlyList<string>> ListDevicesAsync(CancellationToken token = default)
    {
        var bytes = await RunAsync(["devices", "-l"], token).ConfigureAwait(false);
        return ParseDevices(Encoding.UTF8.GetString(bytes));
    }

    public async Task<byte[]> CaptureAsync(string serial, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) throw new ArgumentException("请选择 ADB 设备", nameof(serial));
        var bytes = await RunAsync(["-s", serial, "exec-out", "screencap", "-p"], token).ConfigureAwait(false);
        return IsPng(bytes) ? bytes : throw new IOException("ADB 未返回有效的 PNG 截图");
    }

    private async Task<byte[]> RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 ADB");
        try
        {
            using var output = new MemoryStream();
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException($"ADB 失败：{(await stderr.ConfigureAwait(false)).Trim()}");
            return output.ToArray();
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
