# ASPAssistant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a WPF overlay tool for Arknights PC that provides a side panel for browsing Garrison Protocol operator/equipment data and an overlay that marks tracked items when they appear in the shop.

**Architecture:** Dual-window WPF app (SidePanel + Overlay) sharing ViewModels via MVVM. Core engine handles game window tracking via User32 P/Invoke and OCR scanning via MaaFramework .NET bindings. GameState layer stores real-time data extracted from screenshots.

**Tech Stack:** C# / .NET 8 / WPF / MaaFramework (NuGet: Maa.Framework) / System.Text.Json / CommunityToolkit.Mvvm

**Spec:** `docs/superpowers/specs/2026-03-26-asp-assistant-design.md`

**Important:** This is a Windows-only WPF project. Build and test on Windows with the Arknights PC client installed.

---

## File Structure

```
ASPAssistant/
├── ASPAssistant.sln
├── src/
│   ├── ASPAssistant.App/                    — WPF application (entry point)
│   │   ├── ASPAssistant.App.csproj
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── Windows/
│   │   │   ├── SidePanelWindow.xaml / .cs
│   │   │   └── OverlayWindow.xaml / .cs
│   │   ├── Views/
│   │   │   ├── OperatorBrowseView.xaml / .cs
│   │   │   ├── EquipmentBrowseView.xaml / .cs
│   │   │   └── TrackingView.xaml / .cs
│   │   ├── Controls/
│   │   │   ├── OperatorCard.xaml / .cs
│   │   │   └── EquipmentCard.xaml / .cs
│   │   ├── Converters/
│   │   │   └── TierToRomanConverter.cs
│   │   └── Resources/
│   │       └── Styles.xaml
│   ├── ASPAssistant.Core/                   — Core logic (no WPF dependency)
│   │   ├── ASPAssistant.Core.csproj
│   │   ├── Models/
│   │   │   ├── Operator.cs
│   │   │   ├── Equipment.cs
│   │   │   ├── ShopItem.cs
│   │   │   └── TrackingEntry.cs
│   │   ├── GameState/
│   │   │   ├── GameState.cs
│   │   │   └── GameStateUpdater.cs
│   │   ├── GameModes/
│   │   │   ├── IGameMode.cs
│   │   │   └── GarrisonProtocol/
│   │   │       ├── GarrisonProtocolMode.cs
│   │   │       └── GarrisonOcrStrategy.cs
│   │   ├── ViewModels/
│   │   │   ├── OperatorBrowseViewModel.cs
│   │   │   ├── EquipmentBrowseViewModel.cs
│   │   │   ├── TrackingViewModel.cs
│   │   │   └── GameStateViewModel.cs
│   │   ├── Data/
│   │   │   ├── JsonDataStore.cs
│   │   │   └── SettingsManager.cs
│   │   ├── Services/
│   │   │   ├── OcrScannerService.cs
│   │   │   └── ScreenCaptureService.cs
│   │   └── Interop/
│   │       └── User32.cs
│   └── ASPAssistant.Tests/                  — Unit tests
│       ├── ASPAssistant.Tests.csproj
│       ├── Models/
│       │   ├── OperatorTests.cs
│       │   └── EquipmentTests.cs
│       ├── Data/
│       │   ├── JsonDataStoreTests.cs
│       │   └── SettingsManagerTests.cs
│       ├── ViewModels/
│       │   ├── OperatorBrowseViewModelTests.cs
│       │   ├── EquipmentBrowseViewModelTests.cs
│       │   └── TrackingViewModelTests.cs
│       ├── GameState/
│       │   └── GameStateUpdaterTests.cs
│       └── TestData/
│           ├── operators.json
│           └── equipment.json
├── data/
│   ├── operators.json                       — Operator data (user-provided)
│   └── equipment.json                       — Equipment data (user-provided)
└── docs/
```

---

### Task 1: Project Scaffolding

**Files:**
- Create: `ASPAssistant.sln`
- Create: `src/ASPAssistant.App/ASPAssistant.App.csproj`
- Create: `src/ASPAssistant.Core/ASPAssistant.Core.csproj`
- Create: `src/ASPAssistant.Tests/ASPAssistant.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd /Users/moonfair/Projects/ASPAssistant
dotnet new sln -n ASPAssistant
dotnet new wpf -n ASPAssistant.App -o src/ASPAssistant.App
dotnet new classlib -n ASPAssistant.Core -o src/ASPAssistant.Core
dotnet new xunit -n ASPAssistant.Tests -o src/ASPAssistant.Tests
dotnet sln add src/ASPAssistant.App/ASPAssistant.App.csproj
dotnet sln add src/ASPAssistant.Core/ASPAssistant.Core.csproj
dotnet sln add src/ASPAssistant.Tests/ASPAssistant.Tests.csproj
```

- [ ] **Step 2: Add project references and NuGet packages**

```bash
# Core references
dotnet add src/ASPAssistant.App reference src/ASPAssistant.Core
dotnet add src/ASPAssistant.Tests reference src/ASPAssistant.Core

# NuGet packages
dotnet add src/ASPAssistant.Core package CommunityToolkit.Mvvm
dotnet add src/ASPAssistant.Core package Maa.Framework
dotnet add src/ASPAssistant.Tests package Moq
dotnet add src/ASPAssistant.Tests package FluentAssertions
```

- [ ] **Step 3: Configure Core csproj for net8.0-windows**

Edit `src/ASPAssistant.Core/ASPAssistant.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

Edit `src/ASPAssistant.Tests/ASPAssistant.Tests.csproj` to also target `net8.0-windows`.

- [ ] **Step 4: Create directory structure**

```bash
cd src/ASPAssistant.App
mkdir -p Windows Views Controls Converters Resources

cd ../ASPAssistant.Core
mkdir -p Models GameState GameModes/GarrisonProtocol ViewModels Data Services Interop

cd ../ASPAssistant.Tests
mkdir -p Models Data ViewModels GameState TestData

cd ../../..
mkdir -p data
```

- [ ] **Step 5: Create .gitignore and verify build**

Create `.gitignore` with standard .NET entries (bin/, obj/, .vs/, *.user, .superpowers/).

```bash
dotnet build ASPAssistant.sln
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold ASPAssistant solution with App, Core, and Tests projects"
```

---

### Task 2: Data Models

**Files:**
- Create: `src/ASPAssistant.Core/Models/Operator.cs`
- Create: `src/ASPAssistant.Core/Models/Equipment.cs`
- Create: `src/ASPAssistant.Core/Models/ShopItem.cs`
- Create: `src/ASPAssistant.Core/Models/TrackingEntry.cs`
- Test: `src/ASPAssistant.Tests/Models/OperatorTests.cs`
- Test: `src/ASPAssistant.Tests/Models/EquipmentTests.cs`

- [ ] **Step 1: Write failing tests for Operator model deserialization**

`src/ASPAssistant.Tests/Models/OperatorTests.cs`:
```csharp
using System.Text.Json;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Models;

public class OperatorTests
{
    [Fact]
    public void Deserialize_ValidJson_ReturnsOperator()
    {
        var json = """
        {
            "name": "隐现",
            "tier": 6,
            "coreCovenant": "拉特兰",
            "additionalCovenants": ["迅捷"],
            "normal": {
                "traitType": "作战能力",
                "traitDescription": "【拉特兰】每叠加5层，本干员攻击速度+1"
            },
            "elite": {
                "traitType": "作战能力",
                "traitDescription": "【拉特兰】每叠加5层，本干员攻击速度+2"
            }
        }
        """;

        var op = JsonSerializer.Deserialize<Operator>(json);

        op.Should().NotBeNull();
        op!.Name.Should().Be("隐现");
        op.Tier.Should().Be(6);
        op.CoreCovenant.Should().Be("拉特兰");
        op.AdditionalCovenants.Should().ContainSingle("迅捷");
        op.Normal.TraitType.Should().Be("作战能力");
        op.Normal.TraitDescription.Should().Contain("攻击速度+1");
        op.Elite.TraitDescription.Should().Contain("攻击速度+2");
    }

    [Fact]
    public void Deserialize_MultipleCovenants_AllPresent()
    {
        var json = """
        {
            "name": "角峰",
            "tier": 4,
            "coreCovenant": "谢拉格",
            "additionalCovenants": ["坚守"],
            "normal": {
                "traitType": "单次叠加",
                "traitDescription": "<获得时>自身所属盟约层数+2（无需激活盟约）"
            },
            "elite": {
                "traitType": "单次叠加",
                "traitDescription": "<获得时>自身所属盟约层数+4（无需激活盟约）"
            }
        }
        """;

        var op = JsonSerializer.Deserialize<Operator>(json);

        op!.Name.Should().Be("角峰");
        op.CoreCovenant.Should().Be("谢拉格");
        op.AdditionalCovenants.Should().Contain("坚守");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/ASPAssistant.Tests --filter "OperatorTests" -v n
```

Expected: FAIL — `Operator` type does not exist.

- [ ] **Step 3: Implement data models**

`src/ASPAssistant.Core/Models/Operator.cs`:
```csharp
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public class OperatorVariant
{
    [JsonPropertyName("traitType")]
    public string TraitType { get; set; } = "";

    [JsonPropertyName("traitDescription")]
    public string TraitDescription { get; set; } = "";
}

public class Operator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("coreCovenant")]
    public string CoreCovenant { get; set; } = "";

