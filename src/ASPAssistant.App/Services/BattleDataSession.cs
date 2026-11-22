using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core;
using ASPAssistant.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace ASPAssistant.App.Services;

internal sealed record BattleFeedMessage(uint WindowPid, BattleUpdate Update);

internal static class BattleDataSession
{
    internal const string WorkerSwitch = "--battle-data-worker";
    private static string PipeName(Guid id) => $"ASPAssistant.Battle.{id:N}";

    internal static async Task ConnectAsync(uint initialPid, bool mayRequestPermission,
        Func<BattleFeedMessage, Task> publish, CancellationToken token)
    {
        using var identity = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        await ConnectWithPermissionAsync(initialPid, mayRequestPermission, elevated,
            stopOnDenied => WatchClientsAsync(publish, stopOnDenied, token),
            () => ObserveElevatedAsync(publish, token), token).ConfigureAwait(false);
    }

    internal static async Task ConnectWithPermissionAsync(uint initialPid, bool mayRequestPermission, bool elevated,
        Func<bool, Task<bool>> watch, Func<Task> requestPermission, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (elevated || !mayRequestPermission || initialPid != 0)
        {
            bool canEscalate = mayRequestPermission && !elevated;
            bool denied = await watch(canEscalate).ConfigureAwait(false);
            if (!denied || !canEscalate) return;
        }
        token.ThrowIfCancellationRequested();
        await requestPermission().ConfigureAwait(false);
    }

    // The official client may start after the assistant, or restart with a new PID.
    // Send idle status too, so a disconnected parent also releases a waiting worker.
    private static async Task<bool> WatchClientsAsync(Func<BattleFeedMessage, Task> publish, bool stopOnDenied,
        CancellationToken token)
    {
        uint previousPid = 0;
        BattleFeedState state = BattleFeedState.WaitingForClient;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            uint pid = GameTargetSelection.FindWindowsClient()?.WindowPid ?? 0;
            if (pid != 0 && (pid != previousPid || state == BattleFeedState.WaitingForClient))
            {
                await ObserveAsync(pid, async update =>
                {
                    state = update.State;
                    if (state != BattleFeedState.PermissionRequired || !stopOnDenied)
                        await publish(new(pid, update)).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
                if (state == BattleFeedState.PermissionRequired && stopOnDenied) return true;
            }
            else
            {
                if (pid == 0) state = BattleFeedState.WaitingForClient;
                await publish(new(pid, new(state))).ConfigureAwait(false);
            }
            previousPid = pid;
            await Task.Delay(1000, token).ConfigureAwait(false);
        }
    }

    private static async Task ObserveAsync(uint pid, Func<BattleUpdate, Task> publish, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using var game = ArknightsBattleReader.Open(pid, out var initial);
            if (game == null) { await publish(new(initial)); return; }
            while (!game.HasExited && GameTargetSelection.FindWindowsClient()?.WindowPid == pid)
            {
                token.ThrowIfCancellationRequested();
                await publish(game.Read()).ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);
            }
            await publish(new(BattleFeedState.WaitingForClient));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { await publish(new(BattleFeedState.PermissionRequired)); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            AppLogger.Warn("BattleData", ex.Message);
            await publish(new(BattleFeedState.ConnectionFailed));
        }
    }

    private static async Task ObserveElevatedAsync(Func<BattleFeedMessage, Task> publish, CancellationToken token)
    {
        var id = Guid.NewGuid();
        using var pipe = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ASPAssistant.App.exe"))
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(WorkerSwitch);
        start.ArgumentList.Add(id.ToString("N"));
        token.ThrowIfCancellationRequested();
        using var worker = Process.Start(start) ?? throw new IOException("无法启动战斗采集进程");
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
        handshake.CancelAfter(TimeSpan.FromSeconds(15));
        try { await pipe.WaitForConnectionAsync(handshake.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("战斗采集进程连接超时"); }
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var peer) || peer != worker.Id)
            throw new IOException("战斗采集进程身份不匹配");

        using var input = new StreamReader(pipe);
        while (await input.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            var message = JsonSerializer.Deserialize<BattleFeedMessage>(line) ?? throw new InvalidDataException();
            await publish(message).ConfigureAwait(false);
        }
        throw new IOException("战斗采集进程已断开");
    }

    // The child accepts no commands. Monitor EOF so revocation stops an idle reader
    // without waiting for a future memory sample or a failed write.
    internal static async Task RunUntilParentClosesAsync(Stream pipe, Func<CancellationToken, Task> watch)
    {
        using var lifetime = new CancellationTokenSource();
        async Task MonitorParentAsync()
        {
            try { await pipe.ReadAsync(new byte[1], lifetime.Token).ConfigureAwait(false); }
            catch (IOException) { }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally { lifetime.Cancel(); }
        }
        var monitor = MonitorParentAsync();
        try { await watch(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { lifetime.Cancel(); await monitor.ConfigureAwait(false); }
    }

    internal static async Task<int> RunWorkerAsync(string[] args)
    {
        if (args.Length != 2 || !Guid.TryParseExact(args[1], "N", out var id))
            return 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);
            await pipe.ConnectAsync(12000).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var peer)) return 1;
            using var parent = Process.GetProcessById(checked((int)peer));
            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                return 1;
            using var output = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            await RunUntilParentClosesAsync(pipe, token => WatchClientsAsync(
                update => output.WriteLineAsync(JsonSerializer.Serialize(update)), false, token));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            return 1;
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
}
