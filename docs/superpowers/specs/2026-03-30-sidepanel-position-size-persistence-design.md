# SidePanel 位置与尺寸持久化设计文档

**日期**：2026-03-30  
**状态**：已批准

---

## 背景与目标

当前 `WindowTrackerService` 每 100ms 轮询一次明日方舟游戏窗口，每次都触发 `GameWindowMoved` 事件，导致 `SidePanelWindow` 被强制重定位，玩家手动拖动或调整 ASPA 窗口后的操作会立即被覆盖。

本特性需要实现：

1. **位置持久化**：玩家手动移动 ASPA 窗口后，保留其位置，直到玩家再次移动明日方舟游戏窗口，此时恢复默认贴附位置。
2. **尺寸持久化**：玩家手动调整 ASPA 窗口高度后，始终保留该高度，即使明日方舟游戏窗口移动也不覆盖。

---

## 架构概览

三处改动，职责单一，互不耦合：

```
WindowTrackerService        SidePanelWindow            App.xaml.cs
─────────────────────       ──────────────────         ─────────────
OnPollTick:                 _isUserPositioned          订阅两个事件：
  rect 变化 →               _isUserSized                 GameWindowMoved
    GameWindowMoved         _isProgrammaticChange          → gameActuallyMoved=true
  rect 不变 →               LocationChanged              GameWindowPolled
    GameWindowPolled          → 设 _isUserPositioned      → gameActuallyMoved=false
                            SizeChanged
                              → 设 _isUserSized
                            UpdatePosition(
                              rect, attachInside,
                              gameActuallyMoved)
```

---

## 详细设计

### 1. `WindowTrackerService`（`src/ASPAssistant.Core/Services/WindowTrackerService.cs`）

**新增事件：**
```csharp
public event Action<RECT>? GameWindowPolled;
```

**修改 `OnPollTick`：**

在现有 `CurrentGameRect = windowRect; GameWindowMoved?.Invoke(windowRect);` 之前加入 rect 比较：

```
旧 rect == 新 rect → 触发 GameWindowPolled（位置稳定心跳）
旧 rect != 新 rect → 触发 GameWindowMoved（游戏窗口真实移动）
```

两条路径都更新 `CurrentGameRect`。

### 2. `SidePanelWindow`（`src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs`）

**新增字段：**
```csharp
private bool _isUserPositioned;   // 用户手动移动过窗口
private bool _isUserSized;        // 用户手动调整过窗口高度
private bool _isProgrammaticChange; // 正在执行程序定位，抑制事件响应
```

**新增事件订阅（构造函数中）：**
```csharp
LocationChanged += OnLocationChanged;
SizeChanged     += OnSizeChanged;
```

**事件处理器：**
```csharp
private void OnLocationChanged(object? sender, EventArgs e)
{
    if (!_isProgrammaticChange)
        _isUserPositioned = true;
}

private void OnSizeChanged(object sender, SizeChangedEventArgs e)
{
    if (!_isProgrammaticChange && e.HeightChanged)
        _isUserSized = true;
}
```

**修改 `UpdatePosition` 签名：**
```csharp
public void UpdatePosition(RECT gameRect, bool attachInside, bool gameActuallyMoved)
```

**修改 `UpdatePosition` 逻辑：**
```
_isProgrammaticChange = true
try {
    if (gameActuallyMoved)
        _isUserPositioned = false   // 游戏移动后解除用户覆盖

    var targetLeft  = attachInside ? gameRect.Right - Width : gameRect.Right
    var targetTop   = gameRect.Top
    var targetHeight = gameRect.Height

    if (!_isUserPositioned) {
        Left = targetLeft
        Top  = targetTop
    }

    if (!_isUserSized)
        Height = targetHeight
} finally {
    _isProgrammaticChange = false
}
```

### 3. `App.xaml.cs`（`src/ASPAssistant.App/App.xaml.cs`）

订阅 `GameWindowPolled` 事件，调用时传 `gameActuallyMoved: false`：

```csharp
_windowTracker.GameWindowPolled += rect =>
{
    Dispatcher.Invoke(() =>
    {
        _sidePanel.UpdatePosition(rect, _windowTracker.ShouldAttachInside,
            gameActuallyMoved: false);
        // overlay 位置不受影响，无需改动
    });
};
```

同时将现有 `GameWindowMoved` 处理器改为传 `gameActuallyMoved: true`。

---

## 数据流

```
轮询心跳
  游戏 rect 未变 → GameWindowPolled → UpdatePosition(..., false)
    _isUserPositioned=true  → 跳过 Left/Top（保留用户位置）
    _isUserPositioned=false → 正常写入 Left/Top
    _isUserSized=true       → 跳过 Height（保留用户高度）
    _isUserSized=false      → 正常写入 Height

  游戏 rect 变化 → GameWindowMoved → UpdatePosition(..., true)
    清除 _isUserPositioned → 正常写入 Left/Top（恢复默认贴附）
    _isUserSized=true       → 跳过 Height（保留用户高度）
    _isUserSized=false      → 正常写入 Height
```

---

## 边界情况

| 场景 | 行为 |
|------|------|
| 程序启动时 | 两个标志均为 false，行为与现有完全一致 |
| 用户最小化窗口后恢复 | `LocationChanged`/`SizeChanged` 可能触发，但守卫抑制误判 |
| 游戏窗口关闭再重新打开 | `GameWindowLost` → `GameWindowMoved`（真实移动），清除 `_isUserPositioned` |
| 用户同时调整了位置和大小 | 两个标志独立管理，互不影响 |

---

## 涉及文件

| 文件 | 改动类型 |
|------|----------|
| `src/ASPAssistant.Core/Services/WindowTrackerService.cs` | 新增事件 + rect 去重逻辑 |
| `src/ASPAssistant.App/Windows/SidePanelWindow.xaml.cs` | 新增字段、事件处理器、修改 `UpdatePosition` |
| `src/ASPAssistant.App/App.xaml.cs` | 订阅新事件，更新调用参数 |