    [JsonPropertyName("additionalCovenants")]
    public List<string> AdditionalCovenants { get; set; } = [];

    [JsonPropertyName("normal")]
    public OperatorVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public OperatorVariant Elite { get; set; } = new();
}
```

`src/ASPAssistant.Core/Models/Equipment.cs`:
```csharp
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public class EquipmentVariant
{
    [JsonPropertyName("effectDescription")]
    public string EffectDescription { get; set; } = "";
}

public class Equipment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("normal")]
    public EquipmentVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public EquipmentVariant Elite { get; set; } = new();
}
```

`src/ASPAssistant.Core/Models/ShopItem.cs`:
```csharp
namespace ASPAssistant.Core.Models;

public class ShopItem
{
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public bool IsTracked { get; set; }

    /// <summary>
    /// OCR-detected bounding box in screenshot pixel coordinates: (X, Y, Width, Height).
    /// </summary>
    public (double X, double Y, double Width, double Height) OcrRegion { get; set; }
}
```

`src/ASPAssistant.Core/Models/TrackingEntry.cs`:
```csharp
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public enum TrackingType
{
    Operator,
    Equipment
}

public class TrackingEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public TrackingType Type { get; set; }
}
```

- [ ] **Step 4: Write and run Equipment deserialization test**

`src/ASPAssistant.Tests/Models/EquipmentTests.cs`:
```csharp
using System.Text.Json;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Models;

public class EquipmentTests
{
    [Fact]
    public void Deserialize_ValidJson_ReturnsEquipment()
    {
        var json = """
        {
            "name": "不屈弹射器",
            "tier": 2,
            "normal": {
                "effectDescription": "再部署时间-30%，生命值-30%"
            },
            "elite": {
                "effectDescription": "再部署时间-50%，生命值-30%"
            }
        }
        """;

        var eq = JsonSerializer.Deserialize<Equipment>(json);

        eq.Should().NotBeNull();
        eq!.Name.Should().Be("不屈弹射器");
        eq.Tier.Should().Be(2);
        eq.Normal.EffectDescription.Should().Contain("-30%");
        eq.Elite.EffectDescription.Should().Contain("-50%");
    }
}
```

```bash
dotnet test src/ASPAssistant.Tests --filter "OperatorTests|EquipmentTests" -v n
```

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ASPAssistant.Core/Models src/ASPAssistant.Tests/Models
git commit -m "feat: add Operator, Equipment, ShopItem, TrackingEntry data models"
```

---

### Task 3: JSON Data Store

**Files:**
- Create: `src/ASPAssistant.Core/Data/JsonDataStore.cs`
- Create: `src/ASPAssistant.Tests/TestData/operators.json`
- Create: `src/ASPAssistant.Tests/TestData/equipment.json`
- Test: `src/ASPAssistant.Tests/Data/JsonDataStoreTests.cs`

- [ ] **Step 1: Create test data files**

`src/ASPAssistant.Tests/TestData/operators.json`:
```json
{
  "operators": [
    {
      "name": "隐现",
      "tier": 6,
      "coreCovenant": "拉特兰",
      "additionalCovenants": ["迅捷"],
      "normal": { "traitType": "作战能力", "traitDescription": "【拉特兰】每叠加5层，本干员攻击速度+1" },
      "elite": { "traitType": "作战能力", "traitDescription": "【拉特兰】每叠加5层，本干员攻击速度+2" }
    },
    {
      "name": "角峰",
      "tier": 4,
      "coreCovenant": "谢拉格",
      "additionalCovenants": ["坚守"],
      "normal": { "traitType": "单次叠加", "traitDescription": "<获得时>自身所属盟约层数+2（无需激活盟约）" },
      "elite": { "traitType": "单次叠加", "traitDescription": "<获得时>自身所属盟约层数+4（无需激活盟约）" }
    },
    {
      "name": "惊蛰",
      "tier": 6,
      "coreCovenant": "炎",
      "additionalCovenants": [],
      "normal": { "traitType": "单次叠加", "traitDescription": "<获得时>获得等于当前调度中心等级的【炎】层数（无需激活盟约）" },
      "elite": { "traitType": "单次叠加", "traitDescription": "<获得时>获得等于两倍的当前调度中心等级的【炎】层数（无需激活盟约）" }
    }
  ]
}
```

`src/ASPAssistant.Tests/TestData/equipment.json`:
```json
{
  "equipment": [
    {
      "name": "不屈弹射器",
      "tier": 2,
      "normal": { "effectDescription": "再部署时间-30%，生命值-30%" },
      "elite": { "effectDescription": "再部署时间-50%，生命值-30%" }
    },
    {
      "name": "红豆购物袋",
      "tier": 1,
      "normal": { "effectDescription": "购买价格为1" },
      "elite": { "effectDescription": "购买价格为1" }
    }
  ]
}
```

