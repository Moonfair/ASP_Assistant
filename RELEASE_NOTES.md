# ASP Assistant 3.4.0

- 在「设置 → 游戏来源」中可选择正在运行的 MuMu 或雷电实例，多开时可按实例名和编号选择。MuMu 已完成实机取图和浮层验证，雷电适配处于试用阶段。
- 蓝叠、夜神、逍遥等模拟器可通过「手动绑定 ADB」指定本机 ADB 设备与窗口。
- 模拟器截图、OCR 和商店标记跟随所选实例。增强截图不可用时会尝试普通 ADB 或窗口截图。切换游戏来源时会清除上一实例的识别结果。

## 下载与使用

下载 `ASPAssistant-v3.4.0-win-x64.zip`，完整解压后运行 `win-x64/ASPAssistant.App.exe`。支持 Windows 10/11 x64，发布包自带 .NET Desktop Runtime 和所需的 VC++ CRT。

个人阵容和追踪设置保存在 `%APPDATA%\ASPAssistant`。如果旧版自动更新失败，可手动下载完整 ZIP 并解压运行。
