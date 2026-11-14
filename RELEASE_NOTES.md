# ASP Assistant 3.8.5

- 鲍勃语音现已适配三套卫戍主题。新增两套主题的战备、倒计时警示、仅剩一人未就绪、完美作战与战斗失败识别。新增主题跳过低血量提示。
- 新增 20 句台词，移除 5 句含炉石专属用语的台词，共 36 句。「看你打牌是种享受」保留在战备闲聊中。
- 导入语音后默认开启。可在「设置 → 默认鲍勃语音」试听或关闭，关闭偏好会保存。

## 下载与使用

下载 `ASPAssistant-v3.8.5-win-x64.zip`，完整解压后运行 `win-x64/ASPAssistant.App.exe`。支持 Windows 10/11 x64，发布包自带 .NET Desktop Runtime 和所需的 VC++ CRT。

公开发布包内置识别模板和字幕。请将从本机《炉石传说》提取的默认鲍勃简体中文 WAV 放到程序目录下的 `data/voice/bob/`，保留游戏内资源名，例如 `VO_DALA_BOSS_99h_Male_Human_NewGame_01.wav`，然后重启程序。从旧版迁移时，复制整个 `data/voice/bob/` 即可保留已导入的语音，有新增台词的 WAV 也可以一起放进去。

个人阵容和追踪设置保存在 `%APPDATA%\ASPAssistant`。
