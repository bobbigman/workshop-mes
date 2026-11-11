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

## 增量包交付方式（2026-10-20 定）

打包只产出 `backend\publish-update` 文件夹，**不打 zip**。
交付 = 直接拷整个 `publish-update` 文件夹到服务器 → 双击 `apply-update.bat`（自动停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）。

## 禁止

- 用开发机 appsettings / start.bat 整文件盖服务器。
- 把 `ASPNETCORE_URLS` 从 https 回退成 http（已上 HTTPS 的站点）。
- 把更新包里的 FDD `WorkshopMes.exe` / runtimeconfig / deps 拷进自包含安装目录。

## 版本号规则（2026-11-12 定稿，改打包脚本前必看）

原则：**版本号在打包入口定一次，编译只消费、不再递增。**

做法（ackend/pack-update.ps1 已是这个顺序，别倒回去）：

1. 1/5 先跑 ump-version.ps1，把 ackend/version.txt 与 rontend/package.json 同时 +1，拿到本次包版本；
2. 2/5 再 
pm run build——此时前端读到的是新号，登录页版本 = 包版本；
3. 3/5 再 dotnet publish -p:SkipVersionBump=true，编译不再自增；
4. 4/5 收尾以入口定下的号为准，并回查 DLL FileVersion 必须等于它，不等直接 throw。

反向顺序（先构建前端、后编译后端）会造成包里的前端、DLL、说明三处版本不一致，且打包后源码版本继续往前跑，开发机显示的号永远比发行包高——这是 1.1.538/539 错位的根因。

WorkshopMes.csproj 的 BumpPatchVersion 只在 Configuration=Release 时递增，dotnet run / Debug 编译不改版本号，所以开发机登录页显示的就是最近一次发行包版本。

验收（每次打包后核对）：前端 bundle 里的版本串、DLL FileVersion、README-update.txt 两处锚点，三处必须是同一个号。