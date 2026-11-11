# 卫戍阵容库

卫戍阵容库提供 ASP Assistant 阵容编辑、发布、搜索、筛选、讨论和评分。访客可以在网页组建阵容、复制短链接或粘贴短链接导入。使用邮箱验证码登录后，可以发布新阵容、编辑自己发布的阵容、匿名评论和评分。编辑保留原有链接、评论和评分。登录邮箱不会出现在公开页面或接口中。

支持 `ASPA-LINEUP/1` 和 `ASPA-LINEUP/2`。干员头像和盟约图标来自 ASP Assistant 自带数据。发布、评论和评分需要一次性图片验证码。每个账号对同一阵容保留一条可更新的评分。

侧边栏数据更新后，在网站目录运行 `npm run sync:catalog`，从仓库的 `data/` 更新干员、装备和核心盟约目录，再重新构建网站。分享文本保留干员顺序、两个装备槽、核心标记和阵容介绍源码。

网页和侧边栏可视化编辑器读取 `lineup` 和 `mermaid` 图块。手动位置使用 `%% aspa-layout N1 211 88` 随分享文本保存。较早版本的侧边栏会把这行当作注释，使用自动布局。

侧边栏点击「分享」会生成并复制当前阵容的快照链接，无需登录。阵容内容在作业站加密保存，不进入公开阵容列表；任何持有链接的人可以在 90 天内打开网页预览，或在桌面端和网页端粘贴导入。网站公开阵容详情原有的 `/s/…` 链接继续有效；点击「去使用」会复制短链接并尝试打开桌面端。上传公开阵容前，在侧边栏的「设置 → 卫戍阵容库」登录。

短链接口：`POST /api/share` 接收 JSON `{ "share": "ASPA-LINEUP/2\n…" }` 并返回 `{ "shortPath": "/s/u-…" }`。`GET /api/share/u-…` 解密并返回分享文本与短链路径；浏览器直接访问 `/s/u-…` 可打开阵容快照。

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
