using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class FilterLayoutSettingsTests
{
    [Fact]
    public void LayoutChoice_SurvivesReloadAndDoesNotOverwriteTracking()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aspa-layout-" + Guid.NewGuid());
        try
        {
            var settings = new SettingsManager(dir);
            settings.LoadSideFilterLayout().Should().BeTrue();
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "tracking.json"), "[]");
            settings.SaveSideFilterLayout(true);
            new SettingsManager(dir).LoadSideFilterLayout().Should().BeTrue();
            settings.SaveSideFilterLayout(false);
            new SettingsManager(dir).LoadSideFilterLayout().Should().BeFalse();
            File.ReadAllText(Path.Combine(dir, "tracking.json")).Should().Be("[]");
            foreach (var invalid in new[] { "{}", "{broken", "null", "{\"SideFilters\":\"true\"}" })
            {
                File.WriteAllText(Path.Combine(dir, "ui.json"), invalid);
                settings.LoadSideFilterLayout().Should().BeTrue();
            }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
