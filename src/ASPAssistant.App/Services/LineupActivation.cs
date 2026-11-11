using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using ASPAssistant.Core;
using Microsoft.Win32.SafeHandles;

namespace ASPAssistant.App.Services;

internal sealed class LineupActivation : IDisposable
{
    private const string OpenWindow = "open";
    private readonly string _name;
    private readonly Mutex _instance;
    private readonly CancellationTokenSource _stopping = new();

    internal bool IsPrimary { get; }

    internal LineupActivation(string? instanceName = null)
    {
        using var user = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        _name = instanceName ?? $"ASPAssistant.{user.User!.Value}.{process.SessionId}";
        _instance = new Mutex(false, _name, out var primary);
        IsPrimary = primary;
    }

    internal async Task ForwardAsync(string? hash)
    {
        using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);
        // The browser-launched process can pass its foreground permission to the running instance.
        if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId))
            AllowSetForegroundWindow(processId);
        await client.WriteAsync(Encoding.ASCII.GetBytes(hash ?? OpenWindow), timeout.Token);
    }

    internal async Task ListenAsync(Func<string?, Task> activate)
    {
        var stopping = _stopping.Token;
        var buffer = new byte[65];
        while (!stopping.IsCancellationRequested)
        {
            string message;
            using (var server = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                try
                {
                    await server.WaitForConnectionAsync(stopping);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var length = await server.ReadAtLeastAsync(buffer, buffer.Length, false, timeout.Token);
                    message = Encoding.ASCII.GetString(buffer, 0, length);
                }
                catch (OperationCanceledException) { continue; }
                catch (IOException ex)
                {
                    AppLogger.Warn("Activation", ex.Message);
                    continue;
                }
            }

            if (stopping.IsCancellationRequested) break;
            if (message == OpenWindow) await activate(null);
            else if (LineupProtocol.IsHash(message)) await activate(message);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        _instance.Dispose();
    }
}
