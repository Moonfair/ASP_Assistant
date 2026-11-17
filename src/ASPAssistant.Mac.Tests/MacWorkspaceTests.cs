using ASPAssistant.Mac;

namespace ASPAssistant.Mac.Tests;

public class MacWorkspaceTests
{
    [Fact]
    public async Task LoadsCatalogAndPersistsTracking()
    {
        var settings = Path.Combine(Path.GetTempPath(), "aspa-mac-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = Path.Combine(AppContext.BaseDirectory, "data");
            using var workspace = await MacWorkspace.LoadAsync(data, settings, initializeScanner: false);
            Assert.NotEmpty(workspace.Operators);
            Assert.NotEmpty(workspace.Equipment);
            var name = workspace.Operators.First(op => !op.StrategyOnly).Name;
            workspace.Tracking.AddTracking(name, ASPAssistant.Core.Models.TrackingType.Operator);
            await workspace.SaveTrackingAsync();
            using var reopened = await MacWorkspace.LoadAsync(data, settings, initializeScanner: false);
            Assert.Contains(reopened.Tracking.AllEntries, entry => entry.Name == name);
        }
        finally { if (Directory.Exists(settings)) Directory.Delete(settings, recursive: true); }
    }
}
