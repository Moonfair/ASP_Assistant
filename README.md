<div align="center">
  <img src="data/icons/aspa_logo.svg" width="96" height="96" alt="ASPA Logo"/>
  <h1>ASP Assistant</h1>
  <p>明日方舟·卫戍协议 实时辅助工具</p>
  <p>
    <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square"/>
    <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square"/>
    <img src="https://img.shields.io/badge/game-卫戍协议-orange?style=flat-square"/>
    <img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square"/>
  </p>
</div>

---

## 截图

![ASP Assistant 运行截图](docs/screenshot.png)

---

## 功能简介

ASP Assistant 是一款用于《明日方舟》**卫戍协议**玩法的透明悬浮侧边辅助工具，运行时叠加在游戏窗口上，提供以下功能：

### 干员浏览与筛选
- 干员默认按**等阶从低到高**排列，筛选后保持此顺序
- **等阶**、**核心盟约（阵营）**、**附加盟约**、**特质类型**、**触发时机**支持多选；同类标签匹配任意一项，不同类别同时满足
- **叠层**与**本局状态**保持单选；点击“全部”清空该类筛选，取消最后一个标签后自动回到“全部”
- 支持实时**名称搜索**
- 按**本局状态**（正常 / 禁用）过滤显示
- 可在每张干员卡片上手动**标记 / 取消标记**为禁用

### 核心与附加盟约自动禁用检测
- 自动检测“确认本局信息”准备页，不需要先添加追踪目标；侧栏顶部显示检测状态
- 保持顶部核心盟约及两行附加盟约完整可见，自动读取名称与图标状态：绿色可用，灰色且带阵容标记视为禁用，无需滚动到干员头像列表
- 有附加盟约的干员：**至少一个核心盟约被禁用，且至少一个附加盟约也被禁用**，才自动禁用
- 无附加盟约的干员：**总共只有一个核心盟约，且该核心被禁用**，才自动禁用；其他情况均不自动禁用
- 两部分识别完成后更新整份自动禁用名单；只改变附加盟约也会更新，识别不完整时保留已有名单并重试
- 策略选择、自选编队页面保留结果；下方禁用头像列表不再作为识别入口
- 点击“截屏录入”可强制重扫当前盟约，识别不依赖干员皮肤头像模板
- 支持连续多次截屏排队执行，界面显示"X 录入任务执行中"进度提示
- 手动禁用/取消禁用会保留至清空或下一局；清空后，相同盟约状态不会立即重新填入，点击“截屏录入”可恢复自动结果

### 装备追踪
- 浏览全部装备数据，按**等阶**、**类型**多选筛选
- 点击追踪按钮，将装备或干员加入本局追踪列表

### 游戏状态感知（OCR）
- 实时扫描识别商店物品、场上 / 备战区干员
- 自动统计当前盟约层数
- 在游戏画面上以**悬浮标记**高亮已追踪的目标
- 商店追踪直接识别名称栏，干员和装备共用逻辑；支持同名多卡及分辨率缩放，不依赖皮肤颜色、立绘或卡框模板。职业图标干扰文字时会裁掉图标复读，并用完整名称库校验。
- 新皮肤保留商店布局时可直接使用；若名称栏位置或卡牌布局改变，需要调整识别区域和相对定位规则。

### 其他
- 自动检测新版本并弹出更新提示
- 窗口置顶、可自由拖动和缩放

---

## 运行要求

| 条件 | 说明 |
|------|------|
| 操作系统 | Windows 10 / 11（x64） |
| 运行库 | 正式发布 ZIP 包自带 .NET 8 Desktop Runtime 和 x64 VC++ CRT；源码运行需要安装开发依赖 |
| 游戏客户端 | 明日方舟 PC 版（B 服 / 官服均可） |
| 游戏分辨率 | 推荐 16:9（1280×720 及以上） |

---

## 构建方法

```bash
# 克隆仓库
git clone <repo-url>
cd ASP_Assistant

# 拉取游戏数据（首次运行或版本更新后）
cd data
python fetch_spdatabase.py

# 构建并运行
cd ..
dotnet run --project src/ASPAssistant.App
```

> **提示**：`fetch_spdatabase.py` 需要 Python 3.10+ 以及 `requests` 和 `Pillow` 库。
> ```bash
> pip install requests Pillow
> ```

