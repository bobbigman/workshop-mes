# 212-优化指令：pack-update 脚本补漏（清调试残留 _*.json 与嵌套历史目录）

> 状态：已做（2026-10-03 豆包执行，验收通过）
> 性质：正式生产（打包脚本直接影响发往客户机的增量包洁净度，属部署可靠性）
> 目标：让【执行方 Cursor】通过【改 `backend/pack-update.ps1` 的清理段】得到【重跑打包后包内不再混入调试 json 与嵌套目录】的可观察结果
> 依据/拍板：2026-10-03 黑帽大人拍板；来源为当日实跑 `pack-update.ps1` 产物 `backend/publish-update` 复验发现两处漏网

---

## 取舍（定死，拍板记录在此）
| 项 | 定案 |
| 做/不做 | 做：只在 `pack-update.ps1` 清理段加两条递归清理，并保持闸门校验 |
| 复用还是另写 | 复用现有清理/校验结构（脚本第 38-53 行清删、第 56-63 行闸门），不另写新脚本 |
| PC / H5 | 不涉及前端组件；脚本本身是 PowerShell |
| 挂载/入口时机 | 不涉及页面入口 |

## 本次范围与落点
- 交付：重跑 `pack-update.ps1` 后，`publish-update` 包内 **`_*.json` 数 = 0、嵌套历史目录数 = 0**，其余内容不变。
- 已有 / 缺口（实测证据，2026-10-03）：
  - 脚本第 51 行只删 `global.json`、`_mcp_body.json`、`_t.json`、`*.staticwebassets*.json`；
  - **缺口 1**：`dotnet publish` 把 `backend` 根下 40 个接口调试响应 `_*.json`（`_login*.json`、`_salary*.json`、`_items*.json`、`_sl_*.json` 等）带进包顶层，脚本没清；
  - **缺口 2**：包内出现嵌套历史目录 `_verify_build_81\publish-update`（前次验证构建残渣），脚本只删 `publish-win64/_build_out`，没删 `_verify_*`。
- 数据与契约：不改表、不改接口、不改 `appsettings` 清理逻辑与闸门白名单（appsettings/start/stop/exe/runtimeconfig/deps 维持现有删除+throw 行为）。
- 文档：无需改 `docs/02`；可选在 `DEPLOYMENT_NOTES.md` 或技能注释补一句，但不强求。

## 行为与口径（分叉写成显式条件）
- 当【输出目录 `$out` 出现任意 `_*.json` 文件（顶层或嵌套，含 wwwroot 下）】→ 一律递归删除，不留白名单例外。
- 当【输出目录出现 `_verify_*` 之类非程序本体目录】→ 递归删除（参照现有 `publish-win64/_build_out` 处理方式）。
- 当【删除后仍检出敏感文件（appsettings/start/stop/exe/runtimeconfig/deps）】→ 维持现有闸门 throw，不交付。
- 清理与闸门校验顺序不变：先清、后验、再写 apply/readme。

## 止步线
- 不重写打包/部署流程；不改 `apply-update.bat`；不改服务器端行为；不做增量差分、不做压缩。
- 后置项不预建：新开关、新参数、新配置文件、测试脚本。

## 验收（≤5 条，可观察，集中验收）
- [ ] 重跑 `pack-update.ps1` 成功，输出 `OK: .../publish-update`，无 throw。
- [ ] 包内递归扫描 `_*.json` 数 = 0（含 wwwroot 下）。
- [ ] 包内无 `_verify_*` / `publish-win64` / `_build_out` 等嵌套目录。
- [ ] 敏感文件闸门仍生效：包内无 `appsettings*.json`、`start.bat`、`stop.bat`、`WorkshopMes.exe`、`*.runtimeconfig.json`、`*.deps.json`。
- [ ] `wwwroot/index.html`、`apply-update.bat`、`README-update.txt`、各 `*.dll` 正常存在，包体约 40MB 级。
- 节奏：改完跑一次 `pack-update.ps1` 集中验收，失败才定点修；不边改边全量验。

## 交付终点
- 附重跑后的实际清点结果（`_*.json` 数、嵌套目录数、敏感文件数）；验收满足即交付，不追加与目标无关的优化。
- 现场服务器联调不适用（纯开发机打包脚本），无需标注。

---

## 执行记录（2026-10-03，豆包执行）
- 改动：`backend/pack-update.ps1` 历史产物段追加删 `_verify_*` 目录；调试残留段追加删 `_*.json`（递归，含 wwwroot）。
- 重跑结果：`OK: .../publish-update`，版本 1.1.192，约 40 MB。
- 独立复验（非脚本自身校验）：敏感文件 0 / 包内 `_*.json` 0 / wwwroot 下 `_*.json` 0 / 嵌套残留目录 0 / `index.html`+`apply-update.bat`+`README-update.txt`+`web.config` 均在 / dll 78。验收全部勾选通过。
