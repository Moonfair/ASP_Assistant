# Windows 模拟器兼容调研

## 兼容目标

MuMu 和雷电作为首批完整适配目标。用户在 ASP Assistant 选择运行《明日方舟》的模拟器实例后，侧栏贴附对应窗口，自动识别准备页与商店，手动截屏录入和商店浮层可以正常使用。通用连接覆盖蓝叠、夜神、逍遥等提供本地 ADB 的模拟器，允许手动绑定设备与窗口。

## 现有调用链

- `User32.FindArknightsWindow` 按完整标题“明日方舟”取窗口。`WindowTrackerService`、`ScreenCaptureService`、`App.xaml.cs` 和 `OverlayWindow` 各自调用它。模拟器多开时需要从同一实例得到所有窗口信息。
- `WindowTrackerService` 用外层窗口计算侧栏位置，`OverlayWindow` 按游戏客户区投影归一化的 OCR 标记。模拟器标题栏、工具条和黑边需要从浮层坐标中剔除，WPF 位置还要处理显示器 DPI。
- `OcrScannerService` 每 500 毫秒尝试扫描一帧，手动录入在按钮回调里同步截图。模拟器取图应在后台执行，并在点击时绑定目标实例与当前识别代次。现有 OCR 可以继续吃 PNG 和相对坐标。

## 实现路线

| 方案 | 适用性 |
| --- | --- |
| Win32 取图与贴附 | 接入简单。对外层 HWND 可用 Windows Graphics Capture，再按渲染区裁切。屏幕像素复制可复用现有实现，窗口被遮挡时截图会包含遮挡物。 |
| 通用 ADB 截图与 Win32 贴附 | Android 原生分辨率，有明确设备序列号，适合手动接入。MuMu 本机的 PNG 和 Gzip 取图耗时会挤占 500 毫秒识别周期。 |
| 厂商实例发现、MaaFramework 增强取图和通用回退 | 建议采用。用同一个实例索引关联设备与渲染区，保留 Windows 客户端入口。MuMu 的增强取图已验证可用，雷电需在实际安装环境中跑同一套验收。 |

