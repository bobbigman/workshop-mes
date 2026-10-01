# WorkshopMes 部署更新铁律

> 长期遵守。细则与验收见 `docs/59-执行指令-部署更新铁律-appsettings不覆盖.md`。  
> 脚本闸门：`backend/pack-update.ps1`、`backend/apply-update.bat`。

## 一句话

**已上线机只用增量更新包；绝不拿整包 `publish-win64` 覆盖生产目录；绝不覆盖服务器上的 `appsettings*.json` 与 `start.bat`。**

## 首次整包（空目录）

- 用自包含 `publish-win64`，整夹拷到目标机。
- 此时写入的 `appsettings.json` / `start.bat` 即该机定稿。

## 日常更新（已定稿之后）

只覆盖：

- `wwwroot\`
- `*.dll`
- 可选 `web.config`

永不覆盖：

- `appsettings*.json`
- `start.bat` / `stop.bat`
- `certs\`、`logs\`
- `WorkshopMes.exe`、`*.runtimeconfig.json`、`*.deps.json`

新配置项：只把新键合并进服务器那份 appsettings，旧值不动，并记入更新说明。

`Instance:DisplayName`：填**中文厂名**（登录眉标/页头）；禁止 `WorkShop` 等英文代号。已上线机若仍显示英文，在服务器 appsettings 里手工改该键，勿整文件覆盖。

## 禁止

- 用开发机 appsettings / start.bat 整文件盖服务器。
- 把 `ASPNETCORE_URLS` 从 https 回退成 http（已上 HTTPS 的站点）。
- 把更新包里的 FDD `WorkshopMes.exe` / runtimeconfig / deps 拷进自包含安装目录。
