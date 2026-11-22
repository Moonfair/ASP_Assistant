using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ASPAssistant.Core.Services;

/// <summary>Samples only the local MuMu instance selected by game-source discovery.</summary>
public static class MuMuBattleFeed
{
    private static readonly Lazy<string> Payload = new(() =>
    {
        using var stream = typeof(MuMuBattleFeed).Assembly.GetManifestResourceStream("ASPA.MuMuBattle.dex")
            ?? throw new InvalidDataException("Missing embedded MuMu battle reader");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Convert.ToBase64String(bytes.ToArray());
    });

    // No APK installation, game writes, remote listener or persistent service.
    // The payload goes straight from the assembly to a root-owned temp directory;
    // never execute a pre-existing file from the shared /data/local/tmp.
    private const string Launch = """
        umask 077
        dir=$(mktemp -d /data/local/aspa-battle.XXXXXXXX) || exit 1
        trap 'rm -f "$dir/probe.dex"; rmdir "$dir"' EXIT
        echo upload
        IFS= read -r blob || exit 1
        printf %s "$blob" | base64 -d > "$dir/probe.dex" || exit 1
        chmod 400 "$dir/probe.dex" || exit 1
        CLASSPATH="$dir/probe.dex" app_process64 / aspa.mumu.BattleProbe "$1" "$2"
        """;
    private static string Quote(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";

    private static readonly string Bootstrap = """
        found=
        for pkg in com.hypergryph.arknights com.hypergryph.arknights.bilibili; do
          pid=$(pidof "$pkg") || continue
          case "$pid" in *[!0-9]*|'') echo ambiguous; exit 0;; esac
          if [ -n "$found" ]; then echo ambiguous; exit 0; fi
          found=$pkg; gamepid=$pid
        done
        if [ -z "$found" ]; then echo absent; exit 0; fi
        info=$(dumpsys package "$found")
        if ! echo "$info" | grep -Eq '^[[:space:]]*versionName=2\.7\.91[[:space:]]*$' ||
           ! echo "$info" | grep -Eq '^[[:space:]]*primaryCpuAbi=arm64-v8a[[:space:]]*$'; then echo unsupported; exit 0; fi
        if [ "$(id -u)" != 0 ] && [ "$(su -c 'id -u' 2>/dev/null)" != 0 ]; then echo permission; exit 0; fi
        """ + "\ncmd=" + Quote("sh -c " + Quote(Launch) + " aspa ") +
        "\"$gamepid $found\"\nif [ \"$(id -u)\" = 0 ]; then exec sh -c \"$cmd\"; else exec su -c \"$cmd\"; fi";

    public static async Task ConnectAsync(GameTarget target, Func<BattleUpdate, Task> publish, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target.Kind != GameTargetKind.MuMu || AdbConnection.GetLocalEndpoint(target) is not { } endpoint ||
            target.AdbPath is not { } adb || !File.Exists(adb))
        { await publish(Update(BattleFeedState.ConnectionFailed)); return; }

        while (true)
        {
            token.ThrowIfCancellationRequested();
            BattleFeedState state;
            try
            {
                state = await AdbConnection.ConnectAsync(adb, endpoint, token).ConfigureAwait(false) is { } serial
                    ? await ObserveAsync(adb, serial, publish, token).ConfigureAwait(false)
                    : BattleFeedState.ConnectionFailed;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or Win32Exception)
            {
                AppLogger.Warn("MuMuBattle", ex.Message);
                state = BattleFeedState.ConnectionFailed;
            }
            token.ThrowIfCancellationRequested();
            await publish(Update(state)).ConfigureAwait(false);
            if (state is BattleFeedState.PermissionRequired or BattleFeedState.UnsupportedBuild) return;
            await Task.Delay(state == BattleFeedState.ConnectionFailed ? 5000 : 1500, token).ConfigureAwait(false);
        }
    }

