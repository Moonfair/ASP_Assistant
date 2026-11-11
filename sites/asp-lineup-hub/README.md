# 卫戍阵容库

卫戍阵容库提供 ASP Assistant 阵容编辑、发布、搜索、筛选、讨论和评分。访客可以在网页组建阵容、导入和导出分享文本。使用邮箱验证码登录后，可以发布新阵容、编辑自己发布的阵容、匿名评论和评分。编辑保留原有链接、评论和评分。登录邮箱不会出现在公开页面或接口中。

支持 `ASPA-LINEUP/1` 和 `ASPA-LINEUP/2`。干员头像和盟约图标来自 ASP Assistant 自带数据。发布、评论和评分需要一次性图片验证码。每个账号对同一阵容保留一条可更新的评分。

侧边栏数据更新后，在网站目录运行 `npm run sync:catalog`，从仓库的 `data/` 更新干员、装备和核心盟约目录，再重新构建网站。分享文本保留干员顺序、两个装备槽、核心标记和阵容介绍源码。

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
