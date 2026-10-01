---
name: workshop-mes-deploy
description: >-
  Builds and ships the Workshop MES (车间小工单) as one self-contained Windows
  folder, or a smaller incremental update pack for an existing install: SQL
  Server backup/restore, win-x64 publish, Vue dist inside wwwroot, port 8080.
  Use when the user asks to deploy to another server, 部署包, 发布包,
  publish-win64, 更新包, publish-update, 增量更新, 自包含, 拷到新服务器,
  or to repeat the 2026-09-19 deploy.
---

# 车间小工单：拷到另一台 Windows 服务器

来源：2026-09-19 已在新服务器跑通（目录 `D:\WorkShop\backend\publish-win64`，监听 `http://0.0.0.0:8080`）。

默认形态：**一个文件夹、一个进程、一个端口 8080**。前端由后端托管，目标机不装 .NET、不装 IIS/Nginx。`docs/05` 里「前端单独 IIS/Nginx、目标机装 Hosting Bundle」是旧方案，用户没点名就不要用。

同一台机器开两个库、两个端口，走 `deploy/instance/远程双实例部署.md`，不要套本技能。

## 打部署包之前先问

**先提问，再执行任何构建命令。** 本轮对话里还没有这三项答案时，停下来问，不要用仓库里的 `appsettings.json`、也不要用上次部署的 IP / 库名 / 密码顶上。

一次问清：

1. IP 地址（伙伴/目标服务器，用来写 `PublicBaseUrl` 和验收访问地址）
2. MS SQL 数据库文件名（即库名，如 `WorkShopMes`，写入 `ConnectionStrings.Database`）
3. sa 密码

用户答完再往下做。密码只写进本次发布目录的 `appsettings.json`，不要写进本技能、不要改 `backend\appsettings.json`、不要提交 git。程序与 SQL 同机时连接串 `Server=localhost`；不同机时把 `Server` 换成用户指明的 SQL 机地址。

## 开发机打部署包

工作目录是仓库根。发布前清掉失效代理（本机 Clash 常把 `HTTP_PROXY`/`HTTPS_PROXY` 指到 `127.0.0.1:7890`，代理没开时 NuGet 会失败）：

```powershell
Remove-Item env:HTTP_PROXY -ErrorAction SilentlyContinue
Remove-Item env:HTTPS_PROXY -ErrorAction SilentlyContinue
Remove-Item env:ALL_PROXY -ErrorAction SilentlyContinue

Set-Location frontend
npm run build
if ($LASTEXITCODE -ne 0) { throw "frontend build failed" }

$www = Join-Path (Resolve-Path ..\backend) "wwwroot"
if (Test-Path $www) { Remove-Item $www -Recurse -Force }
New-Item -ItemType Directory -Path $www | Out-Null
Copy-Item .\dist\* $www -Recurse -Force

Set-Location ..\backend
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish-win64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
```

发布会清空输出目录，**必须在 publish 之后**做三件事。

1. 把 `backend\publish-win64\appsettings.json` 的 `ConnectionStrings.Default` 改成用户刚回答的库名和 sa 密码：

```text
Server=localhost;Database=用户给的库名;User Id=sa;Password=用户给的sa密码;TrustServerCertificate=True
```

不要留 `Trusted_Connection`。SQL 不在程序同机时，把 `localhost` 换成那台 SQL 的地址。

2. 同文件把 `Instance.PublicBaseUrl` 改成 `http://用户给的IP:8080`（未申请 HTTPS 时用 http，不要擅自改成 https）。

3. 再写入 `backend\publish-win64\start.bat`（优先从仓库里的 `backend\start.bat` 复制，保持客户机提示一致）：

```powershell
Copy-Item .\start.bat .\publish-win64\start.bat -Force
Copy-Item .\stop.bat .\publish-win64\stop.bat -Force
```

模板见 `backend\start.bat`、`backend\stop.bat`。

黑窗口启动后应能看到「正在连接数据库」「已启动」等字样（程序已写回控制台）。不要再以为「全黑=卡死」。

打完立刻核对，缺一项就不要交给用户拷走：

- `WorkshopMes.exe`
- `coreclr.dll`、`hostfxr.dll`（证明自包含，目标机不用装 .NET）
- `wwwroot\index.html` 和 `wwwroot\assets`
- `appsettings.json`
- `start.bat`、`stop.bat`

产物大约一百多 MB。必须整夹拷贝，不能只拷 exe。