---

## 打包发布

使用 PowerShell 7，在项目根目录执行（将版本号换成本次发布版本）：

```powershell
dotnet test ASPAssistant.sln -c Release
python -B -m unittest discover -s tests
./scripts/publish.ps1 -Version 1.2.3 -OutputDirectory publish/win-x64
Compress-Archive -LiteralPath publish/win-x64 -DestinationPath publish/ASPAssistant-v1.2.3-win-x64.zip
```

打包机需安装 .NET 8 SDK 和 Visual Studio / Build Tools 的 C++ 工具。脚本自动查找 Visual Studio 的 x64 CRT 可再分发文件，将它们放到 `runtimes/win-x64/native` 的 Maa DLL 旁边，确保原生加载器能找到依赖；也可用 `-VcRedistDirectory` 指定 CRT 来源目录。找不到依赖、依赖架构错误或输出目录非空时会停止，避免生成不完整或夹带旧文件的包。重新打包请使用新的空目录。

GitHub 标签发布也调用同一个脚本。发布前需要提交全部代码和数据更新，包括 `covenants.json` 与新增图片；标签对应的提交必须包含这些文件。

发布包只包含 Windows x64 原生库、当前目录数据、浏览图片和 OCR 模型，不包含旧皮肤匹配模板、调试符号、Python 缓存或个人设置。皮肤下载脚本仍保留供开发参考。

### 用户数据与更新缓存

- 追踪设置：`%APPDATA%\ASPAssistant\tracking.json`。保存采用临时文件替换；损坏文件会保留为 `tracking.corrupt-*.json`，并恢复为空列表。
- 应用日志和更新失败日志：`%APPDATA%\ASPAssistant\logs`。
- 更新临时目录：`%TEMP%\ASPAssistant-Update\<独立任务 ID>`。下载失败或安装结束后清理本次任务，不删除其他正在进行的更新。
- 将程序解压到当前用户可写的目录；更新前会检查目标目录写入权限。中文、空格及特殊字符路径均按字面值处理。

---

## 数据文件说明

```
data/
├── operators.json          # 干员数据（含阵营、特质等）
├── equipment.json          # 装备数据
├── skin_avatar_map.json    # 皮肤头像 → 干员名映射（Ban 位识别用）
├── source_metadata.json    # 赛季、抓取时间、上游修改时间及 SHA-256
├── icons/
│   ├── operators/          # 干员头像（来自 PRTS Wiki）
│   └── skin_avatars/       # 皮肤头像（Ban 位识别模板）
└── fetch_spdatabase.py     # 数据拉取 / 皮肤下载脚本
```

### 常用脚本命令

| 命令 | 功能 |
|------|------|
| `python fetch_spdatabase.py` | 拉取最新干员 / 装备数据 |
| `python fetch_spdatabase.py --download-skins` | 下载皮肤头像并重建映射文件 |
| `python fetch_spdatabase.py --apply-mask` | 为现有皮肤头像补白色背景 |
| `python fetch_spdatabase.py --skip-icons` | 跳过头像下载，仅更新 JSON |

当前默认拉取 **卫戍协议：规程**（`act3autochess / V3_1`）。旧赛季存档可用
`--season 2`（盟约上半）或 `--season 3`（盟约下半的存档版本）拉取；这些命令会替换当前本地数据。
上游文件的最后修改时间和本次抓取时间记录在 `data/source_metadata.json`，均带有时区信息。

---

## 项目结构

```
src/
├── ASPAssistant.App/       # WPF 前端（Views、Windows、Resources）
├── ASPAssistant.Core/      # 业务逻辑（Services、ViewModels、Models、GameState）
└── ASPAssistant.Tests/     # 单元测试（xUnit + FluentAssertions）
```

---

## 开发依赖

- [MaaFramework](https://github.com/MaaAssistantArknights/MaaFramework) — 图像识别（OCR / TemplateMatch / FeatureMatch）
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 基础设施
- [Newtonsoft.Json](https://www.newtonsoft.com/json) / `System.Text.Json` — JSON 序列化

---

## 许可证

本项目以 [Apache License 2.0](LICENSE) 开源，可自由使用、修改和分发，但须保留原始版权声明。

本项目仅供个人学习和使用，与 Hypergryph / 鹰角网络无关。
