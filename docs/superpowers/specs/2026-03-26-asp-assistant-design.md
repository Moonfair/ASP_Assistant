# ASPAssistant — 明日方舟卫戍协议辅助工具设计文档

## 1. 概述

### 1.1 项目背景

明日方舟PC端的"卫戍协议"玩法中，玩家需要在商店中招募干员和购买装备来构建阵容。由于干员/装备种类繁多、特质效果各异，玩家在对局中需要频繁查阅数据来做出决策。ASPAssistant 旨在提供一个非侵入式的辅助工具，帮助玩家：

1. **快速浏览干员和装备数据** — 悬浮式侧面板，无需切出游戏
2. **追踪关键干员/装备** — 当追踪目标出现在商店时自动标记，避免遗漏

### 1.2 设计原则

- **可扩展框架** — 首期仅支持卫戍协议，但架构支持后续扩展到其他玩法
- **非侵入式** — 覆盖层点击穿透，不影响游戏操作
- **参考成熟方案** — 架构参考 Hearthstone Deck Tracker（HDT）的双窗口模式

### 1.3 技术栈

- **语言/框架**: C# + WPF (.NET 8)
- **OCR/截图**: MaaFramework (.NET Binding, NuGet: Maa.Framework)
- **窗口追踪**: Win32 User32 P/Invoke
- **数据格式**: JSON（用户手动提供干员/装备数据文件）
- **构建工具**: dotnet CLI / Visual Studio

---

## 2. 系统架构

### 2.1 双窗口架构

采用 HDT 风格的双窗口分离架构：

```
ASPAssistant.App
├── SidePanel Window     — 独立 WPF 窗口，贴附在游戏窗口侧面
│   ├── 干员浏览 Tab     — 搜索、筛选、查看干员数据
│   ├── 装备浏览 Tab     — 搜索、筛选、查看装备数据
│   └── 追踪管理 Tab     — 追踪列表、盟约计数、游戏状态
├── Overlay Window       — 透明 WPF 窗口，覆盖在游戏画面上
│   └── Canvas           — 绘制商店追踪标记（★ 角标）
├── Core Engine
│   ├── WindowTracker    — User32 P/Invoke 追踪游戏窗口位置
│   ├── ScreenCapture    — MaaFramework 截图
│   └── OcrScanner       — MaaFramework OCR 定时扫描
├── Shared ViewModels    — MVVM，SidePanel 和 Overlay 共享状态
│   ├── OperatorViewModel
│   ├── EquipmentViewModel
│   ├── TrackingViewModel
│   └── GameStateViewModel
├── GameState            — OCR 提取的实时游戏数据
│   ├── 场上干员 (名称×数量)
│   ├── 备战区干员 (名称×数量)
│   ├── 商店内容 (名称+价格)
│   ├── 金币余额
│   └── 回合数
└── Data Layer           — 静态数据与配置
    ├── JSON DataStore   — 干员/装备 JSON 数据文件
    ├── SettingsManager  — 用户设置持久化
    └── MaaFramework     — OCR 引擎集成
```

### 2.2 窗口定位策略

参考 Hearthstone Deck Tracker 的窗口管理方式：

- **检测游戏窗口**: 使用 `User32.FindWindow()` 查找明日方舟PC端窗口（窗口标题 "明日方舟"）
- **位置同步**: 定时轮询 `User32.GetWindowRect()` 获取游戏窗口位置和尺寸
- **SidePanel 贴附逻辑**:
  - **默认**: 贴附在游戏窗口外侧（右侧），SidePanel.Left = GameWindow.Right
  - **空间不足**: 如果游戏窗口右侧空间不足（< SidePanel.Width），改为贴附在内侧（右侧覆盖）
  - **全屏模式**: 检测到全屏时，自动切换为内侧贴附
- **Overlay 窗口**: 完全覆盖游戏窗口区域，位置和尺寸与游戏窗口保持一致

### 2.3 模块化设计

为支持未来扩展到其他玩法，采用接口抽象：

```csharp
// 玩法模块接口
interface IGameMode
{
    string Name { get; }
    IEnumerable<TabDefinition> GetTabs();       // 该玩法提供的 Tab 页
    IOcrStrategy GetOcrStrategy();              // 该玩法的 OCR 识别策略
    IOverlayRenderer GetOverlayRenderer();      // 该玩法的覆盖层渲染器
}

// 首期实现
class GarrisonProtocolMode : IGameMode { ... }
```

---

## 3. 侧面板（SidePanel）设计

### 3.1 整体布局

- **窗口尺寸**: 宽度 280-320px，高度跟随游戏窗口
- **标题栏**: 可拖拽移动（解除贴附时）、最小化、关闭按钮
- **Tab 切换**: 干员 / 装备 / 追踪

