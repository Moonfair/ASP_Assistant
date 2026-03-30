# SidePanel 位置与尺寸持久化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 玩家手动移动 ASPA 面板后保留其位置直到游戏窗口真正移动；玩家手动调整 ASPA 面板高度后永久保留该高度。

**Architecture:** 在 `WindowTrackerService` 中区分"真实移动"与"稳定心跳"两类事件；在 `SidePanelWindow` 中用标志位追踪用户意图；`App.xaml.cs` 订阅新事件并传入 `gameActuallyMoved` 参数。

**Tech Stack:** C# 12, .NET 8, WPF, xUnit 2.9, FluentAssertions 8

---

## 文件改动一览

| 文件 | 类型 | 职责 |
|------|------|------|
| `src/ASPAssistant.Core/Services/WindowTrackerService.cs` | 修改 | 新增 `GameWindowPolled` 事件；提取 `IsGameWindowChanged` 纯逻辑方法；修改 `OnPollTick` 分发两类事件 |
| `src/ASPAssistant.Tests/Services/WindowTrackerServiceTests.cs` | 新建 | 对 `IsGameWindowChanged` 做单元测试 |
| `src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs` | 修改 | 新增五个标志/辅助字段；新增四个事件处理器；修改 `UpdatePosition` 签名与逻辑 |
| `src/ASPAssistant.App/App.xaml.cs` | 修改 | 订阅 `GameWindowPolled`；在两个处理器中传入 `gameActuallyMoved` |

---

## Task 1: WindowTrackerService — 提取纯逻辑并新增事件

**Files:**
- Modify: `src/ASPAssistant.Core/Services/WindowTrackerService.cs`
- Create: `src/ASPAssistant.Tests/Services/WindowTrackerServiceTests.cs`

### 步骤

- [ ] **Step 1: 新建测试文件（先写失败测试）**

  创建 `src/ASPAssistant.Tests/Services/WindowTrackerServiceTests.cs`：

  ```csharp
  using ASPAssistant.Core.Interop;
  using ASPAssistant.Core.Services;
  using FluentAssertions;

  namespace ASPAssistant.Tests.Services;

  public class WindowTrackerServiceTests
  {
      private static RECT R(int l, int t, int r, int b) =>
          new() { Left = l, Top = t, Right = r, Bottom = b };

      // 首帧：previous 为 null，视为"变化"
      [Fact]
      public void IsGameWindowChanged_WhenPreviousIsNull_ReturnsTrue()
      {
          WindowTrackerService.IsGameWindowChanged(null, false, R(0,0,1920,1080), false)
              .Should().BeTrue();
      }

      // RECT 无变化，attachInside 无变化 → 稳定心跳
      [Fact]
      public void IsGameWindowChanged_WhenRectAndAttachSame_ReturnsFalse()
      {
          var rect = R(100, 50, 1820, 1080);
          WindowTrackerService.IsGameWindowChanged(rect, true, rect, true)
              .Should().BeFalse();
      }

      // RECT 变化 → 真实移动
      [Fact]
      public void IsGameWindowChanged_WhenRectChanged_ReturnsTrue()
      {
          WindowTrackerService.IsGameWindowChanged(R(0,0,100,100), false, R(10,0,110,100), false)
              .Should().BeTrue();
      }

      // RECT 不变但 ShouldAttachInside 变化 → 也视为"变化"
      [Fact]
      public void IsGameWindowChanged_WhenOnlyAttachInsideChanged_ReturnsTrue()
      {
          var rect = R(0, 0, 1920, 1080);
          WindowTrackerService.IsGameWindowChanged(rect, false, rect, true)
              .Should().BeTrue();
      }
  }
  ```

- [ ] **Step 2: 运行测试，确认失败（方法尚不存在）**

  ```
  cd src/ASPAssistant.Tests
  dotnet test --filter "WindowTrackerServiceTests"
  ```

  预期：编译失败，提示 `IsGameWindowChanged` 不存在。

- [ ] **Step 3: 在 `WindowTrackerService` 中添加 `GameWindowPolled` 事件和 `IsGameWindowChanged` 静态方法**

  在现有字段区块下方添加事件：
  ```csharp
  public event Action<RECT>? GameWindowPolled;
  ```

  在类末尾（`Dispose` 之前）添加 `public static` 方法（需为 `public` 以便测试程序集访问）：
  ```csharp
  public static bool IsGameWindowChanged(RECT? previous, bool previousAttachInside,
      RECT current, bool currentAttachInside)
  {
      if (previous is null) return true;
      var p = previous.Value;
      return p.Left   != current.Left   ||
             p.Top    != current.Top    ||
             p.Right  != current.Right  ||
             p.Bottom != current.Bottom ||
             previousAttachInside != currentAttachInside;
  }
  ```

  `RECT` 是无 `==` 重载的裸结构体，必须逐字段比较。