## 更新铁律（已上线机，长期遵守）

详见仓库根 `DEPLOYMENT_NOTES.md` 与 `docs/59-执行指令-部署更新铁律-appsettings不覆盖.md`。

- **已上线机只用增量包**，禁止用整包 `publish-win64` 覆盖安装目录（会冲掉 HTTPS/连接串/服务器改过的 `start.bat`）。
- **永不覆盖**：`appsettings*.json`、`start.bat`、`stop.bat`、`certs\`、`logs\`、自包含的 `WorkshopMes.exe` / `*.runtimeconfig.json` / `*.deps.json`。
- **只覆盖**：`wwwroot\`、`*.dll`（及可选 `web.config`）。
- 新配置项：只把新键合并进服务器那份 appsettings，旧值不动，并记入更新说明。

## 更新包（已装过整包之后）

客户机已经有自包含 `publish-win64` 时，不要再拷一百多 MB。打增量更新包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File backend\pack-update.ps1
```

产物在 `backend\publish-update`（约几十 MB）。包内**没有** `appsettings*.json`、`start.bat`、`stop.bat`，也**没有**会破坏自包含的 FDD 版 `WorkshopMes.exe` / runtimeconfig / deps（脚本会校验并 throw）。

客户机：结束 `WorkshopMes.exe`（或 `stop.bat`）→ 拷过去 → 双击 `apply-update.bat` → 用**服务器原有** `start.bat` 启动。说明见 `README-update.txt`。

**禁止**把更新包里的 `WorkshopMes.exe` / `*.runtimeconfig.json` / `*.deps.json` 拷进安装目录。盖上去会出现 `You must install or update .NET`。若已误盖，用 `backend/publish-repair-runtime`（或整包里同名文件）盖回，并保留客户机 `appsettings.json` 与 `start.bat`。

用户说「更新包 / 增量 / 不用拷整个部署」时走本节；说「全新服务器 / 第一次部署」仍走上面整包。

## 数据库

引擎是本机 SQL Server。库名用提问时用户给的那个，不要默认写成 `WorkshopMes`。不要在服务占着文件时拷 `.mdf`/`.ldf`。

1. 开发机 SSMS（服务器 `localhost`，Windows 身份验证）右键该库 → 任务 → 备份，得到 `.bak`。
2. 新服务器先装好 SQL Server，SSMS 右键「数据库」→ 还原数据库。
3. 不要对公网开放 1433。

## 目标机安装（仅首次 / 空目录）

> 下面「覆盖整个文件夹」**只适用于首次装到空目录**。目录里已有定稿的 `appsettings.json` / HTTPS `start.bat` 时，改走上面「更新包」，不要整包覆盖。

1. 先停掉正在跑的黑窗口（Ctrl+C），再把整包拷进空目录（或全新路径）。上次落地路径是 `D:\WorkShop\backend\publish-win64`。
2. 覆盖后看一眼 `appsettings.json`：`Database`、sa 密码、`PublicBaseUrl`（应是提问时的 IP:8080）应已是提问时填的那组。若仍是 `Trusted_Connection`，说明发布后没改连接串，先改再启动。
3. 双击 `start.bat`。黑窗口保持开着，关了进程就停。若该机已改成 HTTPS，以服务器上那份 `start.bat` / appsettings 为准，不要从开发机再盖一次。
4. 启动过程中窗口应有中文提示；成功时出现「已启动」。故障时看退出码，并打开同目录 `logs\mes-error-*.log`。
5. 浏览器能打开登录页即服务正常。

防火墙（局域网打不开时）：

```powershell
New-NetFirewallRule -DisplayName "WorkshopMes-8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

## 验收

| 打开 | 应看到 |
|---|---|
| `http://localhost:8080/` | 登录页 |
| `http://localhost:8080/swagger` | 接口文档（`EnableSwagger` 为 true 时） |
| `http://新服务器IP:8080/` | 同一登录页 |
| `http://新服务器IP:8080/#/h5/scan` | 扫码报工 |
| `http://新服务器IP:8080/#/board` | 车间看板 |

`ERR_CONNECTION_REFUSED` = 8080 上没有进程。去目标目录重新双击 `start.bat`。窗口一闪就关，把窗口里的红字贴回来，不要改端口或改成 IIS 来绕。

前端 `baseURL` 是 `/api`，和页面同源，不要再配 Nginx 反代或单独的 API 地址。