    private static async Task<BattleFeedState> ObserveAsync(string adb, string serial, Func<BattleUpdate, Task> publish,
        CancellationToken token)
    {
        var start = new ProcessStartInfo(adb)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "-s", serial, "shell", "-T", Bootstrap.Replace("\r", "") }) start.ArgumentList.Add(arg);
        using var proc = Process.Start(start) ?? throw new IOException("Cannot start MuMu ADB");
        var errors = proc.StandardError.ReadToEndAsync();
        var input = proc.StandardInput;
        input.NewLine = "\n";
        async Task<string> Read()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                return await proc.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("MuMu reader disconnected");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("MuMu reader timed out"); }
        }
        async Task Write(string line)
        {
            await input.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
            await input.FlushAsync(token).ConfigureAwait(false);
        }
        try
        {
            string line = await Read().ConfigureAwait(false);
            if (line != "upload") return Decode(line).State;
            await Write(Payload.Value).ConfigureAwait(false);
            line = await Read().ConfigureAwait(false);
            if (line != "ready") return Decode(line).State;
            while (true)
            {
                await Write("poll").ConfigureAwait(false);
                var update = Decode(await Read().ConfigureAwait(false));
                if (update.State is not (BattleFeedState.Live or BattleFeedState.WaitingForRound or BattleFeedState.ReadInterrupted))
                    return update.State;
                await publish(update).ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
        finally
        {
            try { input.Close(); } catch (IOException) { }
            try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { try { proc.Kill(); } catch (InvalidOperationException) { } }
            if (errors.IsCompletedSuccessfully && !string.IsNullOrWhiteSpace(errors.Result))
                AppLogger.Warn("MuMuBattle", errors.Result.Trim());
        }
    }

    private static BattleUpdate Update(BattleFeedState state, BattleFrame? frame = null) =>
        new(state, frame) { Source = GameTargetKind.MuMu };

    internal static BattleUpdate Decode(string line)
    {
        int separator = line.IndexOf('|');
        if (separator < 0) return DecodeBattle(line);
        var update = DecodeBattle(line[..separator]);
        return update.State is BattleFeedState.Live or BattleFeedState.WaitingForRound && line.Length - separator <= 4096
            ? update with { Stage = DecodeStage(line[(separator + 1)..]) } : update;
    }

    private static BattleStage? DecodeStage(string section)
    {
        static bool UInt(string s, out int n) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n);
        static bool Key(string s) => s.Length <= 128 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        var parts = section.Split(';');
        var head = parts[0].Split(',');
        if (head.Length != 5 || !UInt(head[0], out var phase) || phase > 14 ||
            !UInt(head[1], out var round) || !UInt(head[2], out var player) || player > 31 ||
            !Key(head[3]) || !UInt(head[4], out var hp)) return null;
        var stage = new BattleStage(phase, round, player, head[3], null);
        if (phase != GarrisonPhase.BossBattle || parts.Length > 4) return stage;
        var rows = new List<BossDamageEntry>();
        foreach (var part in parts.AsSpan(1))
        {
            var cells = part.Split(',');
            if (cells.Length != 3 || !UInt(cells[0], out var owner) || owner > 31 || cells[1].Length == 0 ||
                !Key(cells[1]) || !UInt(cells[2], out var amount)) return stage;
            var chess = UInt(cells[1], out var key) ? $"编号 {key}" : cells[1];
            rows.Add(new(owner, null, chess, amount));
        }
        return stage with { BossDamage = new(hp > 0 ? hp : null, rows.ToArray()) };
    }

    private static BattleUpdate DecodeBattle(string line)
    {
        if (!line.StartsWith("frame\t", StringComparison.Ordinal)) return Update(line switch
        {
            "idle" => BattleFeedState.WaitingForRound, "boot" => BattleFeedState.Connecting,
            "absent" => BattleFeedState.WaitingForClient, "retry" => BattleFeedState.ReadInterrupted,
            "unsupported" => BattleFeedState.UnsupportedBuild, "permission" => BattleFeedState.PermissionRequired,
            "ambiguous" => BattleFeedState.ConnectionFailed, _ => throw new InvalidDataException("Unknown MuMu reader response")
        });
        static void Check(bool valid) { if (!valid) throw new InvalidDataException("Invalid MuMu battle frame"); }
        Check(line.Length <= 32768);
        var p = line.Split('\t');
        Check(p.Length is >= 5 and <= 133);
        var ci = CultureInfo.InvariantCulture;
        Check(int.TryParse(p[1], NumberStyles.None, ci, out int round) && round > 0);
        Check(uint.TryParse(p[2], NumberStyles.None, ci, out uint frame));
        Check(long.TryParse(p[3], NumberStyles.None, ci, out long ticks) && ticks < (259200L << 32));
        Check(p[4] is "0" or "1");
        var counters = new Dictionary<string, OperatorCounters>(StringComparer.Ordinal);
        foreach (var cell in p.AsSpan(5))
        {
            var c = cell.Split(':');
            Check(c.Length == 5 && c[0].Length <= 110 && c[0].StartsWith("char_", StringComparison.Ordinal) &&
                c[0].All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '#' or '.' or '-'));
            double Value(int index)
            {
                Check(float.TryParse(c[index], NumberStyles.Float, ci, out var v) && v is >= 0 and <= 1e15f);
                return v;
            }
            Check(counters.TryAdd(c[0], new(Value(1), Value(2), Value(3), Value(4))));
        }
        return Update(BattleFeedState.Live, new(round, frame, ticks / 4294967296d, p[4]=="1", counters));
    }
}
