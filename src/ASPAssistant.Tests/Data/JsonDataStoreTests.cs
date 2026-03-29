using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class JsonDataStoreTests
{
    private readonly string _testDataDir = Path.Combine(
        AppContext.BaseDirectory, "TestData");

    [Fact]
    public async Task LoadOperators_ReturnsAllOperators()
    {
        var store = new JsonDataStore(_testDataDir);
        var operators = await store.LoadOperatorsAsync();

        operators.Should().HaveCount(3);
        operators.Should().Contain(o => o.Name == "隐现");
        operators.Should().Contain(o => o.Name == "角峰");
        operators.Should().Contain(o => o.Name == "惊蛰");
    }

    [Fact]
    public async Task LoadEquipment_ReturnsAllEquipment()
    {
        var store = new JsonDataStore(_testDataDir);
        var (equipment, manualJobChange) = await store.LoadEquipmentAsync();

        equipment.Should().HaveCount(2);
        equipment.Should().Contain(e => e.Name == "不屈弹射器");
        manualJobChange.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadOperators_InvalidPath_ReturnsEmpty()
    {
        var store = new JsonDataStore("/nonexistent/path");
        var operators = await store.LoadOperatorsAsync();

        operators.Should().BeEmpty();
    }
}