### 3.2 干员浏览 Tab

**筛选区域**（可折叠）:
- 等阶筛选: 全选 / I / II / III / IV / V / VI
- 核心盟约筛选: 炎、萨尔贡、维多利亚、谢拉格、拉特兰、阿戈尔、叙拉古、卡西米尔
- 附加盟约筛选: 投资人、远见、调和、精准、坚守、突袭、奇迹、不屈、迅捷、助力、灵巧、协防干员、独行、奥术
- 特质词条筛选: 作战能力、整备能力、持续叠加、单次叠加、特异化、叠加
- 搜索框: 按干员名称模糊匹配

**干员卡片**（紧凑布局）:
```
┌──────────────────────────┐
│ Ⅵ 隐现      [普通|精锐] │  ← 等阶（罗马数字）+ 名称 + 卡片级切换
│ 🏛拉特兰  ⚡迅捷         │  ← 核心盟约 + 附加盟约图标
│ 作战能力：【拉特兰】     │  ← 特质词条类型 + 描述
│ 每叠加5层,攻速+1        │     数值根据普通/精锐切换变化
│              [☆ 追踪]   │  ← 追踪按钮
└──────────────────────────┘
```

- 每张卡片独立的 `[普通|精锐]` 切换按钮
- 等阶以罗马数字显示（Ⅰ~Ⅵ）
- 点击"追踪"按钮将该干员加入追踪列表，按钮变为"★ 追踪中"

### 3.3 装备浏览 Tab

**筛选区域**: 按等阶筛选（I~VI），按名称搜索

**装备卡片**:
```
┌──────────────────────────┐
│ Ⅱ 不屈弹射器 [普通|精锐] │  ← 等阶 + 名称 + 切换
│ 基础效果/进阶效果:       │
│ 再部署时间-30%,生命-30%  │  ← 根据切换显示不同效果
│              [☆ 追踪]   │
└──────────────────────────┘
```

### 3.4 追踪管理 Tab

**盟约持有计数**（顶部）:
```
┌──────────────────────────┐
│ 炎×2  谢拉格×3  阿戈尔×1 │  ← 来自 GameState OCR
│ 不屈×5  迅捷×2  坚守×1   │     实时更新
└──────────────────────────┘
```

**追踪列表**:
- 追踪中的干员列表（带移除按钮）
- 追踪中的装备列表（带移除按钮）
- 追踪列表持久化到本地 JSON 文件

**游戏状态面板**（底部）:
- 回合数、金币余额
- 场上干员（名称×数量）
- 备战区干员（名称×数量）
- 商店内容（名称 + 价格）
- 追踪命中提示（"⚠ 角峰 在商店!"）

---

## 4. 覆盖层（Overlay）设计

### 4.1 窗口属性

```xml
<Window
    AllowsTransparency="True"
    WindowStyle="None"
    Background="Transparent"
    Topmost="True"
    ShowInTaskbar="False"
    ResizeMode="NoResize" />
```

额外设置 `WS_EX_TRANSPARENT` 扩展样式实现点击穿透。

### 4.2 标记渲染

当 OCR 识别到商店中存在被追踪的干员/装备时：
- 在对应项目的**右上角**绘制绿色圆形 ★ 角标
- 角标尺寸约 28×28px（按游戏窗口比例缩放）
- 绿色填充（#4CAF50）+ 白色星号 + 阴影
- 使用 WPF Canvas 上的轻量 UIElement 渲染

### 4.3 坐标转换

OCR 返回的是截图像素坐标，需转换为覆盖层 Canvas 坐标：
1. OCR 返回 TextRegion (x, y, width, height) — 相对于截图
2. 覆盖层与游戏窗口客户区对齐
3. 标记位置 = TextRegion 右上角 + 偏移

---

## 5. OCR 与 GameState

### 5.1 OCR 扫描流程

```
定时器(每 N 秒) → ScreenCapture → OCR 识别 → 更新 GameState → 通知 ViewModel
```

- 使用 MaaFramework 的 ScreenCapture API 截取游戏画面
- 使用 MaaFramework 的 OCR API 识别文字
- 扫描间隔可配置（默认 3 秒）
- 仅在游戏窗口处于前台时触发扫描

### 5.2 识别区域

不对整个画面做 OCR，而是针对特定区域：
- **商店区域**: 识别干员/装备名称和价格
- **场上区域**: 识别已部署干员
- **备战区域**: 识别备战干员
- **经济信息区域**: 识别金币数、回合数

具体的区域坐标需要根据游戏窗口分辨率做比例适配。参考 MaaAssistantArknights 的区域定义方式。

