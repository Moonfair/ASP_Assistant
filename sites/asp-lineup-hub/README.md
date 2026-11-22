# 卫戍阵容库

卫戍阵容库提供 ASP Assistant 阵容编辑、发布、搜索、筛选、讨论和评分。访客可以在网页组建阵容、复制短链接或粘贴短链接导入。使用邮箱验证码登录后，可以发布新阵容、编辑自己发布的阵容、匿名评论和评分。编辑保留原有链接、评论和评分。登录邮箱不会出现在公开页面或接口中。

支持 `ASPA-LINEUP/1` 和 `ASPA-LINEUP/2`。干员头像和盟约图标来自 ASP Assistant 自带数据。发布、评论和评分需要一次性图片验证码。每个账号对同一阵容保留一条可更新的评分。

侧边栏数据更新后，在网站目录运行 `npm run sync:catalog`，从仓库的 `data/` 更新干员、装备和盟约规则，再重新构建网站。分享文本保留干员顺序、装备槽位、核心标记、精锐状态、盟约标签和阵容介绍源码。

网页和侧边栏可视化编辑器读取 `lineup` 和 `mermaid` 图块。手动位置使用 `%% aspa-layout N1 211 88` 随分享文本保存。较早版本的侧边栏会把这行当作注释，使用自动布局。

侧边栏点击「分享」会生成并复制当前阵容的快照链接，无需登录。阵容内容在作业站加密保存，不进入公开阵容列表；任何持有链接的人可以在 90 天内打开网页预览，或在桌面端和网页端粘贴导入。网站公开阵容详情原有的 `/s/…` 链接继续有效；点击「去使用」会复制短链接并尝试打开桌面端。上传公开阵容前，在侧边栏的「设置 → 卫戍阵容库」登录。

短链接口：`POST /api/share` 接收 JSON `{ "share": "ASPA-LINEUP/2\n…" }` 并返回 `{ "shortPath": "/s/u-…" }`。`GET /api/share/u-…` 解密并返回分享文本与短链路径；浏览器直接访问 `/s/u-…` 可打开阵容快照。

## 干员投票

桌面详情通过 `GET /api/operator-votes?season=…&operator=…` 读取公开汇总。登录后用同一路径提交 `{ "skill": 2 }` 或 `{ "equipment": ["雪祀之衣"] }`，用 `/api/operator-votes/mine` 查询两项是否已提交。重复提交返回原有投票已记录的状态。每个赛季、干员、账号和投票类型只计一次。汇总在 SQLite 中增量维护，服务端缓存 60 秒，桌面端按需读取并缓存 45 秒。

更新侧边栏赛季目录后，先在仓库根目录运行 `python data/fetch_operator_skills.py` 获取本体技能，再运行 `npm run sync:catalog`。作业站启动时自动创建投票所需的表和触发器。

## 游戏房间与招募

新版桌面端通过 `POST /api/desktop/match/live/{action}` 同步游戏中的卫戍协议队伍。任一成员启用插件即可显示实际房号、房主、昵称和就绪状态。成员进入相同对局后会自动同步画笔。没有启用插件的队友也占用游戏座位，策略、盟约和战术显示为「未知」。

游戏房主可在有空位的大厅粘贴游戏邀请并发布招募。房间外的玩家开始搜索后会获得一个候选邀请，在游戏中使用邀请、实际进房后才会出现在队员名单里。同一次搜索中跳过的房间不会再次出现。取消再搜索会重新选择。房满、开始游戏匹配、开局、房主变化、房主退出插件，或连续 12 秒没有收到房主的大厅上报，都会撤下招募。恢复空位后可由房主重新发布。

搜索首先考虑加入后仍有机会凑成两名兜底、两名攻坚的队伍。「随便」、未知战术和剩余座位都可以补位。每人可选最多四项策略、四项盟约，服务端逐项比较全部选择。开始搜索或房间连续招募满 60 秒后，对该候选放宽条件，先选战术超额人数较少的队伍，再选策略与盟约重复项较少的队伍。同等条件优先人多、战术互补、招募较早的房间。

`sync` 提交游戏状态、最多四席的实际名单及本人的偏好。在游戏里开始单人匹配时，状态可以是 `matching` 且没有房号。游戏对局生成新房号时，以对局名单核对并合并对应大厅。服务端将房号、玩家游戏标识、邀请和笔迹保存在当前进程的内存中。返回的成员 ID 和插件房间 ID 独立随机生成。部署使用一个 Node.js 实例，90 秒未同步的客户端会被清理。

`recruit`、`stop-recruit`、`force-start`、`mute` 和 `stroke` 携带当前的插件房间 ID。`search` 开始搜索，`next` 携带刚才展示的候选 ID，`cancel-search` 取消搜索。`leave` 结束当前随机凭证，之后的同步需要新凭证。客户端在同一凭证的请求中递增 `sequence` 以过滤延迟重试。房主可使用测试包的「强制开始」验证当前队伍的画笔。笔迹使用归一化坐标，按成员随机分配颜色，10 秒后消失，各成员可分别屏蔽其他插件队友的笔迹。

## 旧版四人匹配