Mark these as `CopyToOutputDirectory` in the test .csproj:
```xml
<ItemGroup>
  <Content Include="TestData\**\*.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 2: Write failing tests for JsonDataStore**

`src/ASPAssistant.Tests/Data/JsonDataStoreTests.cs`:
```csharp
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
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

        var equipment = await store.LoadEquipmentAsync();

        equipment.Should().HaveCount(2);
        equipment.Should().Contain(e => e.Name == "不屈弹射器");
    }

    [Fact]
    public async Task LoadOperators_InvalidPath_ReturnsEmpty()
    {
        var store = new JsonDataStore("/nonexistent/path");

        var operators = await store.LoadOperatorsAsync();

        operators.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test src/ASPAssistant.Tests --filter "JsonDataStoreTests" -v n
```

Expected: FAIL — `JsonDataStore` does not exist.

- [ ] **Step 4: Implement JsonDataStore**

`src/ASPAssistant.Core/Data/JsonDataStore.cs`:
```csharp
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public class JsonDataStore
{
    private readonly string _dataDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JsonDataStore(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public async Task<List<Operator>> LoadOperatorsAsync()
    {
        var path = Path.Combine(_dataDirectory, "operators.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        var wrapper = JsonSerializer.Deserialize<OperatorDataFile>(json, JsonOptions);
        return wrapper?.Operators ?? [];
    }

    public async Task<List<Equipment>> LoadEquipmentAsync()
    {
        var path = Path.Combine(_dataDirectory, "equipment.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        var wrapper = JsonSerializer.Deserialize<EquipmentDataFile>(json, JsonOptions);
        return wrapper?.Equipment ?? [];
    }

    private class OperatorDataFile
    {
        public List<Operator> Operators { get; set; } = [];
    }

    private class EquipmentDataFile
    {
        public List<Equipment> Equipment { get; set; } = [];
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test src/ASPAssistant.Tests --filter "JsonDataStoreTests" -v n
```

Expected: All PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ASPAssistant.Core/Data/JsonDataStore.cs src/ASPAssistant.Tests/Data src/ASPAssistant.Tests/TestData
git commit -m "feat: add JsonDataStore for loading operator and equipment data from JSON files"
```

---

### Task 4: Settings Manager

**Files:**
- Create: `src/ASPAssistant.Core/Data/SettingsManager.cs`
- Test: `src/ASPAssistant.Tests/Data/SettingsManagerTests.cs`

- [ ] **Step 1: Write failing tests**

`src/ASPAssistant.Tests/Data/SettingsManagerTests.cs`:
```csharp
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

    [Fact]
    public void GetOcrInterval_Default_Returns3Seconds()
    {
        _manager.OcrScanIntervalSeconds.Should().Be(3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/ASPAssistant.Tests --filter "SettingsManagerTests" -v n
```

Expected: FAIL — `SettingsManager` does not exist.

- [ ] **Step 3: Implement SettingsManager**

`src/ASPAssistant.Core/Data/SettingsManager.cs`:
```csharp
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public class SettingsManager
{
    private readonly string _settingsDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public int OcrScanIntervalSeconds { get; set; } = 3;

    public SettingsManager(string settingsDir)
    {
        _settingsDir = settingsDir;
    }

    public async Task SaveTrackingEntriesAsync(List<TrackingEntry> entries)
    {
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "tracking.json");
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<List<TrackingEntry>> LoadTrackingEntriesAsync()
    {
        var path = Path.Combine(_settingsDir, "tracking.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<TrackingEntry>>(json, JsonOptions) ?? [];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test src/ASPAssistant.Tests --filter "SettingsManagerTests" -v n
```

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ASPAssistant.Core/Data/SettingsManager.cs src/ASPAssistant.Tests/Data/SettingsManagerTests.cs
git commit -m "feat: add SettingsManager for persisting tracking entries and settings"
```

---

### Task 5: Win32 Interop (User32)

**Files:**
- Create: `src/ASPAssistant.Core/Interop/User32.cs`

- [ ] **Step 1: Implement User32 P/Invoke declarations**

`src/ASPAssistant.Core/Interop/User32.cs`:
```csharp
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public static class User32
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // Extended window style constants
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>
    /// Find the Arknights PC window by title.
    /// </summary>
    public static IntPtr FindArknightsWindow()
    {
        return FindWindow(null, "明日方舟");
    }

    /// <summary>
    /// Get the client area of a window in screen coordinates.
    /// </summary>
    public static RECT? GetClientRectScreen(IntPtr hWnd)
    {
        if (!GetClientRect(hWnd, out var clientRect))
            return null;

        var topLeft = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref topLeft))
            return null;

        return new RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = topLeft.X + clientRect.Width,
            Bottom = topLeft.Y + clientRect.Height
        };
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ASPAssistant.Core
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ASPAssistant.Core/Interop/User32.cs
git commit -m "feat: add User32 P/Invoke declarations for window tracking"
```

---

### Task 6: Window Tracker Service

**Files:**
- Create: `src/ASPAssistant.Core/Services/WindowTrackerService.cs`

- [ ] **Step 1: Implement WindowTrackerService**

`src/ASPAssistant.Core/Services/WindowTrackerService.cs`:
```csharp
using System.Timers;
using ASPAssistant.Core.Interop;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class WindowTrackerService : IDisposable
{
    private IntPtr _gameWindowHandle;
    private readonly Timer _pollTimer;

    public event Action<RECT>? GameWindowMoved;
    public event Action? GameWindowLost;
    public event Action<bool>? GameWindowFocusChanged;

    public RECT? CurrentGameRect { get; private set; }
    public bool IsGameFocused { get; private set; }
    public bool IsGameFound => _gameWindowHandle != IntPtr.Zero
                                && User32.IsWindow(_gameWindowHandle);

    /// <summary>
    /// Whether the side panel should attach inside the game window
    /// (when fullscreen or insufficient screen space on the right).
    /// </summary>
    public bool ShouldAttachInside { get; private set; }

    public WindowTrackerService(int pollIntervalMs = 100)
    {
        _pollTimer = new Timer(pollIntervalMs);
        _pollTimer.Elapsed += OnPollTick;
    }

    public void Start()
    {
        _gameWindowHandle = User32.FindArknightsWindow();
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    private void OnPollTick(object? sender, ElapsedEventArgs e)
    {
        // Try to find window if not found yet
        if (!IsGameFound)
        {
            _gameWindowHandle = User32.FindArknightsWindow();
            if (_gameWindowHandle == IntPtr.Zero)
            {
                if (CurrentGameRect != null)
                {
                    CurrentGameRect = null;
                    GameWindowLost?.Invoke();
                }
                return;
            }
        }

        // Check focus
        var foreground = User32.GetForegroundWindow();
        var wasFocused = IsGameFocused;
        IsGameFocused = foreground == _gameWindowHandle;
        if (wasFocused != IsGameFocused)
            GameWindowFocusChanged?.Invoke(IsGameFocused);

        // Get window rect
        if (!User32.GetWindowRect(_gameWindowHandle, out var windowRect))
        {
            CurrentGameRect = null;
            GameWindowLost?.Invoke();
            _gameWindowHandle = IntPtr.Zero;
            return;
        }

        // Detect fullscreen or insufficient right-side space
        var screenWidth = GetPrimaryScreenWidth();
        var rightSpace = screenWidth - windowRect.Right;
        ShouldAttachInside = rightSpace < 320 || IsFullscreen(windowRect, screenWidth);

        CurrentGameRect = windowRect;
        GameWindowMoved?.Invoke(windowRect);
    }

    private static bool IsFullscreen(RECT rect, int screenWidth)
    {
        // Simple heuristic: window fills most of the screen width
        return rect.Left <= 0 && rect.Width >= screenWidth - 10;
    }

    private static int GetPrimaryScreenWidth()
    {
        // Use SystemParameters in WPF context; fallback to common resolution
        return 1920; // Will be replaced by actual SystemParameters in App layer
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ASPAssistant.Core
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ASPAssistant.Core/Services/WindowTrackerService.cs
git commit -m "feat: add WindowTrackerService for tracking Arknights game window position"
```

---

### Task 7: GameState Model

**Files:**
- Create: `src/ASPAssistant.Core/GameState/GameState.cs`
- Create: `src/ASPAssistant.Core/GameState/GameStateUpdater.cs`
- Test: `src/ASPAssistant.Tests/GameState/GameStateUpdaterTests.cs`

- [ ] **Step 1: Write failing test for GameStateUpdater**

`src/ASPAssistant.Tests/GameState/GameStateUpdaterTests.cs`:
```csharp
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.GameState;

public class GameStateUpdaterTests
{
    [Fact]
    public void UpdateShop_MatchesTrackedItems()
    {
        var state = new Core.GameState.GameState();
        var tracked = new List<TrackingEntry>
        {
            new() { Name = "角峰", Type = TrackingType.Operator },
            new() { Name = "不屈弹射器", Type = TrackingType.Equipment }
        };
        var shopItems = new List<ShopItem>
        {
            new() { Name = "隐现", Price = 8 },
            new() { Name = "角峰", Price = 4 },
            new() { Name = "不屈弹射器", Price = 3 }
        };

        GameStateUpdater.UpdateShopTracking(shopItems, tracked);

        shopItems.First(s => s.Name == "角峰").IsTracked.Should().BeTrue();
        shopItems.First(s => s.Name == "不屈弹射器").IsTracked.Should().BeTrue();
        shopItems.First(s => s.Name == "隐现").IsTracked.Should().BeFalse();
    }

    [Fact]
    public void ComputeCovenantCounts_FromFieldAndBench()
    {
        var operators = new List<Operator>
        {
            new() { Name = "角峰", CoreCovenant = "谢拉格", AdditionalCovenants = ["坚守"] },
            new() { Name = "深巡", CoreCovenant = "阿戈尔", AdditionalCovenants = [] },
            new() { Name = "红豆", CoreCovenant = "", AdditionalCovenants = ["不屈"] }
        };
        var owned = new List<(string Name, int Count)>
        {
            ("角峰", 2), ("深巡", 1), ("红豆", 1)
        };

        var counts = GameStateUpdater.ComputeCovenantCounts(owned, operators);

        counts["谢拉格"].Should().Be(2);  // 角峰×2
        counts["阿戈尔"].Should().Be(1);  // 深巡×1
        counts["不屈"].Should().Be(1);    // 红豆×1
        counts["坚守"].Should().Be(2);    // 角峰×2 (additional)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/ASPAssistant.Tests --filter "GameStateUpdaterTests" -v n
```

Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement GameState and GameStateUpdater**

`src/ASPAssistant.Core/GameState/GameState.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.GameState;

public partial class GameState : ObservableObject
{
    [ObservableProperty]
    private int _currentRound;

    [ObservableProperty]
    private int _gold;

    [ObservableProperty]
    private List<(string Name, int Count)> _fieldOperators = [];

    [ObservableProperty]
    private List<(string Name, int Count)> _benchOperators = [];

    [ObservableProperty]
    private List<ShopItem> _shopItems = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();
}
```

`src/ASPAssistant.Core/GameState/GameStateUpdater.cs`:
```csharp
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.GameState;

public static class GameStateUpdater
{
    /// <summary>
    /// Mark shop items that match tracking entries.
    /// </summary>
    public static void UpdateShopTracking(List<ShopItem> shopItems, List<TrackingEntry> tracked)
    {
        var trackedNames = new HashSet<string>(tracked.Select(t => t.Name));
        foreach (var item in shopItems)
        {
            item.IsTracked = trackedNames.Contains(item.Name);
        }
    }

    /// <summary>
    /// Compute covenant counts from owned operators.
    /// Each owned operator contributes its core covenant and additional covenants × count.
    /// </summary>
    public static Dictionary<string, int> ComputeCovenantCounts(
        List<(string Name, int Count)> ownedOperators,
        List<Operator> operatorDatabase)
    {
        var counts = new Dictionary<string, int>();
        var dbLookup = operatorDatabase.ToDictionary(o => o.Name);

        foreach (var (name, count) in ownedOperators)
        {
            if (!dbLookup.TryGetValue(name, out var op))
                continue;

            if (!string.IsNullOrEmpty(op.CoreCovenant))
            {
                counts.TryGetValue(op.CoreCovenant, out var existing);
                counts[op.CoreCovenant] = existing + count;
            }

            foreach (var covenant in op.AdditionalCovenants)
            {
                counts.TryGetValue(covenant, out var existing);
                counts[covenant] = existing + count;
            }
        }

        return counts;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test src/ASPAssistant.Tests --filter "GameStateUpdaterTests" -v n
```

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ASPAssistant.Core/GameState src/ASPAssistant.Tests/GameState
git commit -m "feat: add GameState model and GameStateUpdater with tracking match and covenant counting"
```

---

### Task 8: ViewModels

**Files:**
- Create: `src/ASPAssistant.Core/ViewModels/OperatorBrowseViewModel.cs`
- Create: `src/ASPAssistant.Core/ViewModels/EquipmentBrowseViewModel.cs`
- Create: `src/ASPAssistant.Core/ViewModels/TrackingViewModel.cs`
- Create: `src/ASPAssistant.Core/ViewModels/GameStateViewModel.cs`
- Test: `src/ASPAssistant.Tests/ViewModels/OperatorBrowseViewModelTests.cs`
- Test: `src/ASPAssistant.Tests/ViewModels/TrackingViewModelTests.cs`

- [ ] **Step 1: Write failing tests for OperatorBrowseViewModel**

`src/ASPAssistant.Tests/ViewModels/OperatorBrowseViewModelTests.cs`:
```csharp
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class OperatorBrowseViewModelTests
{
    private static List<Operator> TestOperators =>
    [
        new() { Name = "隐现", Tier = 6, CoreCovenant = "拉特兰", AdditionalCovenants = ["迅捷"],
                Normal = new() { TraitType = "作战能力", TraitDescription = "攻速+1" },
                Elite = new() { TraitType = "作战能力", TraitDescription = "攻速+2" } },
        new() { Name = "角峰", Tier = 4, CoreCovenant = "谢拉格", AdditionalCovenants = ["坚守"],
                Normal = new() { TraitType = "单次叠加", TraitDescription = "层数+2" },
                Elite = new() { TraitType = "单次叠加", TraitDescription = "层数+4" } },
        new() { Name = "惊蛰", Tier = 6, CoreCovenant = "炎", AdditionalCovenants = [],
                Normal = new() { TraitType = "单次叠加", TraitDescription = "炎层数" },
                Elite = new() { TraitType = "单次叠加", TraitDescription = "两倍炎层数" } }
    ];

    [Fact]
    public void FilterByTier_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);

        vm.SelectedTierFilter = 4;

        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("角峰");
    }

    [Fact]
    public void FilterByCovenant_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);

        vm.SelectedCovenantFilter = "炎";

        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("惊蛰");
    }

    [Fact]
    public void SearchByName_ReturnsMatchingOnly()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);

        vm.SearchText = "角";

        vm.FilteredOperators.Should().HaveCount(1);
        vm.FilteredOperators[0].Name.Should().Be("角峰");
    }

    [Fact]
    public void NoFilter_ReturnsAll()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(TestOperators);

        vm.FilteredOperators.Should().HaveCount(3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/ASPAssistant.Tests --filter "OperatorBrowseViewModelTests" -v n
```

Expected: FAIL — `OperatorBrowseViewModel` does not exist.

- [ ] **Step 3: Implement OperatorBrowseViewModel**

`src/ASPAssistant.Core/ViewModels/OperatorBrowseViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class OperatorBrowseViewModel : ObservableObject
{
    private List<Operator> _allOperators = [];

    [ObservableProperty]
    private ObservableCollection<Operator> _filteredOperators = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int? _selectedTierFilter;

    [ObservableProperty]
    private string? _selectedCovenantFilter;

    [ObservableProperty]
    private string? _selectedTraitTypeFilter;

    public void LoadOperators(List<Operator> operators)
    {
        _allOperators = operators;
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();
    partial void OnSelectedCovenantFilterChanged(string? value) => ApplyFilters();
    partial void OnSelectedTraitTypeFilterChanged(string? value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allOperators.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(o => o.Tier == SelectedTierFilter.Value);

        if (!string.IsNullOrEmpty(SelectedCovenantFilter))
            filtered = filtered.Where(o =>
                o.CoreCovenant == SelectedCovenantFilter ||
                o.AdditionalCovenants.Contains(SelectedCovenantFilter));

        if (!string.IsNullOrEmpty(SelectedTraitTypeFilter))
            filtered = filtered.Where(o =>
                o.Normal.TraitType == SelectedTraitTypeFilter ||
                o.Elite.TraitType == SelectedTraitTypeFilter);

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(o =>
                o.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredOperators = new ObservableCollection<Operator>(filtered);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test src/ASPAssistant.Tests --filter "OperatorBrowseViewModelTests" -v n
```

Expected: All PASS.

- [ ] **Step 5: Implement EquipmentBrowseViewModel**

`src/ASPAssistant.Core/ViewModels/EquipmentBrowseViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class EquipmentBrowseViewModel : ObservableObject
{
    private List<Equipment> _allEquipment = [];

    [ObservableProperty]
    private ObservableCollection<Equipment> _filteredEquipment = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int? _selectedTierFilter;

    public void LoadEquipment(List<Equipment> equipment)
    {
        _allEquipment = equipment;
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTierFilterChanged(int? value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allEquipment.AsEnumerable();

        if (SelectedTierFilter.HasValue)
            filtered = filtered.Where(e => e.Tier == SelectedTierFilter.Value);

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredEquipment = new ObservableCollection<Equipment>(filtered);
    }
}
```

- [ ] **Step 6: Write failing tests for TrackingViewModel, then implement**

`src/ASPAssistant.Tests/ViewModels/TrackingViewModelTests.cs`:
```csharp
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class TrackingViewModelTests
{
    [Fact]
    public void AddTracking_AddsEntry()
    {
        var vm = new TrackingViewModel();

        vm.AddTracking("角峰", TrackingType.Operator);

        vm.TrackedOperators.Should().ContainSingle(e => e.Name == "角峰");
    }

    [Fact]
    public void AddTracking_Duplicate_DoesNotAdd()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("角峰", TrackingType.Operator);

        vm.TrackedOperators.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveTracking_RemovesEntry()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);

        vm.RemoveTracking("角峰");

        vm.TrackedOperators.Should().BeEmpty();
    }

    [Fact]
    public void IsTracked_ReturnsTrueForTrackedItem()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);

        vm.IsTracked("角峰").Should().BeTrue();
        vm.IsTracked("隐现").Should().BeFalse();
    }

    [Fact]
    public void AllEntries_ReturnsBothTypes()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("不屈弹射器", TrackingType.Equipment);

        vm.AllEntries.Should().HaveCount(2);
        vm.TrackedOperators.Should().HaveCount(1);
        vm.TrackedEquipment.Should().HaveCount(1);
    }
}
```

`src/ASPAssistant.Core/ViewModels/TrackingViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedOperators = [];

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedEquipment = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];

    public void AddTracking(string name, TrackingType type)
    {
        if (IsTracked(name))
            return;

        var entry = new TrackingEntry { Name = name, Type = type };
        if (type == TrackingType.Operator)
            TrackedOperators.Add(entry);
        else
            TrackedEquipment.Add(entry);
    }

    public void RemoveTracking(string name)
    {
        var opEntry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (opEntry != null)
        {
            TrackedOperators.Remove(opEntry);
            return;
        }

        var eqEntry = TrackedEquipment.FirstOrDefault(e => e.Name == name);
        if (eqEntry != null)
            TrackedEquipment.Remove(eqEntry);
    }

    public bool IsTracked(string name)
    {
        return TrackedOperators.Any(e => e.Name == name)
            || TrackedEquipment.Any(e => e.Name == name);
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        foreach (var entry in entries)
        {
            if (entry.Type == TrackingType.Operator)
                TrackedOperators.Add(entry);
            else
                TrackedEquipment.Add(entry);
        }
    }
}
```

- [ ] **Step 7: Implement GameStateViewModel**

`src/ASPAssistant.Core/ViewModels/GameStateViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class GameStateViewModel : ObservableObject
{
    [ObservableProperty]
    private int _currentRound;

    [ObservableProperty]
    private int _gold;

    [ObservableProperty]
    private ObservableCollection<string> _fieldOperatorSummary = [];

    [ObservableProperty]
    private ObservableCollection<string> _benchOperatorSummary = [];

    [ObservableProperty]
    private ObservableCollection<ShopItem> _shopItems = [];

    [ObservableProperty]
    private ObservableCollection<string> _trackingAlerts = [];

    public void UpdateFromGameState(Core.GameState.GameState state)
    {
        CurrentRound = state.CurrentRound;
        Gold = state.Gold;

        FieldOperatorSummary = new ObservableCollection<string>(
            state.FieldOperators.Select(o => $"{o.Name}×{o.Count}"));

        BenchOperatorSummary = new ObservableCollection<string>(
            state.BenchOperators.Select(o => $"{o.Name}×{o.Count}"));

        ShopItems = new ObservableCollection<ShopItem>(state.ShopItems);

        TrackingAlerts = new ObservableCollection<string>(
            state.ShopItems.Where(s => s.IsTracked).Select(s => $"⚠ {s.Name} 在商店!"));
    }
}
```

- [ ] **Step 8: Run all ViewModel tests**

```bash
dotnet test src/ASPAssistant.Tests --filter "OperatorBrowseViewModelTests|TrackingViewModelTests" -v n
```

Expected: All PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ASPAssistant.Core/ViewModels src/ASPAssistant.Tests/ViewModels
git commit -m "feat: add OperatorBrowse, EquipmentBrowse, Tracking, and GameState ViewModels"
```

---

### Task 9: GameMode Interface and Garrison Protocol

**Files:**
- Create: `src/ASPAssistant.Core/GameModes/IGameMode.cs`
- Create: `src/ASPAssistant.Core/GameModes/GarrisonProtocol/GarrisonProtocolMode.cs`
- Create: `src/ASPAssistant.Core/GameModes/GarrisonProtocol/GarrisonOcrStrategy.cs`

- [ ] **Step 1: Define IGameMode interface**

`src/ASPAssistant.Core/GameModes/IGameMode.cs`:
```csharp
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.GameModes;

/// <summary>
/// Defines OCR regions relative to game window (as percentages 0.0-1.0).
/// </summary>
public record OcrRegionDefinition(
    string Name,
    double XPercent,
    double YPercent,
    double WidthPercent,
    double HeightPercent);

/// <summary>
/// Strategy for OCR recognition specific to a game mode.
/// </summary>
public interface IOcrStrategy
{
    /// <summary>
    /// Get the OCR regions to scan for this game mode.
    /// </summary>
    IReadOnlyList<OcrRegionDefinition> GetScanRegions();
}

/// <summary>
/// Represents a game mode that can be supported by the assistant.
/// </summary>
public interface IGameMode
{
    string Name { get; }
    IOcrStrategy OcrStrategy { get; }
}
```

- [ ] **Step 2: Implement GarrisonProtocolMode and OcrStrategy**

`src/ASPAssistant.Core/GameModes/GarrisonProtocol/GarrisonOcrStrategy.cs`:
```csharp
namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public class GarrisonOcrStrategy : IOcrStrategy
{
    // These percentages define where to scan in the game window.
    // Actual values need calibration with the Arknights PC client.
    // Placeholder values based on typical 16:9 layout.
    private static readonly List<OcrRegionDefinition> Regions =
    [
        new("Shop", XPercent: 0.15, YPercent: 0.35, WidthPercent: 0.70, HeightPercent: 0.35),
        new("Gold", XPercent: 0.85, YPercent: 0.02, WidthPercent: 0.12, HeightPercent: 0.05),
        new("Round", XPercent: 0.45, YPercent: 0.02, WidthPercent: 0.10, HeightPercent: 0.05),
        new("Field", XPercent: 0.10, YPercent: 0.72, WidthPercent: 0.80, HeightPercent: 0.15),
        new("Bench", XPercent: 0.10, YPercent: 0.88, WidthPercent: 0.80, HeightPercent: 0.10)
    ];

    public IReadOnlyList<OcrRegionDefinition> GetScanRegions() => Regions;
}
```

`src/ASPAssistant.Core/GameModes/GarrisonProtocol/GarrisonProtocolMode.cs`:
```csharp
namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public class GarrisonProtocolMode : IGameMode
{
    public string Name => "卫戍协议";
    public IOcrStrategy OcrStrategy { get; } = new GarrisonOcrStrategy();
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/ASPAssistant.Core
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ASPAssistant.Core/GameModes
git commit -m "feat: add IGameMode interface and GarrisonProtocol implementation with OCR region definitions"
```

---

### Task 10: OCR and Screen Capture Services

**Files:**
- Create: `src/ASPAssistant.Core/Services/ScreenCaptureService.cs`
- Create: `src/ASPAssistant.Core/Services/OcrScannerService.cs`

- [ ] **Step 1: Implement ScreenCaptureService**

`src/ASPAssistant.Core/Services/ScreenCaptureService.cs`:
```csharp
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Captures screenshots of the game window using MaaFramework.
/// This is a thin wrapper; actual MaaFramework initialization and
/// resource management happens during app startup.
/// </summary>
public class ScreenCaptureService
{
    private IntPtr _gameWindowHandle;

    public void SetTargetWindow(IntPtr handle)
    {
        _gameWindowHandle = handle;
    }

    /// <summary>
    /// Capture the game window's client area as a byte array (PNG).
    /// Uses MaaFramework's screen capture capability.
    /// Returns null if capture fails.
    /// </summary>
    public byte[]? CaptureScreen()
    {
        if (_gameWindowHandle == IntPtr.Zero)
            return null;

        // MaaFramework integration point:
        // In the actual implementation, this calls MaaFramework's
        // screencap API bound to the game window handle.
        // The exact API depends on MaaFramework .NET binding version.
        //
        // Pseudocode:
        // var controller = MaaController.CreateWin32(handle, screencapMethod);
        // controller.Screencap();
        // return controller.GetImage();

        // Stub: actual implementation requires MaaFramework runtime
        return null;
    }
}
```

- [ ] **Step 2: Implement OcrScannerService**

`src/ASPAssistant.Core/Services/OcrScannerService.cs`:
```csharp
using System.Timers;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Periodically scans the game screen via OCR and updates GameState.
/// </summary>
public class OcrScannerService : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly Timer _scanTimer;
    private readonly Core.GameState.GameState _gameState;

    public event Action<Core.GameState.GameState>? GameStateUpdated;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        Core.GameState.GameState gameState,
        int intervalSeconds = 3)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _gameState = gameState;
        _scanTimer = new Timer(intervalSeconds * 1000);
        _scanTimer.Elapsed += OnScanTick;
    }

    public void Start() => _scanTimer.Start();
    public void Stop() => _scanTimer.Stop();

    private void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        var screenshot = _captureService.CaptureScreen();
        if (screenshot == null)
            return;

        var regions = _ocrStrategy.GetScanRegions();

        // MaaFramework integration point:
        // For each region, crop the screenshot and run OCR.
        //
        // Pseudocode:
        // foreach (var region in regions)
        // {
        //     var cropped = CropImage(screenshot, region);
        //     var result = MaaOcr.Recognize(cropped);
        //     ParseAndUpdateGameState(region.Name, result);
        // }

        // After OCR completes, parse results and update GameState
        // ParseShopItems(ocrResults) → _gameState.ShopItems
        // ParseGold(ocrResults) → _gameState.Gold
        // ParseRound(ocrResults) → _gameState.CurrentRound
        // etc.

        GameStateUpdated?.Invoke(_gameState);
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/ASPAssistant.Core
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ASPAssistant.Core/Services
git commit -m "feat: add ScreenCaptureService and OcrScannerService with MaaFramework integration points"
```

---

### Task 11: WPF Resources and Converters

**Files:**
- Create: `src/ASPAssistant.App/Converters/TierToRomanConverter.cs`
- Create: `src/ASPAssistant.App/Resources/Styles.xaml`

- [ ] **Step 1: Implement TierToRomanConverter**

`src/ASPAssistant.App/Converters/TierToRomanConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

public class TierToRomanConverter : IValueConverter
{
    private static readonly string[] RomanNumerals = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int tier && tier >= 1 && tier <= 6)
            return RomanNumerals[tier];
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Create dark theme Styles.xaml**

`src/ASPAssistant.App/Resources/Styles.xaml`:
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Colors -->
    <Color x:Key="PanelBackgroundColor">#FF1A1A2E</Color>
    <Color x:Key="CardBackgroundColor">#FF252540</Color>
    <Color x:Key="AccentColor">#FF4FC3F7</Color>
    <Color x:Key="TrackingColor">#FF4CAF50</Color>
    <Color x:Key="TextPrimaryColor">#FFE0E0E0</Color>
    <Color x:Key="TextSecondaryColor">#FF8899AA</Color>
    <Color x:Key="BorderColor">#FF334155</Color>

    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource PanelBackgroundColor}" />
    <SolidColorBrush x:Key="CardBackgroundBrush" Color="{StaticResource CardBackgroundColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="TrackingBrush" Color="{StaticResource TrackingColor}" />
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimaryColor}" />
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextSecondaryColor}" />
    <SolidColorBrush x:Key="BorderBrush" Color="{StaticResource BorderColor}" />

    <!-- Card style for operator/equipment items -->
    <Style x:Key="ItemCardBorder" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource CardBackgroundBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="4" />
        <Setter Property="Padding" Value="8" />
        <Setter Property="Margin" Value="0,0,0,6" />
    </Style>

    <!-- Toggle button style for Normal/Elite -->
    <Style x:Key="VariantToggleButton" TargetType="ToggleButton">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="4,2" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Cursor" Value="Hand" />
        <Style.Triggers>
            <Trigger Property="IsChecked" Value="True">
                <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
                <Setter Property="FontWeight" Value="Bold" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Tracking button -->
    <Style x:Key="TrackingButton" TargetType="Button">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
        <Setter Property="Padding" Value="6,2" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Cursor" Value="Hand" />
    </Style>

    <!-- Tab control style -->
    <Style x:Key="DarkTabControl" TargetType="TabControl">
        <Setter Property="Background" Value="{StaticResource PanelBackgroundBrush}" />
        <Setter Property="BorderThickness" Value="0" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/ASPAssistant.App
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ASPAssistant.App/Converters src/ASPAssistant.App/Resources
git commit -m "feat: add TierToRomanConverter and dark theme styles"
```

---

### Task 12: Operator and Equipment Card Controls

**Files:**
- Create: `src/ASPAssistant.App/Controls/OperatorCard.xaml`
- Create: `src/ASPAssistant.App/Controls/OperatorCard.xaml.cs`
- Create: `src/ASPAssistant.App/Controls/EquipmentCard.xaml`
- Create: `src/ASPAssistant.App/Controls/EquipmentCard.xaml.cs`

- [ ] **Step 1: Create OperatorCard control**

`src/ASPAssistant.App/Controls/OperatorCard.xaml`:
```xml
<UserControl x:Class="ASPAssistant.App.Controls.OperatorCard"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:ASPAssistant.App.Converters">
    <UserControl.Resources>
        <converters:TierToRomanConverter x:Key="TierToRoman" />
    </UserControl.Resources>

    <Border Style="{StaticResource ItemCardBorder}">
        <StackPanel>
            <!-- Row 1: Tier + Name + Normal/Elite toggle -->
            <DockPanel>
                <TextBlock DockPanel.Dock="Left"
                           Text="{Binding Tier, Converter={StaticResource TierToRoman}}"
                           Foreground="{StaticResource AccentBrush}"
                           FontWeight="Bold" FontSize="13" Margin="0,0,6,0" />
                <TextBlock DockPanel.Dock="Left"
                           Text="{Binding Name}"
                           Foreground="{StaticResource TextPrimaryBrush}"
                           FontWeight="Bold" FontSize="13" />
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <ToggleButton x:Name="NormalToggle" Content="普通"
                                  IsChecked="True"
                                  Style="{StaticResource VariantToggleButton}"
                                  Click="OnNormalClick" />
                    <TextBlock Text="|" Foreground="{StaticResource TextSecondaryBrush}"
                               Margin="2,0" VerticalAlignment="Center" FontSize="11" />
                    <ToggleButton x:Name="EliteToggle" Content="精锐"
                                  Style="{StaticResource VariantToggleButton}"
                                  Click="OnEliteClick" />
                </StackPanel>
            </DockPanel>

            <!-- Row 2: Covenants -->
            <WrapPanel Margin="0,4,0,0">
                <TextBlock Text="{Binding CoreCovenant}"
                           Foreground="{StaticResource AccentBrush}"
                           FontSize="12" Margin="0,0,8,0" />
                <ItemsControl ItemsSource="{Binding AdditionalCovenants}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding}"
                                       Foreground="{StaticResource TextSecondaryBrush}"
                                       FontSize="12" Margin="0,0,6,0" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </WrapPanel>

            <!-- Row 3: Trait description (switches based on Normal/Elite) -->
            <StackPanel Margin="0,4,0,0">
                <TextBlock x:Name="TraitTypeText"
                           FontSize="11" Foreground="{StaticResource TrackingBrush}" />
                <TextBlock x:Name="TraitDescriptionText"
                           FontSize="12" Foreground="{StaticResource TextPrimaryBrush}"
                           TextWrapping="Wrap" />
            </StackPanel>

            <!-- Row 4: Tracking button -->
            <Button x:Name="TrackButton" HorizontalAlignment="Right"
                    Style="{StaticResource TrackingButton}"
                    Margin="0,4,0,0" Click="OnTrackClick" />
        </StackPanel>
    </Border>
</UserControl>
```

`src/ASPAssistant.App/Controls/OperatorCard.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

public partial class OperatorCard : UserControl
{
    public static readonly RoutedEvent TrackingToggledEvent =
        EventManager.RegisterRoutedEvent(
            "TrackingToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(OperatorCard));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    public bool IsTracked { get; set; }
    private bool _showElite;

    public OperatorCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _showElite = false;
        UpdateVariantDisplay();
        UpdateTrackButton();
    }

    private void OnNormalClick(object sender, RoutedEventArgs e)
    {
        _showElite = false;
        NormalToggle.IsChecked = true;
        EliteToggle.IsChecked = false;
        UpdateVariantDisplay();
    }

    private void OnEliteClick(object sender, RoutedEventArgs e)
    {
        _showElite = true;
        NormalToggle.IsChecked = false;
        EliteToggle.IsChecked = true;
        UpdateVariantDisplay();
    }

    private void UpdateVariantDisplay()
    {
        if (DataContext is not Operator op) return;
        var variant = _showElite ? op.Elite : op.Normal;
        TraitTypeText.Text = variant.TraitType;
        TraitDescriptionText.Text = variant.TraitDescription;
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTracked ? "★ 追踪中" : "☆ 追踪";
    }
}
```

- [ ] **Step 2: Create EquipmentCard control**

`src/ASPAssistant.App/Controls/EquipmentCard.xaml`:
```xml
<UserControl x:Class="ASPAssistant.App.Controls.EquipmentCard"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:ASPAssistant.App.Converters">
    <UserControl.Resources>
        <converters:TierToRomanConverter x:Key="TierToRoman" />
    </UserControl.Resources>

    <Border Style="{StaticResource ItemCardBorder}">
        <StackPanel>
            <!-- Row 1: Tier + Name + Toggle -->
            <DockPanel>
                <TextBlock DockPanel.Dock="Left"
                           Text="{Binding Tier, Converter={StaticResource TierToRoman}}"
                           Foreground="{StaticResource AccentBrush}"
                           FontWeight="Bold" FontSize="13" Margin="0,0,6,0" />
                <TextBlock DockPanel.Dock="Left"
                           Text="{Binding Name}"
                           Foreground="{StaticResource TextPrimaryBrush}"
                           FontWeight="Bold" FontSize="13" />
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <ToggleButton x:Name="NormalToggle" Content="普通"
                                  IsChecked="True"
                                  Style="{StaticResource VariantToggleButton}"
                                  Click="OnNormalClick" />
                    <TextBlock Text="|" Foreground="{StaticResource TextSecondaryBrush}"
                               Margin="2,0" VerticalAlignment="Center" FontSize="11" />
                    <ToggleButton x:Name="EliteToggle" Content="精锐"
                                  Style="{StaticResource VariantToggleButton}"
                                  Click="OnEliteClick" />
                </StackPanel>
            </DockPanel>

            <!-- Row 2: Effect description -->
            <TextBlock x:Name="EffectText" Margin="0,4,0,0"
                       FontSize="12" Foreground="{StaticResource TextPrimaryBrush}"
                       TextWrapping="Wrap" />

            <!-- Row 3: Tracking button -->
            <Button x:Name="TrackButton" HorizontalAlignment="Right"
                    Style="{StaticResource TrackingButton}"
                    Margin="0,4,0,0" Click="OnTrackClick" />
        </StackPanel>
    </Border>
</UserControl>
```

`src/ASPAssistant.App/Controls/EquipmentCard.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

public partial class EquipmentCard : UserControl
{
    public static readonly RoutedEvent TrackingToggledEvent =
        EventManager.RegisterRoutedEvent(
            "TrackingToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(EquipmentCard));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    public bool IsTracked { get; set; }
    private bool _showElite;

    public EquipmentCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _showElite = false;
        UpdateVariantDisplay();
        UpdateTrackButton();
    }

    private void OnNormalClick(object sender, RoutedEventArgs e)
    {
        _showElite = false;
        NormalToggle.IsChecked = true;
        EliteToggle.IsChecked = false;
        UpdateVariantDisplay();
    }

    private void OnEliteClick(object sender, RoutedEventArgs e)
    {
        _showElite = true;
        NormalToggle.IsChecked = false;
        EliteToggle.IsChecked = true;
        UpdateVariantDisplay();
    }

    private void UpdateVariantDisplay()
    {
        if (DataContext is not Equipment eq) return;
        var variant = _showElite ? eq.Elite : eq.Normal;
        EffectText.Text = variant.EffectDescription;
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTracked ? "★ 追踪中" : "☆ 追踪";
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/ASPAssistant.App
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ASPAssistant.App/Controls
git commit -m "feat: add OperatorCard and EquipmentCard controls with Normal/Elite toggle"
```

---

### Task 13: Tab Views (OperatorBrowse, EquipmentBrowse, Tracking)

**Files:**
- Create: `src/ASPAssistant.App/Views/OperatorBrowseView.xaml` / `.cs`
- Create: `src/ASPAssistant.App/Views/EquipmentBrowseView.xaml` / `.cs`
- Create: `src/ASPAssistant.App/Views/TrackingView.xaml` / `.cs`

- [ ] **Step 1: Create OperatorBrowseView**

`src/ASPAssistant.App/Views/OperatorBrowseView.xaml`:
```xml
<UserControl x:Class="ASPAssistant.App.Views.OperatorBrowseView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:ASPAssistant.App.Controls"
             Background="{StaticResource PanelBackgroundBrush}">

    <DockPanel>
        <!-- Search box -->
        <TextBox DockPanel.Dock="Top" Margin="8"
                 Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                 Background="#FF2A2A40" Foreground="{StaticResource TextPrimaryBrush}"
                 BorderBrush="{StaticResource BorderBrush}" Padding="6,4">
            <TextBox.Style>
                <Style TargetType="TextBox">
                    <Style.Triggers>
                        <Trigger Property="Text" Value="">
                            <Setter Property="Tag" Value="搜索干员名称..." />
                        </Trigger>
                    </Style.Triggers>
                </Style>
            </TextBox.Style>
        </TextBox>

        <!-- Filters (collapsible) -->
        <Expander DockPanel.Dock="Top" Header="筛选" Margin="8,0"
                  Foreground="{StaticResource TextSecondaryBrush}" IsExpanded="False">
            <StackPanel Margin="0,4">
                <!-- Tier filter -->
                <TextBlock Text="等阶:" Foreground="{StaticResource TextSecondaryBrush}"
                           FontSize="11" Margin="0,2" />
                <WrapPanel x:Name="TierFilterPanel" Margin="0,2" />

                <!-- Covenant filter -->
                <TextBlock Text="盟约:" Foreground="{StaticResource TextSecondaryBrush}"
                           FontSize="11" Margin="0,4,0,2" />
                <WrapPanel x:Name="CovenantFilterPanel" Margin="0,2" />

                <!-- Trait type filter -->
                <TextBlock Text="词条:" Foreground="{StaticResource TextSecondaryBrush}"
                           FontSize="11" Margin="0,4,0,2" />
                <WrapPanel x:Name="TraitFilterPanel" Margin="0,2" />
            </StackPanel>
        </Expander>

        <!-- Operator list -->
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding FilteredOperators}" Margin="8">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <controls:OperatorCard TrackingToggled="OnOperatorTrackingToggled" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/ASPAssistant.App/Views/OperatorBrowseView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class OperatorBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;

    public OperatorBrowseView()
    {
        InitializeComponent();
    }

    private void OnOperatorTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
        {
            TrackingRequested?.Invoke(op.Name, TrackingType.Operator);
        }
    }
}
```

- [ ] **Step 2: Create EquipmentBrowseView**

`src/ASPAssistant.App/Views/EquipmentBrowseView.xaml`:
```xml
<UserControl x:Class="ASPAssistant.App.Views.EquipmentBrowseView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:ASPAssistant.App.Controls"
             Background="{StaticResource PanelBackgroundBrush}">

    <DockPanel>
        <TextBox DockPanel.Dock="Top" Margin="8"
                 Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                 Background="#FF2A2A40" Foreground="{StaticResource TextPrimaryBrush}"
                 BorderBrush="{StaticResource BorderBrush}" Padding="6,4" />

        <Expander DockPanel.Dock="Top" Header="筛选" Margin="8,0"
                  Foreground="{StaticResource TextSecondaryBrush}" IsExpanded="False">
            <StackPanel Margin="0,4">
                <TextBlock Text="等阶:" Foreground="{StaticResource TextSecondaryBrush}"
                           FontSize="11" />
                <WrapPanel x:Name="TierFilterPanel" Margin="0,2" />
            </StackPanel>
        </Expander>

        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding FilteredEquipment}" Margin="8">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <controls:EquipmentCard TrackingToggled="OnEquipmentTrackingToggled" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/ASPAssistant.App/Views/EquipmentBrowseView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

public partial class EquipmentBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;

    public EquipmentBrowseView()
    {
        InitializeComponent();
    }

    private void OnEquipmentTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCard card && card.DataContext is Equipment eq)
        {
            TrackingRequested?.Invoke(eq.Name, TrackingType.Equipment);
        }
    }
}
```

- [ ] **Step 3: Create TrackingView**

`src/ASPAssistant.App/Views/TrackingView.xaml`:
```xml
<UserControl x:Class="ASPAssistant.App.Views.TrackingView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource PanelBackgroundBrush}">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="8">
            <!-- Covenant counts -->
            <TextBlock Text="当前盟约持有" FontSize="12" FontWeight="Bold"
                       Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4" />
            <Border Background="{StaticResource CardBackgroundBrush}"
                    BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
                    CornerRadius="4" Padding="8" Margin="0,0,0,12">
                <ItemsControl x:Name="CovenantCountsPanel">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                </ItemsControl>
            </Border>

            <!-- Tracked operators -->
            <TextBlock FontSize="12" FontWeight="Bold"
                       Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4">
                <Run Text="追踪中的干员 (" />
                <Run Text="{Binding TrackedOperators.Count, Mode=OneWay}" />
                <Run Text=")" />
            </TextBlock>
            <ItemsControl ItemsSource="{Binding TrackedOperators}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource ItemCardBorder}">
                            <DockPanel>
                                <Button DockPanel.Dock="Right" Content="✕"
                                        Tag="{Binding Name}"
                                        Style="{StaticResource TrackingButton}"
                                        Click="OnRemoveTracking" />
                                <TextBlock Text="{Binding Name}"
                                           Foreground="{StaticResource TextPrimaryBrush}"
                                           FontSize="12" VerticalAlignment="Center" />
                            </DockPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Tracked equipment -->
            <TextBlock FontSize="12" FontWeight="Bold"
                       Foreground="{StaticResource TextPrimaryBrush}" Margin="0,12,0,4">
                <Run Text="追踪中的装备 (" />
                <Run Text="{Binding TrackedEquipment.Count, Mode=OneWay}" />
                <Run Text=")" />
            </TextBlock>
            <ItemsControl ItemsSource="{Binding TrackedEquipment}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource ItemCardBorder}">
                            <DockPanel>
                                <Button DockPanel.Dock="Right" Content="✕"
                                        Tag="{Binding Name}"
                                        Style="{StaticResource TrackingButton}"
                                        Click="OnRemoveTracking" />
                                <TextBlock Text="{Binding Name}"
                                           Foreground="{StaticResource TextPrimaryBrush}"
                                           FontSize="12" VerticalAlignment="Center" />
                            </DockPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Game state panel -->
            <TextBlock Text="游戏状态（实时 OCR）" FontSize="12" FontWeight="Bold"
                       Foreground="{StaticResource TextPrimaryBrush}" Margin="0,12,0,4" />
            <Border Background="{StaticResource CardBackgroundBrush}"
                    BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
                    CornerRadius="4" Padding="8">
                <StackPanel DataContext="{Binding GameState}">
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                        <TextBlock Foreground="{StaticResource TextSecondaryBrush}" FontSize="12">
                            <Run Text="回合: " /><Run Text="{Binding CurrentRound}" />
                        </TextBlock>
                        <TextBlock Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" Margin="16,0,0,0">
                            <Run Text="金币: " /><Run Text="{Binding Gold}" />
                        </TextBlock>
                    </StackPanel>
                    <!-- Tracking alerts -->
                    <ItemsControl ItemsSource="{Binding TrackingAlerts}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding}"
                                           Foreground="{StaticResource TrackingBrush}"
                                           FontSize="12" FontWeight="Bold" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`src/ASPAssistant.App/Views/TrackingView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;

namespace ASPAssistant.App.Views;

public partial class TrackingView : UserControl
{
    public event Action<string>? RemoveTrackingRequested;

    public TrackingView()
    {
        InitializeComponent();
    }

    private void OnRemoveTracking(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            RemoveTrackingRequested?.Invoke(name);
        }
    }
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build src/ASPAssistant.App
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ASPAssistant.App/Views
git commit -m "feat: add OperatorBrowse, EquipmentBrowse, and Tracking tab views"
```

---

### Task 14: SidePanel Window

**Files:**
- Create: `src/ASPAssistant.App/Windows/SidePanelWindow.xaml`
- Create: `src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs`

- [ ] **Step 1: Create SidePanelWindow**

`src/ASPAssistant.App/Windows/SidePanelWindow.xaml`:
```xml
<Window x:Class="ASPAssistant.App.Windows.SidePanelWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:ASPAssistant.App.Views"
        Title="ASP Assistant"
        Width="310" MinWidth="280" MaxWidth="400"
        WindowStyle="ToolWindow"
        ShowInTaskbar="True"
        Background="{StaticResource PanelBackgroundBrush}">

    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/Resources/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Window.Resources>

    <DockPanel>
        <!-- Title bar -->
        <Border DockPanel.Dock="Top" Background="#FF12122A" Padding="8,6"
                MouseLeftButtonDown="OnTitleBarDrag">
            <DockPanel>
                <TextBlock Text="☰ ASP Assistant" Foreground="{StaticResource TextPrimaryBrush}"
                           FontSize="13" FontWeight="Bold" VerticalAlignment="Center" />
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal"
                            HorizontalAlignment="Right">
                    <Button Content="─" Click="OnMinimize" Padding="6,2"
                            Background="Transparent" Foreground="{StaticResource TextSecondaryBrush}"
                            BorderThickness="0" Cursor="Hand" />
                </StackPanel>
            </DockPanel>
        </Border>

        <!-- Tab control -->
        <TabControl Style="{StaticResource DarkTabControl}">
            <TabItem Header="干员">
                <views:OperatorBrowseView x:Name="OperatorView" />
            </TabItem>
            <TabItem Header="装备">
                <views:EquipmentBrowseView x:Name="EquipmentView" />
            </TabItem>
            <TabItem Header="追踪">
                <views:TrackingView x:Name="TrackingView" />
            </TabItem>
        </TabControl>
    </DockPanel>
</Window>
```

`src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow : Window
{
    public OperatorBrowseViewModel OperatorBrowseVm { get; }
    public EquipmentBrowseViewModel EquipmentBrowseVm { get; }
    public TrackingViewModel TrackingVm { get; }
    public GameStateViewModel GameStateVm { get; }

    public SidePanelWindow(
        OperatorBrowseViewModel operatorVm,
        EquipmentBrowseViewModel equipmentVm,
        TrackingViewModel trackingVm,
        GameStateViewModel gameStateVm)
    {
        OperatorBrowseVm = operatorVm;
        EquipmentBrowseVm = equipmentVm;
        TrackingVm = trackingVm;
        GameStateVm = gameStateVm;

        InitializeComponent();

        OperatorView.DataContext = operatorVm;
        EquipmentView.DataContext = equipmentVm;
        TrackingView.DataContext = trackingVm;

        // Wire up tracking events
        OperatorView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        EquipmentView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        TrackingView.RemoveTrackingRequested += name => trackingVm.RemoveTracking(name);
    }

    /// <summary>
    /// Position the side panel relative to the game window.
    /// Called by WindowTracker when the game window moves.
    /// </summary>
    public void UpdatePosition(RECT gameRect, bool attachInside)
    {
        if (attachInside)
        {
            // Attach inside the game window (right edge)
            Left = gameRect.Right - Width;
            Top = gameRect.Top;
            Height = gameRect.Height;
        }
        else
        {
            // Attach outside the game window (to the right)
            Left = gameRect.Right;
            Top = gameRect.Top;
            Height = gameRect.Height;
        }
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ASPAssistant.App
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ASPAssistant.App/Windows/SidePanelWindow.xaml src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs
git commit -m "feat: add SidePanelWindow with tab navigation and game window position tracking"
```

---

### Task 15: Overlay Window

**Files:**
- Create: `src/ASPAssistant.App/Windows/OverlayWindow.xaml`
- Create: `src/ASPAssistant.App/Windows/OverlayWindow.xaml.cs`

- [ ] **Step 1: Create OverlayWindow**

`src/ASPAssistant.App/Windows/OverlayWindow.xaml`:
```xml
<Window x:Class="ASPAssistant.App.Windows.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ASPOverlay"
        AllowsTransparency="True"
        WindowStyle="None"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize">

    <Canvas x:Name="OverlayCanvas" />
</Window>
```

`src/ASPAssistant.App/Windows/OverlayWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class OverlayWindow : Window
{
    private const double MarkerSize = 28;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Make window click-through using WS_EX_TRANSPARENT
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
            exStyle | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Position the overlay to exactly cover the game window's client area.
    /// </summary>
    public void UpdatePosition(RECT gameClientRect)
    {
        Left = gameClientRect.Left;
        Top = gameClientRect.Top;
        Width = gameClientRect.Width;
        Height = gameClientRect.Height;
    }

    /// <summary>
    /// Update the tracking markers on the overlay canvas.
    /// Called when GameState is updated with new shop data.
    /// </summary>
    public void UpdateMarkers(List<ShopItem> trackedShopItems, RECT gameClientRect)
    {
        OverlayCanvas.Children.Clear();

        foreach (var item in trackedShopItems)
        {
            if (!item.IsTracked)
                continue;

            // Convert OCR pixel coordinates to canvas coordinates
            // OCR region is relative to the screenshot (= game client area)
            var markerX = item.OcrRegion.X + item.OcrRegion.Width - MarkerSize / 2;
            var markerY = item.OcrRegion.Y - MarkerSize / 2;

            // Clamp to canvas bounds
            markerX = Math.Max(0, Math.Min(markerX, ActualWidth - MarkerSize));
            markerY = Math.Max(0, Math.Min(markerY, ActualHeight - MarkerSize));

            var marker = CreateMarker();
            Canvas.SetLeft(marker, markerX);
            Canvas.SetTop(marker, markerY);
            OverlayCanvas.Children.Add(marker);
        }
    }

    private static Grid CreateMarker()
    {
        var grid = new Grid
        {
            Width = MarkerSize,
            Height = MarkerSize
        };

        // Green circle background
        var ellipse = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                ShadowDepth = 2,
                Opacity = 0.4
            }
        };

        // Star text
        var star = new TextBlock
        {
            Text = "★",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(ellipse);
        grid.Children.Add(star);
        return grid;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/ASPAssistant.App
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ASPAssistant.App/Windows/OverlayWindow.xaml src/ASPAssistant.App/Windows/OverlayWindow.xaml.cs
git commit -m "feat: add transparent OverlayWindow with click-through and tracking markers"
```

---

### Task 16: App Startup and Wiring

**Files:**
- Modify: `src/ASPAssistant.App/App.xaml`
- Modify: `src/ASPAssistant.App/App.xaml.cs`
- Create: `data/operators.json` (placeholder)
- Create: `data/equipment.json` (placeholder)

- [ ] **Step 1: Update App.xaml to include styles**

`src/ASPAssistant.App/App.xaml`:
```xml
<Application x:Class="ASPAssistant.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/Resources/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Implement App.xaml.cs startup**

`src/ASPAssistant.App/App.xaml.cs`:
```csharp
using System.IO;
using System.Windows;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App;

public partial class App : Application
{
    private WindowTrackerService? _windowTracker;
    private OcrScannerService? _ocrScanner;
    private SidePanelWindow? _sidePanel;
    private OverlayWindow? _overlay;
    private SettingsManager? _settingsManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Paths
        var appDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ASPAssistant");

        // Data layer
        var dataStore = new JsonDataStore(dataDir);
        _settingsManager = new SettingsManager(settingsDir);

        // Load data
        var operators = await dataStore.LoadOperatorsAsync();
        var equipment = await dataStore.LoadEquipmentAsync();
        var trackingEntries = await _settingsManager.LoadTrackingEntriesAsync();

        // ViewModels
        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators);

        var equipmentVm = new EquipmentBrowseViewModel();
        equipmentVm.LoadEquipment(equipment);

        var trackingVm = new TrackingViewModel();
        trackingVm.LoadEntries(trackingEntries);

        var gameStateVm = new GameStateViewModel();

        // GameState
        var gameState = new Core.GameState.GameState();

        // Game mode
        var garrisonMode = new GarrisonProtocolMode();

        // Services
        var captureService = new ScreenCaptureService();
        _ocrScanner = new OcrScannerService(
            captureService, garrisonMode.OcrStrategy, gameState,
            _settingsManager.OcrScanIntervalSeconds);

        _windowTracker = new WindowTrackerService();

        // Windows
        _sidePanel = new SidePanelWindow(operatorVm, equipmentVm, trackingVm, gameStateVm);
        _overlay = new OverlayWindow();

        // Wire window tracker → window positioning
        _windowTracker.GameWindowMoved += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside);

                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue)
                    _overlay.UpdatePosition(clientRect.Value);
            });
        };

        _windowTracker.GameWindowLost += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _overlay.Hide();
            });
        };

        // Wire OCR scanner → GameState → overlay markers
        _ocrScanner.GameStateUpdated += state =>
        {
            Dispatcher.Invoke(() =>
            {
                Core.GameState.GameStateUpdater.UpdateShopTracking(
                    state.ShopItems, trackingVm.AllEntries.ToList());

                state.CovenantCounts = Core.GameState.GameStateUpdater.ComputeCovenantCounts(
                    [.. state.FieldOperators, .. state.BenchOperators], operators);

                trackingVm.CovenantCounts = state.CovenantCounts;
                gameStateVm.UpdateFromGameState(state);

                var trackedItems = state.ShopItems.Where(s => s.IsTracked).ToList();
                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue && trackedItems.Count > 0)
                {
                    _overlay.Show();
                    _overlay.UpdateMarkers(state.ShopItems, clientRect.Value);
                }
                else
                {
                    _overlay.OverlayCanvas.Children.Clear();
                }
            });
        };

        // Auto-save tracking changes
        trackingVm.TrackedOperators.CollectionChanged += async (_, _) =>
            await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList());
        trackingVm.TrackedEquipment.CollectionChanged += async (_, _) =>
            await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList());

        // Show windows and start services
        _sidePanel.Show();
        _overlay.Show();
        _windowTracker.Start();
        _ocrScanner.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 3: Create placeholder data files**

