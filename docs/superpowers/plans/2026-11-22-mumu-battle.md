# MuMu 战斗采集 Implementation Plan

**目标 / 约束：** 按用户确认的范围仅替换 MuMu 数据源；保持 PC 的 BattleFrame、BattleStatistics 和数据页布局及计算口径。其他模拟器仍仅截图。
- [x] 验证既有 BattleDataView 测试；增加 MuMu 切换、四类计数器、非法帧和实例约束的失败用例。
- [x] tools/mumu-battle：以现有 Root 权限从选中游戏读取经 Android 2.7.91 构建标识校验的战斗对象；按游戏帧采样、交叉复核、检测换局和结束，不修改游戏、不启动观察者。稀疏文件夹具验证计数槽与生命周期。
- [x] MuMuBattleFeed：绑定 GameTarget 的本机 ADB 序列号和已知游戏包，关闭源时回收会话；权限、离线、未知版本均不发布假数据。
- [x] BattleDataView：只切换采集入口，沿用 PC Present；房间、HUD、Debug 不把 MuMu 的伤害样本当作卫戍或房间内存。
- [x] 实机采样、重建 DEX 并校验、运行测试、记录边界；不自动操作玩家对局。
