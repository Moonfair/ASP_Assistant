using System.Diagnostics;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), $"asp_update_test_{Guid.NewGuid():N}");
    private readonly List<string> _jobs = [];

    private string NewJob()
    {
        var job = UpdateInstaller.CreateJobDirectory();
        _jobs.Add(job);
        return job;
    }

    [Fact]
    public async Task Installer_RunsDownloadedExecutableAndCleansOnlyItsJob()
    {
        var job = NewJob();
        var otherJob = NewJob();
        // hostname.exe emits a known value only if the helper actually launches the payload.
        File.Copy(Path.Combine(Environment.SystemDirectory, "hostname.exe"), Path.Combine(job, "setup.exe"));
        var script = await UpdateInstaller.PrepareAsync(job, int.MaxValue, Path.Combine(_fixture, "logs"));
        var result = await RunInstaller(script);
        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().ContainEquivalentOf(Environment.MachineName);
        Directory.Exists(job).Should().BeFalse();
        Directory.Exists(otherJob).Should().BeTrue();
    }

    [Fact]
    public async Task Installer_LaunchFailure_LogsFailureAndCleansJob()
    {
        var job = NewJob();
        var logDir = Path.Combine(_fixture, "中文 logs [1]$");
        await File.WriteAllTextAsync(Path.Combine(job, "setup.exe"), "invalid executable");
        var script = await UpdateInstaller.PrepareAsync(job, int.MaxValue, logDir);
        (await RunInstaller(script)).ExitCode.Should().NotBe(0);
        File.Exists(Path.Combine(logDir, "update-errors.log")).Should().BeTrue();
        Directory.Exists(job).Should().BeFalse();
    }

    [Fact]
    public async Task Prepare_InvalidPackage_RejectsBeforeApplicationExits()
    {
        var job = NewJob();
        var prepare = () => UpdateInstaller.PrepareAsync(job, 1, Path.Combine(_fixture, "logs"));
        await prepare.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void Cleanup_RejectsDirectoryOutsideOwnedUpdateJobs()
    {
        Directory.CreateDirectory(_fixture);
        var cleanup = () => UpdateInstaller.DeleteJobDirectory(_fixture);
        cleanup.Should().Throw<ArgumentException>();
        Directory.Exists(_fixture).Should().BeTrue();
    }

    [Fact]
    public async Task Download_Canceled_CleansItsJobAndPreservesOtherJobs()
    {
        var otherJob = NewJob();
        var root = Path.GetDirectoryName(otherJob)!;
        var before = Directory.GetDirectories(root);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var info = new UpdateInfo("v1.0.0", "1.0.0", "", "https://example.invalid/setup.exe", "ASPAssistant-v1.0.0-win-x64-setup.exe");
        var download = () => new UpdateService().DownloadAndApplyUpdateAsync(info, ct: canceled.Token);
        await download.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetDirectories(root).Should().BeEquivalentTo(before);
        Directory.Exists(otherJob).Should().BeTrue();
    }

    private static async Task<(int ExitCode, string Output)> RunInstaller(string script)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        await Task.WhenAll(stdout, stderr);
        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose()
    {
        foreach (var job in _jobs) UpdateInstaller.DeleteJobDirectory(job);
        var expectedRoot = Path.GetFullPath(Path.GetTempPath()) + "asp_update_test_";
        if (!_fixture.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
    }
}
