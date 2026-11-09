# workshop-data-sim · 接口现状与实测契约

维护日期：2026-10-25。本机开发环境（127.0.0.1:8080，DB=WorkshopMes）**实测**得出的契约。线上目标账套版本需再核对，不能直接照搬。

## 通用

- 所有 API 需 `Authorization: Bearer <管理员JWT>`；curl 需 `-k`（自签证书）。
- 返回信封 `{code, msg, data}`，code=0 成功；HTTP 200 ≠ 业务成功（看 code）。
- Windows PowerShell 传 JSON 会被吞引号 → 用 python urllib 或 `curl --data-binary @文件`；本技能脚本用 python 内置 urllib。

## 用户

- `POST /api/User` 返回 `data=null`（拿不到 id）→ 创建后 `GET /api/User?keyword=<account>` 回查 id。
- UserCreateDto：`{account, name, role(2=工人), userType:0, wageCoeff:1.0, password, deptIds:[部门id]}`。密码 BCrypt 由后端处理。
- 工人 deptIds 必须挂到对应工序的报工权限部门。

## 工序价目表

- 路由：`/api/operation-price-list`（小写，不是 OperationPriceList）。
- 状态机：`SaveAsync` 里草稿 details 完整（无待确认、行有金额、confirmed）时**保存即自动启用 status=1**；启用后不能再次 POST status（`仅停用表可以重新启用，草稿请保存`）。想停用再启用时：POST status=2 → 再 status=1。
- 唯一性：同工序「产品范围」只能有一张生效表——通用（productId=null）一张、专属（productId=某产品）一张可并存；同范围重复/日期重叠会拒绝。
- **专属表**（productId=某产品）优先级高于通用表（`SelectTable`：specific 优先，其次 common）。
- 创建入参 `details`：`{schemaVersion:2, requiredFieldIds:[], groups:[], rows:[{rowId,kind:"base",label,conditions:[],amount,deductPrice:0,groupKey:null,includedRowIds:[],confirmed:true,source,note}]}`。
- 报工单位 `reportUnitId`（产品把=16）须与产品报工单位一致，`unitsPerPriceUnit` 按计价换算（元/把=1）。
- 专属表取价可用无条件 base 行（`conditions:[]`），`requiredFieldIds:[]` → 完全绕开产品属性的规格校验（通用表带伞型/尺寸等条件才需要产品属性齐全）。
- 单张表只允许一种计价方式（1计件/2计时/3单笔），启用后锁定。

## 工单

- `POST /api/WorkOrder` 返回 `data=null` → `GET /api/WorkOrder?keyword=<orderNo>` 回查 id。
- WorkOrderCreateDto：`{productId, qty, orderNo, dueDate, ext}`。
- 新建/复制会自动 `BindAsync` 锁价到全部工序（写入 ProcessingPrice 快照）。
- **已知 Bug（2026-10-25 已修）**：`OperationPriceListService.Calculate` 构造 ProcessingQuote 时原漏填 `FactoryId`/`ProductId`（0），与 `ValidateQuote` 的 `FactoryId>0 && ProductId>0` 冲突，导致新建工单锁价必报「缺少新版价目表锁价依据」；已在第 206 行补 `FactoryId = table.FactoryId, ProductId = productId` 并重启。若换环境仍报此错，先检查该赋值是否还在。

## 报工

- `POST /api/Report`，ReportDto：`{orderId, operationId, goodQty, defectQty, source, confirmUpstream, assignees:[{userId, goodQty, defectQty, durationMinutes}]}`。
- **代报**：admin/班组长（role 1/3）通过 `assignees[]` 代报，顶层 goodQty 可为 0；admin 代报自动已复核（reviewStatus=1），满足工资生成"已复核未结算"前提。
- **软确认**：工序报工量超过紧邻上一道复核通过量时，返回 code=1002（"紧邻上一道不足，是否继续"）；DTO 加 `confirmUpstream:true` 即可放行。
- 同一工单工序已报良品合计不能超 planQty×超产系数；报工后不可轻易删（有复核/工资/变更日志引用）。

## 工资

- 生成：`POST /api/Salary/generate {periodType:1, periodValue:"2026-10"}`（按月）。前提=有已复核未结算报工。
- 回查：`GET /api/Salary?periodType=1&periodValue=2026-10&page=1&pageSize=30`，字段含 userName/totalAmount/status（0=已生成未发放）。

## 前端查看路径

- PC 管理员：`#/salary`（工资报表，adminOnly）、`#/salary-sku`。
- H5 工人端：`/h5/my-wage`（我的工资）。

## 远程 HY伞业V2 实测（2026-10-25，fid=5）

远程部署版 `https://103.39.222.133:11302`，全新库。与本地一致契约可复用，差异与坑如下：

- **产品**：HY-001 三折晴雨伞 id=22、HY-002 id=21（均把/unit=18）；HY-003 id=20（套/17）；HY-004 id=19（件/16，二次包装线）。远程「把」=unitId **18**（本机是 16），建价目表 reportUnitId 要对齐产品。
- **工序**：id 37大裁/38包边/39拉边/40验布/41小裁/42合片/43打带/44打顶/45打珠尾/46缝伞/47品检/48包装/49配件/50装伞柄。工艺路线 RT-HY 华煜晴雨伞标准线=大裁→包边→验布→小裁→打顶→合片→打带→配件→打珠尾→缝伞→品检→装伞柄→包装（13 步，**无拉边**，含打带/配件/装伞柄）。
- **⚠️ 关键坑**：远程 `/api/Report` 查询接口**忽略 `operationId` 过滤参数**——`GET /api/Report?orderId=67&operationId=46` 仍返回该工单全部（合片那 2 条），total>0 会**误判"该工序已报"而跳过**。判断工序是否已报不能用该查询；直接无条件 POST 补报即可（同一工序重复报会被后端拒绝或需先删）。
- **⚠️ 锁价必报缺价工序**：工单 `BindAsync` 会锁价到路线**全部工序**，缺任一道价目表即报「产品XX·工序XX无生效专属或通用价目表」。演示必须给产品路线**全部**工序建价目表（13 道都建，报工可只分 8 道）。
- **工资重新生成**：同一期间已生成草稿后再次 `POST /generate` 报「部分人员该周期工资单已存在，请先删除草稿」；需先 `DELETE /api/Salary/{id}` 清草稿，再 generate。工资生成只取**已复核未结算**报工（admin 代报自动已复核）。
- 远程用户 id：sim01~sim10=45~54（与本地 24~33 不同，建后回查）。
- 结果：13 张 HY-001 专属价目表 + 工单 HYWO-0001(10000把) + 10 工人报工 + 2026-10 工资 10 人 200.00~200.25。前端 `#/operation-price-list` 可见 13 表、`#/salary` 可见 10 人。