侧窗的「匹配」页签通过 `/api/desktop/match/` 排队、确认和同步房间状态。玩家使用助手生成的一次性匹配凭证。服务端优先凑齐两名兜底、两名攻坚，并为意向为「随便」的玩家补位。最早等待的玩家达到 60 秒后，服务端会放宽条件。

自动匹配和自建队伍均接受 1 至 4 项擅长策略（`strategies`）和最多 4 项意向盟约（`covenants`）。空盟约列表表示「随便」。自动匹配会比较每位玩家的全部选择，优先组成策略和盟约互不重复的队伍。接口兼容旧版客户端的单选字段 `strategy` 和 `covenant`。

已有游戏房间时，玩家可在「匹配 → 自建队伍」中提交同一段游戏分享文本。最先提交的人是队长并默认就位，其他三人在游戏内进房后点击「我已就位」。四人全部就位即进入游戏中的标记与屏蔽界面。

等待队友加入时，使用测试包的队长也可点击「强制开始」，带当前房内的 1 至 4 人直接进入游戏。尚未点击「我已就位」的成员也会同步进入游戏状态，开局后房间停止接收新成员。

队列、房间邀请和笔迹保存在当前 Node.js 进程的内存中。部署时使用一个服务实例，重启会结束现有匹配。客户端持续轮询房间状态，服务端在 20 秒确认期结束时取消未确认齐的队伍，并清理超过 90 秒没有联系的成员。游戏中的笔迹保留 10 秒。

## 桌面反馈

桌面端的「设置 → 发送反馈」会将近期应用日志和更新失败日志压缩后提交到 `POST /api/desktop/feedback`。无需登录。桌面端最多打包 8 MiB 的原始日志，优先保留最近写入的内容。服务端收到 ZIP 后写入 COS，写入成功才返回反馈编号 `{ "id": "…" }`。可用该编号在 `aspa-log-1301636332` 的 `feedback/YYYY-MM-DD/<编号>.zip` 下定位日志，日期使用 UTC。

部署时在服务端 `.env` 设置 `COS_REGION=ap-guangzhou`，并提供对该私有桶的 `feedback/*` 有 `cos:PutObject` 权限的密钥。优先读取 `COS_SECRET_ID` 和 `COS_SECRET_KEY`。这两项都留空时使用现有的 `TENCENTCLOUD_SECRET_ID` 和 `TENCENTCLOUD_SECRET_KEY`。更新 Nginx 配置以允许反馈接口接收最多 9 MiB 的请求体。

## 本地开发

需要 Node.js 22.13 或更新版本。腾讯云容器使用 Node.js 24。

```powershell
npm ci
npm run dev
```

打开 [本地阵容库](http://127.0.0.1:4173/)。点「登录 / 注册」，填写邮箱和图片验证码。开发模式把邮件写到 `.site-data/mail/`，打开最新的 JSON 文件取出六位验证码。开发服务器仅监听本机回环地址。测试使用隔离数据库和测试发件器。

```powershell
npm test
```

运行时使用 SQLite，启动时自动应用 SQL 迁移。开发数据库位于 `.site-data/lineups.sqlite`。生产数据保存在 Docker Compose 的命名卷 `asp-lineup-hub_data`；重新构建容器会继续使用该卷。备份时可用 SQLite 的备份命令，也可以先停止应用再备份整个卷。

## 腾讯云部署

生产站点为 `https://aspa.moonfair.cn`，运行在腾讯云香港实例 `txy2`。部署目录为 `/opt/asp-lineup-hub`，应用仅映射到宿主机 `127.0.0.1:4173`，Nginx 负责 HTTPS。升级时保留服务器原有 `.env` 和 SQLite 数据卷，应用启动时自动应用迁移。

生产短链默认从现有 `AUTH_SECRET` 派生加密密钥。也可在首次上线前设置独立的 `SHARE_SECRET`（32 字节随机数据，Base64）。备份正在使用的密钥；替换后旧快照无法解密。若未配置任何密钥，短链服务不会保存明文。

服务器上的操作示例。先将交付包解压到 `/opt/asp-lineup-hub`，再运行：

```sh
cd /opt/asp-lineup-hub
install -m 600 .env.example .env   # 只在首次部署时执行
# 在服务器上编辑 .env，填写 MAIL_PROVIDER、AUTH_SECRET 和邮件配置
docker compose up -d --build
docker compose ps
curl -fsS http://127.0.0.1:4173/healthz
```

DNS 生效后执行以下命令。请将 Certbot 的联系邮箱替换为你自己的邮箱。

```sh
mkdir -p /opt/asp-lineup-hub/acme
install -m 644 deploy/nginx-acme.conf /etc/nginx/conf.d/asp-lineup-hub.conf
nginx -t && systemctl reload nginx
certbot certonly --webroot -w /opt/asp-lineup-hub/acme -d aspa.moonfair.cn --email you@example.com
install -m 644 deploy/nginx-https.conf /etc/nginx/conf.d/asp-lineup-hub.conf
nginx -t && systemctl reload nginx
curl -fsS https://aspa.moonfair.cn/healthz
```

确认现有 Certbot 自动续期任务会在续期后重载 Nginx。若使用其他子域名，同步修改 `PUBLIC_ORIGIN`、两份 Nginx 配置、证书路径和 DNS 记录。