`data/operators.json`:
```json
{
  "operators": []
}
```

`data/equipment.json`:
```json
{
  "equipment": []
}
```

Add to App csproj so data files are copied to output:
```xml
<ItemGroup>
  <Content Include="..\..\data\**\*.json" Link="data\%(RecursiveDir)%(Filename)%(Extension)">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 4: Build the entire solution**

```bash
dotnet build ASPAssistant.sln
```

Expected: Build succeeded.

- [ ] **Step 5: Run all tests**

```bash
dotnet test ASPAssistant.sln -v n
```

Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ASPAssistant.App/App.xaml src/ASPAssistant.App/App.xaml.cs data/
git commit -m "feat: wire up App startup with all services, windows, and data loading"
```

---

### Task 17: Final Verification

- [ ] **Step 1: Run full test suite**

```bash
dotnet test ASPAssistant.sln -v n --logger "console;verbosity=detailed"
```

Expected: All tests pass.

- [ ] **Step 2: Manual testing on Windows with Arknights PC**

1. Place real operator/equipment JSON data in `data/` directory
2. Launch `dotnet run --project src/ASPAssistant.App`
3. Open Arknights PC client
4. Verify: SidePanel appears on the right side of the game window
5. Verify: SidePanel follows game window when moved
6. Verify: Operator/equipment cards display correctly with filters working
7. Verify: Normal/Elite toggle works per card
8. Verify: Adding/removing tracking items works and persists
9. Verify: In fullscreen mode, SidePanel attaches inside
10. Verify: OCR scans and overlay markers appear on tracked shop items (requires MaaFramework runtime)

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "chore: final cleanup and verification"
```
