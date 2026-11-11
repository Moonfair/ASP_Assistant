using System.Diagnostics;

namespace ASPAssistant.Core.Services;

internal static class LocalCommand
{
    internal static async Task<byte[]> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new IOException($"Could not start {Path.GetFileName(path)}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var stdout = new MemoryStream();
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(stdout, timeout.Token),
                stderr, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException($"{Path.GetFileName(path)} exited with code {process.ExitCode}: {stderr.Result.Trim()}");
            return stdout.ToArray();
        }
        catch (OperationCanceledException)
        {
            // An adb client may have started a shared adb server. Terminate the timed-out client only.
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileName(path)} did not respond within 5 seconds");
        }
    }
}
