using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using ASPAssistant.Tests.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class LineupViewModelTests
{
    private static List<Operator> Operators() => Enumerable.Range(0, 31)
        .Select(i => new Operator { Name = $"干员{i}", Tier = i % 3 + 1 }).ToList();

    [Fact]
    public async Task Editor_AddsStrategyOperatorsToLineupWithoutTrackingThemInTheShop()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await store.LoadOperatorsAsync();
        var vm = new LineupEditorViewModel(operators, []) { Name = "策略阵容", Author = "博士" };
        foreach (var name in new[] { "暴躁铁皮", "盟约·辅助干员", "预备干员-医疗", "Touch" })
        {
            vm.OperatorBrowser.SearchText = name;
            vm.AddOperator(vm.AvailableOperators.Should().ContainSingle().Subject);
        }

        var lineup = vm.BuildLineup();
        lineup.Members.Select(member => member.OperatorName)
            .Should().Equal("“暴躁铁皮”", "盟约·辅助干员", "预备干员-医疗", "Touch");
        var tracking = new TrackingViewModel(operators);
        tracking.TrackLineup(lineup).Should().Be(0);
        tracking.TrackedOperators.Should().BeEmpty();
    }

    [Fact]
    public async Task Editor_EquipsFourSlotsForIronhideAndPreservesThemThroughShare()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await store.LoadOperatorsAsync();
        var (equipment, _) = await store.LoadEquipmentAsync();
        var vm = new LineupEditorViewModel(operators, equipment) { Name = "测试四槽", Author = "博士" };
        vm.OperatorBrowser.SearchText = "暴躁铁皮";
        vm.AddOperator(vm.AvailableOperators.Should().ContainSingle().Subject);
        var ironhide = vm.Members.Single();
        var names = equipment.Take(4).Select(item => item.Name).ToArray();
        foreach (var name in names) vm.Equip(ironhide, name).Should().BeTrue();
        vm.Equip(ironhide, names[0]).Should().BeFalse();
        vm.ClearEquipment(ironhide, 3);
        vm.Equip(ironhide, names[2], 3).Should().BeTrue();

        var ordinary = operators.First(op => op.Name == "折光");
        vm.AddOperator(ordinary);
        vm.Equip(vm.Members.Last(), names[3], 4).Should().BeFalse();
        var share = vm.ExportLineup(vm.BuildLineup());
        share.Should().Contain("\"Equipment3\": \"" + names[2] + "\"")
            .And.Contain("\"Equipment4\": \"" + names[3] + "\"");
        var codec = LineupShareService.FromCatalog(operators, equipment.Select(item => item.Name));
        codec.Import(share).Members[0].EquipmentSlots.Should().Equal(names);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var library = new LineupStore(path);
            await library.SaveAsync([vm.BuildLineup()]);
            var reopened = new LineupEditorViewModel(operators, equipment, (await library.LoadAsync()).Single());
            reopened.ExportLineup(reopened.BuildLineup()).Should().Be(share);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Editor_PreventsDuplicatesAndThirtyFirstOperator_AndFreesRemovedSlot()
    {
        var operators = Operators();
        var vm = new LineupEditorViewModel(operators, []);
        foreach (var op in operators) vm.AddOperator(op);
        vm.Members.Should().HaveCount(30);
        vm.AddOperator(operators[0]);
        vm.Members.Should().HaveCount(30);
        vm.RemoveMember(vm.Members[0]);
        vm.AddOperator(operators[30]);
        vm.Members.Should().HaveCount(30);
        vm.Members.Last().OperatorName.Should().Be("干员30");
        vm.SearchText = "干员0";
        vm.AvailableOperators.Select(o => o.Name).Should().Equal("干员0");
    }

    [Fact]
    public void Editor_EditsCopyAndSnapshotDoesNotChangeWithLaterEdits()
    {
        var original = LineupShareTests.Example();
        var vm = new LineupEditorViewModel([new() { Name = "能天使" }, new() { Name = "阿米娅" }],
            [new() { Name = "长剑" }, new() { Name = "法杖" }], original);
        vm.Author = "另一位作者";
        vm.Members[0].IsStackingCore = false;
        vm.Members[0].Equipment2 = "法杖";
        var saved = vm.BuildLineup();
        vm.Members[0].Equipment2 = "";
        original.Author.Should().Be("博士 🐱");
        original.Members[0].IsStackingCore.Should().BeTrue();
        original.Members[0].Equipment2.Should().Be("长剑");
        saved.Author.Should().Be("另一位作者");
        saved.Members[0].Equipment2.Should().Be("法杖");
        saved.Members[0].IsDamageCore.Should().BeTrue();
        saved.Id.Should().Be(original.Id);
    }

    [Fact]
    public void Editor_CombinesAllCatalogFilters_AndExcludesSelectedMembers()
    {
        var ops = new List<Operator>
        {
            new() { Name = "目标", Tier = 2, CoreCovenants = ["甲"], AdditionalCovenants = ["乙"],
                Normal = new() { Traits = [new() { TraitType = "经济", TraitDescription = "<获得时>层数+1" }] } },
            new() { Name = "禁用", Tier = 2, CoreCovenants = ["甲"], AdditionalCovenants = ["乙"],
                Normal = new() { Traits = [new() { TraitType = "经济", TraitDescription = "<获得时>层数+1" }] } },
            new() { Name = "其他", Tier = 1, CoreCovenants = ["丙"] }
        };
        var vm = new LineupEditorViewModel(ops, []);
        vm.OperatorBrowser.SetBanChecker(n => n == "禁用");
        vm.OperatorBrowser.SetTierFilters([2]);
        vm.OperatorBrowser.SetCoreCovenantFilters(["甲"]);
        vm.OperatorBrowser.SetOtherCovenantFilters(["乙"]);
        vm.OperatorBrowser.SetTraitTypeFilters(["经济"]);
        vm.OperatorBrowser.SetTriggerTimingFilters(["获得时"]);
        vm.OperatorBrowser.SelectedStackingFilter = true;
        vm.OperatorBrowser.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        vm.AvailableOperators.Select(o => o.Name).Should().Equal("目标");
        vm.AddOperator(ops[0]);
        vm.AvailableOperators.Should().BeEmpty();
        vm.RemoveMember(vm.Members[0]);
        vm.AvailableOperators.Select(o => o.Name).Should().Equal("目标");
        vm.OperatorBrowser.SelectedAvailabilityFilter = AvailabilityFilter.Unavailable;
        vm.AvailableOperators.Select(o => o.Name).Should().Equal("禁用");
        vm.OperatorBrowser.SearchText = "不存在";
        vm.AvailableOperators.Should().BeEmpty();
    }

    [Fact]
    public void Editor_EquipFillsEmptySlots_ExplicitSlotReplaces_AndSelectionFollowsRemoval()
    {
        var ops = Operators();
        var gear = new List<Equipment> { new() { Name = "长剑", IconPath = "sword.png" }, new() { Name = "法杖" } };
        var vm = new LineupEditorViewModel(ops, gear);
        vm.AddOperator(ops[0]);
        var first = vm.SelectedMember!;
        vm.AddOperator(ops[1]);
        vm.SelectedMember.Should().BeSameAs(vm.Members[1]);
        vm.Equip(first, "长剑").Should().BeTrue();
        vm.Equip(first, "法杖").Should().BeTrue();
        first.Equipment1.Should().Be("长剑");
        first.Equipment1IconPath.Should().Be("sword.png");
        vm.Equip(first, "长剑").Should().BeFalse();
        first.Equipment2.Should().Be("法杖");
        vm.Equip(first, "长剑", 2).Should().BeTrue();
        vm.Equip(first, "未知装备", 1).Should().BeFalse();
        vm.Equip(first, "长剑", 3).Should().BeFalse();
        vm.Members[1].Equipment1.Should().BeEmpty();
        vm.ClearEquipment(first, 1);
        first.Equipment1.Should().BeEmpty();
        first.Equipment1IconPath.Should().BeEmpty();
        vm.Equip(first, "法杖").Should().BeTrue();
        first.Equipment1.Should().Be("法杖");
        vm.RemoveMember(first);
        vm.SelectedMember.Should().BeSameAs(vm.Members[0]);
        vm.Equip(first, "长剑").Should().BeFalse("removed targets must not receive equipment");
        vm.RemoveMember(vm.Members[0]);
        vm.SelectedMember.Should().BeNull();
    }

    [Fact]
    public void Editor_EquipmentFiltersPreserveExistingTypeClassification()
    {
        var gear = new List<Equipment>
        {
            new() { Name = "转职甲", Tier = 1, Type = EquipmentType.JobChange },
            new() { Name = "普通甲", Tier = 1 }, new() { Name = "普通乙", Tier = 2 }
        };
        var vm = new LineupEditorViewModel([], gear);
        vm.EquipmentBrowser.SetTypeFilters(["转职"]);
        vm.EquipmentBrowser.FilteredEquipment.Select(e => e.Name).Should().Equal("转职甲");
        vm.EquipmentBrowser.SetTypeFilters(["普通"]);
        vm.EquipmentBrowser.SetTierFilters([2]);
        vm.EquipmentBrowser.SearchText = "乙";
        vm.EquipmentBrowser.FilteredEquipment.Select(e => e.Name).Should().Equal("普通乙");
    }

    [Fact]
    public async Task Library_SaveEditImportAndDeleteSurviveReload()
    {
        var path = Path.Combine(Path.GetTempPath(), "ASPA-LineupTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LineupStore(path);
            var vm = new LineupLibraryViewModel(store);
            await vm.LoadAsync();
            var first = LineupShareTests.Example();
            await vm.SaveAsync(first);
            await vm.SaveAsync(first with { Author = "更新作者" });
            var codec = new LineupShareService(["能天使", "阿米娅"], ["长剑", "法杖"]);
            await vm.SaveAsync(codec.Import(codec.Export(first)));
            var reloaded = new LineupLibraryViewModel(store);
            await reloaded.LoadAsync();
            reloaded.Lineups.Should().HaveCount(2);
            reloaded.Lineups[0].Author.Should().Be("更新作者");
            await reloaded.DeleteAsync(first);
            (await store.LoadAsync()).Should().ContainSingle().Which.Author.Should().Be("博士 🐱");
            Directory.GetFiles(path, "*.tmp").Should().BeEmpty();
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Fact]
    public async Task Library_CanEditAndShareAStoredLineupWithAvailableStrategies()
    {
        var directory = Directory.CreateTempSubdirectory("ASPA-LineupTests-").FullName;
        var file = Path.Combine(directory, "lineups.json");
        const string publishedLink = "https://aspa.moonfair.cn/?lineup=3f3bdc3d-91c1-4cd0-8984-6923a32e0afb";
        try
        {
            await File.WriteAllTextAsync(file, """
                {
                  "Version": 1,
                  "Lineups": [{
                    "Id": "26bc0c8a-e7a9-4d94-a6c8-5193a63640c9",
                    "Name": "策略阵容",
                    "Author": "博士",
                    "Members": [{ "OperatorName": "能天使" }],
                    "AvailableStrategies": ["众志合一", "旧赛季策略"],
                    "PublishedLink": "https://aspa.moonfair.cn/?lineup=3f3bdc3d-91c1-4cd0-8984-6923a32e0afb"
                  }]
                }
                """);
            var store = new LineupStore(directory);
            var library = new LineupLibraryViewModel(store);
            await library.LoadAsync();
            var source = library.Lineups.Should().ContainSingle().Which;
            var editor = new LineupEditorViewModel([new() { Name = "能天使" }], [], source) { Name = "更新阵容" };
            var saved = editor.BuildLineup();
            (await library.SaveAsync(saved)).Should().BeTrue();
            var share = editor.ExportLineup(saved);
            share.Should().StartWith("ASPA-LINEUP/2").And.NotContain("PublishedLink");
            var imported = new LineupShareService(["能天使"], []).Import(share);
            (await library.SaveAsync(imported)).Should().BeTrue();

            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var lineups = document.RootElement.GetProperty("Lineups");
            lineups.GetArrayLength().Should().Be(2);
            lineups[0].GetProperty("Name").GetString().Should().Be("更新阵容");
            lineups[0].GetProperty("PublishedLink").GetString().Should().Be(publishedLink);
            foreach (var lineup in lineups.EnumerateArray())
                lineup.GetProperty("AvailableStrategies").EnumerateArray().Select(strategy => strategy.GetString())
                    .Should().Equal("众志合一", "旧赛季策略");
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Library_UnreadableFileCannotBeOverwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), "ASPA-LineupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(path, "lineups.json"), "broken");
            var vm = new LineupLibraryViewModel(new LineupStore(path));
            await vm.LoadAsync();
            vm.Status.Should().NotBeEmpty();
            (await vm.SaveAsync(LineupShareTests.Example())).Should().BeFalse();
            (await File.ReadAllTextAsync(Path.Combine(path, "lineups.json"))).Should().Be("broken");
            vm.Lineups.Should().BeEmpty();
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task Library_FailedWriteDoesNotAddUnsavedLineup()
    {
        var path = Path.Combine(Path.GetTempPath(), "ASPA-LineupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var vm = new LineupLibraryViewModel(new LineupStore(path));
            await vm.LoadAsync();
            Directory.CreateDirectory(Path.Combine(path, "lineups.json"));
            (await vm.SaveAsync(LineupShareTests.Example())).Should().BeFalse();
            vm.Lineups.Should().BeEmpty();
            vm.Status.Should().NotBeEmpty();
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task Store_DirectoryAtFilePathIsReportedAsReadFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), "ASPA-LineupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "lineups.json"));
        try
        {
            var vm = new LineupLibraryViewModel(new LineupStore(path));
            await vm.LoadAsync();
            vm.Status.Should().NotBeEmpty();
        }
        finally { Directory.Delete(path, true); }
    }
}
