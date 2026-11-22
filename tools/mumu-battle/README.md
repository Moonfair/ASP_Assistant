# MuMu 战斗采集器

只向 ASPA 的 `BattleFrame` 提供当前战斗的游戏时间、结束状态及每名干员的四类累计伤害。不计算另一套 DPS，不读用户账号、房间或队友，不依赖 Python、ArkDPS 或 Frida。Windows 采集器与数据页布局不变。

## 数据来源与边界

- 结构参考 [ArkDPS 读数口径](https://github.com/ChildeRolando/ArkDPS/blob/e7eb83e5099e3829391f2350b4943997be18ad78/collector.py) 与其上游的 [Android 字段表](https://github.com/ChildeRolando/ArkDPS/blob/e7eb83e5099e3829391f2350b4943997be18ad78/vendor/ark-source/game_data/offsets/android_arm64.json)、[战局单例与构建标识](https://github.com/ChildeRolando/ArkDPS/blob/e7eb83e5099e3829391f2350b4943997be18ad78/vendor/ark-source/tools/enemy_health/battle_lifecycle.py)。ASPA 自行实现传输及采样，不分发上游 worker 或游戏文件。
- 仅对所选 MuMu 端点上的国服 / B 服游戏进程尝试读取。必须同时匹配游戏版本、ARM64 ABI、已加载镜像的 GNU Build ID 和对象类型；仅版本号相同并不足够。没有 Root 时不降级为猜测数据。
- 设备端每次接收一个 `poll`，返回一帧或明确状态；仅以只读方式打开目标进程。读取前后检查帧号、定点游戏时间、控制器和统计容器身份，跨帧最多重试三次。只保留 `char_` 键，六个累计槽只取 1/2/3/5，不计治疗。新战斗或时钟回退时重新计回合。没有启动服务，也不占用 TCP 转发端口。
- DEX 内嵌在 Core 中，经已授权的 ADB 会话放入 Root 私有临时目录。关闭采集会话后，标准输入关闭，读数进程退出并清理文件。不需要 Windows 管理员权限。

## 复核

正常构建 ASPA 不需要 JDK 或 Android SDK。修改采集器时，用 JDK 17、[Google Maven 的 R8 8.3.37](https://dl.google.com/dl/android/maven2/com/android/tools/r8/8.3.37/r8-8.3.37.jar) 重编并检查内嵌资源：

```powershell
./tools/mumu-battle/build.ps1 -R8Jar C:/tools/r8-8.3.37.jar
# 源码有意改变且要替换资源时，加 -Update。
dotnet test src/ASPAssistant.Tests --filter "FullyQualifiedName~MuMuBattleFeed|FullyQualifiedName~BattleDataViewTests"
dotnet run --project tools/mumu-battle/smoke -- 0 30
```

最后一条使用正式采集器，在当前运行的 MuMu 实例 0 上只读观察 30 秒；不自动操作游戏。退出码：0 有真实战斗帧；2 已连接但只观察到等待；3 未连通/不受支持。可在战斗中运行，查看真实帧号、时钟、干员数及总伤，结合原数据页验证暂停、倍速、结束与换局。