### 5.3 GameState 数据模型

```csharp
class GameState : INotifyPropertyChanged
{
    int CurrentRound { get; set; }
    int Gold { get; set; }
    List<OwnedOperator> FieldOperators { get; set; }    // 场上
    List<OwnedOperator> BenchOperators { get; set; }    // 备战区
    List<ShopItem> ShopItems { get; set; }               // 商店
    Dictionary<string, int> CovenantCounts { get; set; } // 盟约计数
}

class ShopItem
{
    string Name { get; set; }
    int Price { get; set; }
    bool IsTracked { get; set; }   // 是否在追踪列表中
    Rect OcrRegion { get; set; }   // OCR 识别到的位置区域
}
```

---

## 6. 数据层

### 6.1 静态数据（用户提供）

干员数据 JSON 示例:
```json
{
  "operators": [
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
  ]
}
```

装备数据 JSON 示例:
```json
{
  "equipment": [
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
  ]
}
```

### 6.2 用户设置

持久化到 `settings.json`:
- 追踪列表（追踪的干员/装备名称）
- OCR 扫描间隔
- 窗口位置偏好
- 侧面板宽度

---

## 7. 项目结构

```
ASPAssistant/
├── ASPAssistant.sln
├── src/
│   ├── ASPAssistant.App/              — WPF 应用主项目
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── Windows/
│   │   │   ├── SidePanelWindow.xaml
│   │   │   └── OverlayWindow.xaml
│   │   ├── Views/                     — Tab 页 UserControl
│   │   │   ├── OperatorBrowseView.xaml
│   │   │   ├── EquipmentBrowseView.xaml
│   │   │   └── TrackingView.xaml
│   │   ├── Controls/                  — 自定义控件
│   │   │   ├── OperatorCard.xaml
│   │   │   └── EquipmentCard.xaml
│   │   └── Converters/               — WPF 值转换器
│   ├── ASPAssistant.Core/            — 核心逻辑库
│   │   ├── Engine/
│   │   │   ├── WindowTracker.cs
│   │   │   ├── ScreenCaptureService.cs
│   │   │   └── OcrScannerService.cs
│   │   ├── GameState/
│   │   │   ├── GameState.cs
│   │   │   └── GameStateUpdater.cs
│   │   ├── GameModes/
│   │   │   ├── IGameMode.cs
│   │   │   └── GarrisonProtocol/
│   │   │       ├── GarrisonProtocolMode.cs
│   │   │       ├── GarrisonOcrStrategy.cs
│   │   │       └── GarrisonOverlayRenderer.cs
│   │   ├── ViewModels/
│   │   │   ├── OperatorViewModel.cs
│   │   │   ├── EquipmentViewModel.cs
│   │   │   ├── TrackingViewModel.cs
│   │   │   └── GameStateViewModel.cs
│   │   ├── Models/
│   │   │   ├── Operator.cs
│   │   │   ├── Equipment.cs
│   │   │   ├── ShopItem.cs
│   │   │   └── OwnedOperator.cs
│   │   ├── Data/
│   │   │   ├── JsonDataStore.cs
│   │   │   └── SettingsManager.cs
│   │   └── Interop/
│   │       └── User32.cs             — Win32 P/Invoke 声明
│   └── ASPAssistant.Tests/           — 单元测试
├── data/
│   ├── operators.json                — 干员数据（用户提供）
│   └── equipment.json                — 装备数据（用户提供）
└── docs/
```

---

## 8. 验证计划

### 8.1 开发环境验证

1. **窗口追踪**: 启动应用 → 打开明日方舟PC端 → 验证 SidePanel 自动贴附在游戏窗口侧面，拖动游戏窗口时跟随
2. **全屏适配**: 切换游戏全屏 → 验证 SidePanel 切换为内侧贴附
3. **数据浏览**: 放入测试 JSON 数据 → 验证干员/装备列表正确展示、筛选和搜索功能正常
4. **普通/精锐切换**: 点击卡片上的切换按钮 → 验证数据正确切换
5. **追踪功能**: 添加/移除追踪项 → 验证追踪列表更新且持久化
6. **OCR 扫描**: 进入卫戍协议商店 → 验证 OCR 正确识别商店内容
7. **覆盖层标记**: 追踪的干员出现在商店 → 验证右上角出现绿色 ★ 角标
8. **点击穿透**: 覆盖层标记显示时 → 验证鼠标点击正常穿透到游戏

### 8.2 单元测试

- 数据模型的 JSON 反序列化
- 筛选逻辑（盟约、等阶、词条组合筛选）
- 追踪匹配逻辑（OCR 结果与追踪列表比对）
- 坐标转换逻辑
- GameState 更新与通知
