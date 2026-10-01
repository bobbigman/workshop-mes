# 59-执行指令-部署更新铁律（appsettings / start.bat 不覆盖）

> 写给：Cursor（及后续任何做部署的人）· 长期遵守  
> 日期：2026-09-23 · 已按脚本落地

## 项目
WorkshopMes 车间 MES——**已上线机日常更新铁律**

权威摘要也见仓库根 `DEPLOYMENT_NOTES.md`；脚本闸门：`backend/pack-update.ps1`、`backend/apply-update.bat`；AI 部署走 `.cursor/skills/workshop-mes-deploy`。

---

## 1. 首次整包 vs 日常更新（先分清）

| 场景 | 用什么 | 能否覆盖 appsettings / start.bat |
|---|---|---|
| 全新服务器、空目录第一次装 | 整包 `publish-win64` | 可以（本来就是这次定稿） |
| **已上线机发新版本** | **仅** `publish-update` + `apply-update.bat` | **禁止** |

**禁止**把整包 `publish-win64` 覆盖到已上线安装目录——会冲掉 HTTPS、连接串、密钥，以及服务器已改过的 `start.bat`。

---

## 2. 服务器上这些是「本地定稿、更新绝不覆盖」

- `appsettings*.json`（连接串、PublicBaseUrl、Kestrel 证书路径等）
- `start.bat` / `stop.bat`（伙伴机常设 `ASPNETCORE_URLS=https://0.0.0.0:8080`）
- `certs\`（证书跟域名走）
- `logs\`
- `WorkshopMes.exe`、`*.runtimeconfig.json`、`*.deps.json`（自包含运行时；更新包是 FDD，盖上去会报「须安装 .NET」）

> 例：伙伴站 `https://mesgd.cn:11302` 的 HTTPS 已写进服务器那份配置；仓库里的 `start.bat` 仍是 `http://0.0.0.0:8080`。覆盖即回退。

证书目录参考（该站）：`D:\WorkShop\backend\certs\mesgd\`

---

## 3. 日常更新只覆盖程序本体

- `wwwroot\`（前端）
- 各 `*.dll`（业务程序集）
- 明确需要的 `web.config`（若有）

**不**覆盖第 2 节清单里的任何文件。

---

## 4. 唯一例外：新配置键合并

仅当新版本**新增了配置项**时：

1. 对比仓库/发布机与服务器 `appsettings.json` 的键差；
2. **只加新键、不动旧值**（连接串、证书、PublicBaseUrl 等一律保留服务器现有值）；
3. 合并后做 JSON 合法性校验再重启；
4. 在更新说明里记：加了哪些键、默认值是什么。

禁止「开发机整文件盖过去再改回来」。

---

## 5. 脚本必须遵守（已落地）

- `pack-update.ps1`：打出的包**不含** `appsettings*`、`start.bat`、`stop.bat`、exe/runtimeconfig/deps、certs、logs；缺省校验会 throw。
- `apply-update.bat`：只拷 `wwwroot` + `*.dll`（及可选 `web.config`）；**不**拷 start/stop/appsettings。

改部署脚本时保持上述行为，勿重新加回「copy start.bat」。

---

## 验收勾选

- [ ] 更新包内无 `appsettings.json`、无 `start.bat`、无 `WorkshopMes.exe`
- [ ] 更新前后服务器 `appsettings.json` / `start.bat` 的修改时间与内容未变
- [ ] `ASPNETCORE_URLS` 仍为服务器定稿（HTTPS 站不得变回 http）
- [ ] 若有新配置键：已手工合并且 JSON 合法，更新说明已记录
- [ ] 启动正常；未出现「You must install or update .NET」

## 附：参考

- 服务器 HTTPS 全流程：`58-执行指令-远程服务器HTTPS证书上线.md`
- 操作手册：`docs/https配置/mesgd_https_完整操作手册.md`
