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
    public void GameSourceDefaultsToAutomaticWithoutASavedPreference()
    {
        _manager.LoadGameTarget().Id.Should().Be("auto");
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("mumu:C:\\MuMu:0")]
    [InlineData("adb")]
    public void GameSourceKeepsExistingManualPreferences(string sourceId)
    {
        File.WriteAllText(Path.Combine(_tempDir, "game-target.json"),
            System.Text.Json.JsonSerializer.Serialize(new { Id = sourceId }));

        _manager.LoadGameTarget().Id.Should().Be(sourceId);
    }

    [Fact]
    public void MarkerOffset_RoundTripsAndCanBeReset()
    {
        _manager.LoadMarkerOffset().Should().Be(default(MarkerOffset));

        var offset = new MarkerOffset(-0.035, 0.06);
        _manager.SaveMarkerOffset(offset);
        new SettingsManager(_tempDir).LoadMarkerOffset().Should().Be(offset);

        _manager.SaveMarkerOffset(default);
        new SettingsManager(_tempDir).LoadMarkerOffset().Should().Be(default(MarkerOffset));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"X\":2,\"Y\":0}")]
    [InlineData("{\"X\":0,\"Y\":1e1000}")]
    public void MarkerOffset_InvalidFileUsesDefaultPosition(string json)
    {
        File.WriteAllText(Path.Combine(_tempDir, "marker-offset.json"), json);

        _manager.LoadMarkerOffset().Should().Be(default(MarkerOffset));
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

    [Theory]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"name\":\"\",\"type\":0}]")]
    [InlineData("[{\"name\":\"test\",\"type\":99}]")]
    public async Task LoadTrackingEntries_CorruptFile_PreservesOriginalAndRecovers(string content)
    {
        var path = Path.Combine(_tempDir, "tracking.json");
        await File.WriteAllTextAsync(path, content);

        (await _manager.LoadTrackingEntriesAsync()).Should().BeEmpty();
        var backup = Directory.GetFiles(_tempDir, "tracking.corrupt-*.json").Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(backup)).Should().Be(content);
        File.Exists(path).Should().BeFalse();

        await _manager.SaveTrackingEntriesAsync([new() { Name = "角峰" }]);
        (await _manager.LoadTrackingEntriesAsync()).Should().ContainSingle().Which.Name.Should().Be("角峰");
    }

    [Fact]
    public async Task SaveTrackingEntries_QueuedSaves_PersistLatestCompleteSnapshot()
    {
        var large = Enumerable.Range(0, 10000).Select(i => new TrackingEntry { Name = $"operator-{i}" }).ToList();
        var first = _manager.SaveTrackingEntriesAsync(large);
        var last = _manager.SaveTrackingEntriesAsync([new() { Name = "latest" }]);
        await Task.WhenAll(first, last);

        (await _manager.LoadTrackingEntriesAsync()).Should().ContainSingle().Which.Name.Should().Be("latest");
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveTrackingEntries_ReplacementDenied_KeepsPreviousFile()
    {
        await _manager.SaveTrackingEntriesAsync([new() { Name = "previous" }]);
        var path = Path.Combine(_tempDir, "tracking.json");
        var original = await File.ReadAllTextAsync(path);
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var save = () => _manager.SaveTrackingEntriesAsync([new() { Name = "new" }]);
            await save.Should().ThrowAsync<Exception>().Where(ex => ex is IOException || ex is UnauthorizedAccessException);
            (await File.ReadAllTextAsync(path)).Should().Be(original);
        }
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

}
