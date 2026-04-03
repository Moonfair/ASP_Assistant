using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsManager _manager;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"asp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _manager = new SettingsManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAndLoad_TrackingEntries_RoundTrips()
    {
        var entries = new List<TrackingEntry>
        {
            new() { Name = "角峰", Type = TrackingType.Operator },
            new() { Name = "不屈弹射器", Type = TrackingType.Equipment }
        };

        await _manager.SaveTrackingEntriesAsync(entries);
        var loaded = await _manager.LoadTrackingEntriesAsync();

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(e => e.Name == "角峰" && e.Type == TrackingType.Operator);
        loaded.Should().Contain(e => e.Name == "不屈弹射器" && e.Type == TrackingType.Equipment);
    }

    [Fact]
    public async Task LoadTrackingEntries_NoFile_ReturnsEmpty()
    {
        var loaded = await _manager.LoadTrackingEntriesAsync();
        loaded.Should().BeEmpty();
    }

}