- [ ] **Step 4: 修改 `OnPollTick` 分发逻辑**

  将现有末尾两行：
  ```csharp
  CurrentGameRect = windowRect;
  GameWindowMoved?.Invoke(windowRect);
  ```

  替换为：
  ```csharp
  var changed = IsGameWindowChanged(CurrentGameRect, ShouldAttachInside,
      windowRect, shouldAttachInside);
  ShouldAttachInside = shouldAttachInside;
  CurrentGameRect = windowRect;

  if (changed)
      GameWindowMoved?.Invoke(windowRect);
  else
      GameWindowPolled?.Invoke(windowRect);
  ```

  同时，将 `ShouldAttachInside` 赋值从原处移除（原来是 `ShouldAttachInside = ...` 在比较之前），改为在调用 `IsGameWindowChanged` 后才赋值，以保证比较时用的是上一轮值。具体地，原代码：
  ```csharp
  var screenWidth = GetPrimaryScreenWidth();
  var rightSpace = screenWidth - windowRect.Right;
  ShouldAttachInside = rightSpace < 320 || IsFullscreen(windowRect, screenWidth);

  CurrentGameRect = windowRect;
  GameWindowMoved?.Invoke(windowRect);
  ```

  改为：
  ```csharp
  var screenWidth = GetPrimaryScreenWidth();
  var rightSpace = screenWidth - windowRect.Right;
  var shouldAttachInside = rightSpace < 320 || IsFullscreen(windowRect, screenWidth);

  var changed = IsGameWindowChanged(CurrentGameRect, ShouldAttachInside,
      windowRect, shouldAttachInside);
  ShouldAttachInside = shouldAttachInside;
  CurrentGameRect = windowRect;

  if (changed)
      GameWindowMoved?.Invoke(windowRect);
  else
      GameWindowPolled?.Invoke(windowRect);
  ```

- [ ] **Step 5: 运行测试，确认通过**

  ```
  cd src/ASPAssistant.Tests
  dotnet test --filter "WindowTrackerServiceTests"
  ```

  预期：4 个测试全部 PASS。

- [ ] **Step 6: 确认项目可编译**

  ```
  cd src
  dotnet build ASPAssistant.Core/ASPAssistant.Core.csproj
  ```

  预期：编译成功，0 错误。

- [ ] **Step 7: 提交**

  ```
  git add src/ASPAssistant.Core/Services/WindowTrackerService.cs
  git add src/ASPAssistant.Tests/Services/WindowTrackerServiceTests.cs
  git commit -m "feat: add GameWindowPolled event and rect-dedup logic to WindowTrackerService"
  ```

---

## Task 2: SidePanelWindow — 用户意图标志位与 UpdatePosition 修改

**Files:**
- Modify: `src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs`

此类为 WPF 窗口，不做自动化测试；最终在 Task 3 完成后整体手动验证。

### 步骤

- [ ] **Step 1: 添加四个私有字段**

  在现有 `_updateService` 和 `_pendingUpdate` 字段下方添加：

  ```csharp
  // 用户意图追踪
  private bool _isUserPositioned;       // 用户手动移动过窗口
  private bool _isUserSized;            // 用户手动调整过窗口高度
  private bool _isProgrammaticChange;   // 程序正在设置位置/尺寸，抑制事件响应
  private bool _isRestoring;            // 正在从最小化恢复，抑制事件响应
  ```

- [ ] **Step 2: 添加事件处理器和辅助字段**

  **先**在 Step 1 的字段区块末尾追加一个辅助字段：
  ```csharp
  private WindowState _previousWindowState;
  ```

  然后在 `OnTitleBarDrag` 之前添加以下全部处理器：

  ```csharp
  private void OnLocationChanged(object? sender, EventArgs e)
  {
      if (!_isProgrammaticChange && !_isRestoring)
          _isUserPositioned = true;
  }

  private void OnSizeChanged(object sender, SizeChangedEventArgs e)
  {
      if (!_isProgrammaticChange && !_isRestoring && e.HeightChanged)
          _isUserSized = true;
  }

  private void OnStateChanged(object? sender, EventArgs e)
  {
      if (_previousWindowState == WindowState.Minimized && WindowState == WindowState.Normal)
      {
          _isRestoring = true;
          LayoutUpdated += ClearRestoringFlag;
      }
      _previousWindowState = WindowState;
  }

  private void ClearRestoringFlag(object? sender, EventArgs e)
  {
      _isRestoring = false;
      LayoutUpdated -= ClearRestoringFlag;
  }
  ```

  `OnStateChanged` 通过比较 `_previousWindowState`（旧值）与当前 `WindowState`（新值）精确识别 Minimized→Normal 转换，不会误触其他状态变化。

- [ ] **Step 3: 在构造函数中订阅三个事件（在 Step 2 的处理器存在后才添加订阅）**

  在 `InitializeComponent()` 之后、`trackingVm.GameState = ...` 之前，添加：

  ```csharp
  LocationChanged += OnLocationChanged;
  SizeChanged     += OnSizeChanged;
  StateChanged    += OnStateChanged;
  ```

