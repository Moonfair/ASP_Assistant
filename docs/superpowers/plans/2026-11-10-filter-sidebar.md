# Filter Sidebar Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement inline.

**Goal:** 实现确认的 A 小图标布局及顶部/侧边切换。
**Architecture:** 同一筛选面板在原浏览页和主窗体左侧宿主之间移动，控件与 DataContext 保持不变；主窗体统一管理宽度和右边缘。图标从本地名称文件加载并以透明蒙版着色。
**Tech Stack:** .NET 8 / WPF / xUnit / Python unittest.
**Spec:** ../specs/2026-11-10-filter-sidebar-design.md

## Global Constraints
- 保留当前工作区已有阵容和禁用改动；不提交混合用户改动。
- 默认顶部，布局偏好写入独立 ui.json；图标离线随程序发布。

## Review Focus
- 多次切换、页签切换和重置后筛选结果与控件选择同步。
- 展开/收起不挤压主列表、不漂移右边缘，游戏窗口自动定位仍正常。
- 侧栏独立滚动，顶部布局原有滚动收起仍有效。
- 缺失/损坏图标和设置不会阻止启动；被禁用盟约仍能筛选。
- 原图和刷新脚本、发布配置一致。

## Tasks
- [x] 增加设置往返/坏数据、图标来源映射、侧栏切换保留条件和宽度的行为测试；运行确认失败。
- [x] 增加独立布局设置读写；提供本地盟约图标控件、打包资源和抓取脚本。
- [x] 增加可复用筛选面板宿主逻辑；接入主窗体切换、展开/收起、重置和宽度管理；原有浏览功能保持。
- [x] 运行 .NET / Python 完整测试，渲染实际 WPF 展开/收起/顶部截图检查并完成独立代码审查。

## Validation
`dotnet test ASPAssistant.sln -c Release`
`python -B -m unittest discover -s tests`

## Execution notes
- 工作区已有相关未提交功能，直接增量修改，保留原始文件快照用于审查。

- 实施简化：两个布局使用布尔偏好，无额外枚举；独立 ui.json 以临时文件替换方式同步保存，避免立即退出丢失最后一次切换。
- 实际渲染发现顶部筛选挤占列表，增加独立滚动，最大高度为视图高度的45%（最低80px），以310×500窗口回归测试验证保留结果区域。
- 独立审查：发现损坏 PNG 的 FileFormatException 未捕获；以截断PNG复现 RED，改为显式释放文件流并捕获错误后 GREEN。无未处理审查问题。
- 验证：dotnet test ASPAssistant.sln -c Release --no-restore → 161/161；Python unittest discover → 9/9；git diff --check 通过。
- 实际 WPF 渲染：artifacts/filter-layout-audit/{sidebar-expanded,sidebar-collapsed,top-filters,equipment-sidebar}.png。
- 交付保留在当前工作区，不合并或提交同目录其他正在进行的功能。
