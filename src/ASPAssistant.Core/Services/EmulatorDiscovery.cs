using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public sealed class EmulatorDiscovery
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<IReadOnlyList<GameTarget>> FindAsync(CancellationToken token = default)
    {
        var installs = new HashSet<(GameTargetKind Kind, string Directory)>();
        foreach (string processName in new[] { "MuMuNxDevice", "MuMuPlayer", "dnplayer" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (path != null && FindInstall(processName, Path.GetDirectoryName(path)!) is { } install)
                            installs.Add(install);
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        // A process can exit during discovery or belong to a different Windows user.
                    }
                }
            }
        }

        var results = await Task.WhenAll(installs.Select(i => ReadInstallAsync(i.Kind, i.Directory, token)))
            .ConfigureAwait(false);
        var candidates = results.SelectMany(result => result)
            .Where(t => t.HasLiveWindow() && User32.GetRootWindow(t.RenderWindow) == t.Window)
            .ToArray();

        // LDPlayer 9 and 14 can use the same serial when both installs run the same index.
        // Leave those instances on their own Win32 capture path until the conflict is gone.
        var duplicateSerials = candidates.Where(t => t.Serial != null)
            .GroupBy(t => t.Serial, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Select(t => t.Serial != null && duplicateSerials.Contains(t.Serial)
            ? t with { Serial = null, AlternateSerial = null }
            : t).OrderBy(t => t.Kind).ThenBy(t => t.Instance).ToArray();
    }

    private static (GameTargetKind, string)? FindInstall(string processName, string processDirectory)
    {
        if (processName == "dnplayer")
            return File.Exists(Path.Combine(processDirectory, "ldconsole.exe"))
                ? (GameTargetKind.LdPlayer, processDirectory) : null;

        for (var folder = new DirectoryInfo(processDirectory); folder != null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "nx_main", "MuMuManager.exe")) ||
                File.Exists(Path.Combine(folder.FullName, "shell", "MuMuManager.exe")))
                return (GameTargetKind.MuMu, folder.FullName);
        }
        return null;
    }

    private static async Task<IReadOnlyList<GameTarget>> ReadInstallAsync(
        GameTargetKind kind, string install, CancellationToken token)
    {
        try
        {
            if (kind == GameTargetKind.MuMu)
            {
                var folder = File.Exists(Path.Combine(install, "nx_main", "MuMuManager.exe")) ? "nx_main" : "shell";
                var manager = Path.Combine(install, folder, "MuMuManager.exe");
                var adb = ExistingFile(Path.Combine(install, folder, "adb.exe"))
                    ?? ExistingFile(Path.Combine(install, folder, "vmonitor", "bin", "adb_server.exe"));
                var output = await LocalCommand.RunAsync(manager, ["info", "-v", "all"], token).ConfigureAwait(false);
                return ParseMuMu(Encoding.UTF8.GetString(output), install, adb);
            }

            var bytes = await LocalCommand.RunAsync(Path.Combine(install, "ldconsole.exe"), ["list2"], token)
                .ConfigureAwait(false);
            string lines;
            try { lines = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException)
            {
                lines = CodePagesEncodingProvider.Instance.GetEncoding(936)!.GetString(bytes);
            }
            return ParseLdPlayer(lines, install, ExistingFile(Path.Combine(install, "adb.exe")));
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or TimeoutException or JsonException)
        {
            AppLogger.Warn("Emulator", $"Could not discover {kind}: {ex.Message}");
            return [];
        }
    }

    private static string? ExistingFile(string path) => File.Exists(path) ? path : null;

    internal static IReadOnlyList<GameTarget> ParseMuMu(string output, string install, string? adb)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];
        using var document = JsonDocument.Parse(output.Trim().TrimStart('\uFEFF'));
        var root = document.RootElement;
        var entries = root.ValueKind switch
        {
            JsonValueKind.Object when root.TryGetProperty("index", out _) => new[] { root },
            JsonValueKind.Object => root.EnumerateObject().Select(property => property.Value).ToArray(),
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            _ => []
        };

        var targets = new List<GameTarget>();
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("is_process_started", out var running) || running.ValueKind != JsonValueKind.True ||
                !int.TryParse(Read(entry, "index"), out int index) || index < 0 ||
                !long.TryParse(Read(entry, "main_wnd"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long window) ||
                !long.TryParse(Read(entry, "render_wnd"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long render) ||
                window <= 0 || render <= 0)
                continue;

            string? serial = Read(entry, "adb_host_ip") is "127.0.0.1" or "localhost" &&
                int.TryParse(Read(entry, "adb_port"), out var port) && port is > 0 and <= 65535
                ? $"127.0.0.1:{port}" : null;
            targets.Add(new GameTarget($"mumu:{install}:{index}", Read(entry, "name") ?? $"MuMu {index}",
                GameTargetKind.MuMu, (nint)window, (nint)render, adb, serial, install, index,
                WindowPid: uint.TryParse(Read(entry, "pid"), out var pid) ? pid : 0));
        }
        return targets;
    }

    private static string? Read(JsonElement entry, string property)
    {
        if (!entry.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    internal static IReadOnlyList<GameTarget> ParseLdPlayer(string output, string install, string? adb)
    {
        var targets = new List<GameTarget>();
        foreach (var line in output.TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd().Split(',');
            if (fields.Length < 7 || !int.TryParse(fields[0], out int index) || index is < 0 or > 29990)
                continue;

            // Older list2 ends at the VM PID. Newer versions append width, height and DPI.
            int first = fields.Length >= 10 && fields[^6].Trim() is "0" or "1" ? fields.Length - 8 : fields.Length - 5;
            if (first < 2 || fields[first + 2].Trim() != "1" ||
                !long.TryParse(fields[first], out long window) || window <= 0 ||
                !long.TryParse(fields[first + 1], out long render) || render <= 0 ||
                !uint.TryParse(fields[first + 3], out uint pid) ||
                !int.TryParse(fields[first + 4], out int vmPid))
                continue;

            targets.Add(new GameTarget($"ld:{install}:{index}", string.Join(",", fields[1..first]),
                GameTargetKind.LdPlayer, (nint)window, (nint)render, adb, $"emulator-{5554 + 2 * index}",
                install, index, $"127.0.0.1:{5555 + 2 * index}", vmPid, pid));
        }
        return targets;
    }
}