1. **统一目标来源。** 建立游戏目标服务，保存厂商、安装路径、实例索引、ADB 可执行文件及序列号、顶层 HWND、渲染 HWND。窗口跟踪、手动与自动取图、浮层消费同一个目标快照。原生客户端目标沿用现有标题发现。多实例时让用户选实例；保存安装标识和实例索引，重启后重新查询 HWND 和端口。
2. **复用 MAA 的实例发现规则。** MuMu 从运行进程路径找到 `MuMuManager.exe`，覆盖旧版 `shell` 和新版 `nx_main`，执行 `info -v all` 获取实例状态、`main_wnd`、`render_wnd`、`adb_host_ip` 和 `adb_port`。解析单对象及按实例索引组织的多对象。雷电 9/14 从运行进程安装目录调用 `ldconsole.exe list2`，取实例索引、运行状态和两种 HWND。优先核对在线的 `emulator-(5554 + 2 × index)`，同实例的 TCP 候选是 `127.0.0.1:(5555 + 2 × index)`。用 `adb devices` 和指定序列号查询实际连通性，多实例时检查端口归属，避免把别家模拟器的同号端口绑定进来。新版 MuMu 若未返回 TCP 端点，用实际注册的序列号或手动绑定，避免套用固定端口。 [MaaFramework v5.8.1 Windows 设备发现](https://github.com/MaaXYZ/MaaFramework/blob/v5.8.1/source/MaaToolkit/AdbDevice/AdbDeviceWin32Finder.cpp) · [雷电 list2 与 ADB 指南](https://jp.ldplayer.net/support/ldconsole-command-line-guide.html)
3. **为每个选中实例复用截图控制器。** 优先用已随项目分发的 MaaFramework 运行时启用厂商增强截图，保持源分辨率。连接成功后实际验证帧，失败时切换到该实例的通用 ADB；慢速后端按实际耗时降低取图频率，自动扫描最多一帧在途。可见时提供 Win32 截图回退，Windows Graphics Capture 用顶层窗口取图再裁切。外部调用设置超时，实例离线或切换时让旧帧和排队结果失效。 [MaaFramework 截图方法](https://github.com/MaaXYZ/MaaFramework/blob/v5.8.1/include/MaaFramework/MaaDef.h) · [MAA 雷电截图配置](https://github.com/MaaAssistantArknights/MaaAssistantArknights/blob/dev-v2/resource/config.json)
4. **分别定位侧栏和标记。** 侧栏沿顶层窗口定位，浮层沿实际游戏画面定位。取渲染子窗口客户区，以截图方向、内容比例和实际黑边校准坐标，再按目标显示器 DPI 映射到 WPF。处理窗口移动、缩放、最小化、恢复和关闭，重新计算已有标记的位置。模拟器浮层维持鼠标穿透。
5. **接入通用模拟器。** 设置中可刷新在线 ADB 设备和窗口并手动绑定。蓝叠 Hyper-V 的端口可能在重启后改变，重新读取 `bluestacks.conf` 中对应实例的 ADB 端口。夜神、逍遥先使用同一套通用截图和窗口绑定，按实机结果补充自动发现。 [MAA 连接设置](https://docs.maa.plus/en-us/manual/connection) · [MAA Windows 模拟器支持](https://docs.maa.plus/en-us/manual/device/windows.html)

## 本机取样

2026-11-11，Windows 上的 MuMu 5.30.1.3586，Android 12、实例 0、桌面 100% DPI。测试使用模拟器桌面画面，在该实例上确认了国服包 `com.hypergryph.arknights` 已安装。实例管理器报告 ADB 地址 `127.0.0.1:16384`。顶层客户区是 1347×798，渲染客户区是 1347×758，渲染区向下偏移 40 像素。游戏识别应以实际截图的方向为准，本次 `wm size` 报告 1080×1920，截图为 1920×1080。

| 取图方式 | 本机结果 |
| --- | --- |
| MaaFramework 5.8.1 MuMu Extras | 原分辨率 1920×1080，含 PNG 编码的 5 帧热取图中位数约 94 毫秒，使用 `nx_main/sdk/external_renderer_ipc.dll` |
| ADB `exec-out screencap -p` | 1920×1080，8 次中位数约 1.69 秒 |
| ADB 原始帧 / Gzip | 5 次中位数约 500 / 484 毫秒，尚需在主机解码并交给 OCR |
| MaaFramework Win32 ScreenDC | 渲染 HWND 可取 1347×758，5 帧热取图中位数约 49 毫秒，读取桌面可见像素 |
| MaaFramework Win32 FramePool | 顶层 HWND 可取 1347×797，5 帧热取图中位数约 61 毫秒；渲染子 HWND 创建捕获对象失败 |

现有基线为 .NET 178 项通过，Python 9 项及 14 个子测试通过。本次只验证了 MuMu 实例发现、连接与截图。雷电、蓝叠、夜神和逍遥的适配结论来自上游源码及厂商资料，等待实机验收。

## 实现验收

- 原生客户端与模拟器可同时运行。切换目标后，截图、侧栏、标记和扫描结果均来自同一实例。
- MuMu Android 12 与新版本、雷电 9/14 分别验收单开、多开、重启换端口、ADB 关闭或离线，以及增强截图失败后的回退。通用入口分别用蓝叠、夜神、逍遥走一遍设备与窗口手动绑定。
- 用准备页和商店做真实识别，覆盖自动扫描、手动录入、浮层标记和失败后的恢复。测试有工具条和黑边、100%/150% DPI、跨屏拖动、最小化与恢复。记录取图耗时和 OCR 耗时，核对常用分辨率下的 500 毫秒轮询及浮层对齐。
