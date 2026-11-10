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

    [Theory]
    [InlineData("ordinary path")]
    [InlineData("中文目录")]
    [InlineData("release$aspa[1]'quote`tick")]
    public async Task Installer_CopiesLiteralPaths_RestartsAndCleansOnlyItsJob(string name)
    {
        var job = NewJob();
        var otherJob = NewJob();
        var source = Path.Combine(job, name, "win-x64");
        var target = Path.Combine(_fixture, name, "app");
        Directory.CreateDirectory(Path.Combine(source, "data", "嵌套[1]$目录"));
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(source, "data", "嵌套[1]$目录", "marker.txt"), "new content");
        // A harmless GUI executable with no arguments, so testing restart opens no console.
        File.Copy(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), Path.Combine(source, "ASPAssistant.App.exe"));
        var script = await UpdateInstaller.PrepareAsync(source, target, job, int.MaxValue, Path.Combine(_fixture, "logs"));
        var result = await RunInstaller(script);
        result.ExitCode.Should().Be(0, result.Output);

        (await File.ReadAllTextAsync(Path.Combine(target, "data", "嵌套[1]$目录", "marker.txt"))).Should().Be("new content");
        File.Exists(Path.Combine(target, "ASPAssistant.App.exe")).Should().BeTrue();
        Directory.Exists(job).Should().BeFalse();
        Directory.Exists(otherJob).Should().BeTrue();
    }

    [Fact]
    public async Task Installer_CopyFailure_ReportsFailureAndCleansJob()
    {
        var job = NewJob();
        var source = Path.Combine(job, "source");
        var target = Path.Combine(_fixture, "app");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.Copy(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), Path.Combine(source, "ASPAssistant.App.exe"));
        var targetExe = Path.Combine(target, "ASPAssistant.App.exe");
        await File.WriteAllTextAsync(targetExe, "original");
        var script = await UpdateInstaller.PrepareAsync(source, target, job, int.MaxValue, Path.Combine(_fixture, "logs"));
        using (File.Open(targetExe, FileMode.Open, FileAccess.Read, FileShare.Read))
            (await RunInstaller(script)).ExitCode.Should().NotBe(0);

        (await File.ReadAllTextAsync(targetExe)).Should().Be("original");
        Directory.Exists(job).Should().BeFalse();
    }

    [Fact]
    public async Task Prepare_InvalidPackage_RejectsBeforeApplicationExits()
    {
        var job = NewJob();
        var prepare = () => UpdateInstaller.PrepareAsync(job, _fixture, job, 1, Path.Combine(_fixture, "logs"));
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
        var info = new UpdateInfo("v1.0.0", "1.0.0", "", "https://example.invalid/update.zip", "release.zip");
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
