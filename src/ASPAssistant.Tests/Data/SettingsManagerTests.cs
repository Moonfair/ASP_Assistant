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

    [Fact]
    public void BobVoiceDefaultsToEnabledAndHonorsExplicitOptOut()
    {
        Assert.True(_manager.LoadBobVoice());

        _manager.SaveBobVoice(false);
        Assert.False(new SettingsManager(_tempDir).LoadBobVoice());
    }

    [Fact]
    public void VoiceIntervalAndPanelOpacityRestoreDefaultsAndSavedPreferences()
    {
        Assert.Equal(15, _manager.LoadBobChatterIntervalSeconds());
        Assert.Equal(1, _manager.LoadSidePanelOpacity());

        _manager.SaveBobVoice(false);
        _manager.SaveSideFilterLayout(false);
        _manager.SaveBobChatterIntervalSeconds(60);
        _manager.SaveSidePanelOpacity(0.65);

        var restored = new SettingsManager(_tempDir);
        Assert.Equal(60, restored.LoadBobChatterIntervalSeconds());
        Assert.Equal(0.65, restored.LoadSidePanelOpacity());
        Assert.False(restored.LoadBobVoice());
        Assert.False(restored.LoadSideFilterLayout());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("\"NaN\"")]
    [InlineData("1e1000")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("200")]
    public void InvalidNumericPreferencesUseDefaults(string json)
    {
        File.WriteAllText(Path.Combine(_tempDir, "bob-chatter-interval.json"), json);
        File.WriteAllText(Path.Combine(_tempDir, "side-panel-opacity.json"), json);

        Assert.Equal(15, _manager.LoadBobChatterIntervalSeconds());
        Assert.Equal(1, _manager.LoadSidePanelOpacity());
    }

    [Fact]
    public void FractionalChatterIntervalUsesDefault()
    {
        File.WriteAllText(Path.Combine(_tempDir, "bob-chatter-interval.json"), "15.5");
        Assert.Equal(15, _manager.LoadBobChatterIntervalSeconds());
    }

    [Theory]
    [InlineData(-20, 10, -1, 0.2)]
    [InlineData(10, 10, 0.2, 0.2)]
    [InlineData(120, 120, 1, 1)]
    [InlineData(500, 120, 2, 1)]
    public void NumericPreferencesStayWithinUsableLimits(int interval, int expectedInterval,
        double opacity, double expectedOpacity)
    {
        _manager.SaveBobChatterIntervalSeconds(interval);
        _manager.SaveSidePanelOpacity(opacity);

        Assert.Equal(expectedInterval, _manager.LoadBobChatterIntervalSeconds());
        Assert.Equal(expectedOpacity, _manager.LoadSidePanelOpacity());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteOpacitySavesAUsableDefault(double opacity)
    {
        _manager.SaveSidePanelOpacity(opacity);
        Assert.Equal(1, _manager.LoadSidePanelOpacity());
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