- [ ] **Step 4: 修改 `UpdatePosition` 签名**

  将：
  ```csharp
  public void UpdatePosition(RECT gameRect, bool attachInside)
  ```
  改为：
  ```csharp
  public void UpdatePosition(RECT gameRect, bool attachInside, bool gameActuallyMoved)
  ```

- [ ] **Step 5: 修改 `UpdatePosition` 方法体**

  将现有方法体替换为：

  ```csharp
  public void UpdatePosition(RECT gameRect, bool attachInside, bool gameActuallyMoved)
  {
      _isProgrammaticChange = true;
      try
      {
          if (gameActuallyMoved)
              _isUserPositioned = false;

          var targetLeft = attachInside ? gameRect.Right - Width : (double)gameRect.Right;
          var targetTop  = (double)gameRect.Top;
          var targetHeight = (double)gameRect.Height;

          if (!_isUserPositioned)
          {
              Left = targetLeft;
              Top  = targetTop;
          }

          if (!_isUserSized)
              Height = targetHeight;
      }
      finally
      {
          _isProgrammaticChange = false;
      }
  }
  ```

- [ ] **Step 6: 确认项目可编译（App 项目此时会因 App.xaml.cs 调用旧签名而报错，属正常）**

  从仓库根目录：
  ```
  dotnet build src/ASPAssistant.App/ASPAssistant.App.csproj
  ```

  预期：仅 `App.xaml.cs` 中调用 `UpdatePosition` 处报错（参数数量不符），其余无误。

- [ ] **Step 7: 提交（暂不提交，等 Task 3 一起提交整洁的可编译状态）**

---

## Task 3: App.xaml.cs — 订阅新事件并传参

**Files:**
- Modify: `src/ASPAssistant.App/App.xaml.cs`

### 步骤

- [ ] **Step 1: 在 `GameWindowMoved` 处理器中加入 `gameActuallyMoved: true`**

  找到现有代码（`StartupAsync` 中）：
  ```csharp
  _windowTracker.GameWindowMoved += rect =>
  {
      Dispatcher.Invoke(() =>
      {
          _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside);
          ...
      });
  };
  ```

  将 `_sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside)` 改为：
  ```csharp
  _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside, gameActuallyMoved: true);
  ```

- [ ] **Step 2: 在 `GameWindowMoved` 处理器之后添加 `GameWindowPolled` 订阅**

  ```csharp
  _windowTracker.GameWindowPolled += rect =>
  {
      Dispatcher.Invoke(() =>
      {
          _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside, gameActuallyMoved: false);
      });
  };
  ```

- [ ] **Step 3: 确认完整解决方案可编译**

  从仓库根目录运行：
  ```
  dotnet build ASPAssistant.sln
  ```

  预期：0 错误，0 警告（或仅有已有警告）。

- [ ] **Step 4: 运行全部单元测试**

  ```
  cd src/ASPAssistant.Tests
  dotnet test
  ```

  预期：所有测试通过（包括新增的 4 个 `WindowTrackerServiceTests`）。

- [ ] **Step 5: 提交 Task 2 + Task 3 的全部改动**

  ```
  git add src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs
  git add src/ASPAssistant.App/App.xaml.cs
  git commit -m "feat: persist SidePanel position/size across game window polling"
  ```

---

## Task 4: 手动冒烟测试

**无需改动文件**

- [ ] **Step 1: 编译并启动应用**

  启动明日方舟游戏（或至少让系统能找到游戏进程），然后在 IDE 中 `F5` 启动 `ASPAssistant.App`。

- [ ] **验证场景 A：位置持久化**
  1. 应用启动后，ASPA 面板贴附在游戏窗口旁。
  2. 用鼠标拖动 ASPA 面板到屏幕中间。
  3. 等待 2 秒——面板保持在拖动后的位置，不会被拉回游戏窗口旁。
  4. 移动明日方舟游戏窗口。
  5. ASPA 面板恢复贴附到游戏窗口旁。

- [ ] **验证场景 B：尺寸持久化**
  1. 用鼠标拖动 ASPA 面板底边，将其高度调小（例如拖到约一半高度）。
  2. 等待 2 秒——高度保持不变。
  3. 移动明日方舟游戏窗口——ASPA 面板高度仍保持用户调整后的值，不被游戏窗口高度覆盖。

- [ ] **验证场景 C：最小化再恢复**
  1. 点击 ASPA 面板的最小化按钮。
  2. 从任务栏点击恢复。
  3. 等待 2 秒——`_isUserPositioned` 不应被误设（面板仍跟随游戏窗口）。
  4. 拖动游戏窗口——面板跟随贴附（验证最小化恢复未干扰正常行为）。

- [ ] **Step 完成后提交测试通过记录（可选）**

  ```
  git commit --allow-empty -m "test: manual smoke test passed for position/size persistence"
  ```
