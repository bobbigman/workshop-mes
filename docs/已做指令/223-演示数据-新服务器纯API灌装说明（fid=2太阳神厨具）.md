# 223 - 演示数据・新服务器纯 API 灌装说明（fid=2 太阳神厨具）

> 用途：在售前演示服务器 `https://103.39.222.133:11302`（账套 fid=2）造「真实感 + 紧张感」演示数据。
> 性质：纯演示模拟数据；**纯 API 灌装（无 DB 直连）**，与 221（本地 fid=5，SQL 直连）方法不同。
> 生成日期：2026-10-10。实测通过。

## 0. 与 221 的差异（为什么不能照搬）

221 用 SQL Server 直连做三件事：①回填时间线（造历史累积感）、②给 admin 补部门（红线2 报工权限）、③一键重置。**新服务器只能走 API（11302 端口，无 DB 直连），这三样废掉**。灌法改为纯 API，全部 POST；「紧张感」（超期/临期/异常/达成率梯度）由 `dueDate` + 报工量控制，不依赖历史时间回填。

## 1. fid=2 主数据（实测，2026-10-10）

| 对象 | id / 值 |
|---|---|
| 产品 | 3=DDQ-001 打蛋器；新建 10=CG-001 炒锅 / 11=TG-002 汤锅 / 12=GC-003 锅铲 / 13=CD-004 菜刀 / 14=LS-005 漏勺 |
| 工序 | 4 下料 / 5 冲压 / 6 打磨 / 7 质检 / 8 组装 / 9 包装 |
| 部门 | 12 冲压 / 13 打磨 / 14 质检 / 15 组装；包装挂 14 质检组 |
| 员工 | 6 admin（role=1，无部门）/ 7 张班长（仅冲压）/ 8 李师傅 / 9 王军 / 10 赵江陵 |
| 工艺路线 | routing=2 厨具标准线（下料→冲压→打磨→质检→组装→包装） |
| 单位 | unit=9 件 |
| 不良项 | 10 划伤 / 11 尺寸超差 / 12 毛刺 / 13 组装松动 / 14 包装破损 |
| 看板 | `/api/ReportStat/board`；不良排行 `/api/ReportStat/defect` |

## 2. 灌装步骤（纯 API，curl -k 跳过证书）

> 前置：`$token = "<fid=2 管理员 Bearer>"`；`$B = https://103.39.222.133:11302/api`；body 中文一律 UTF8 bytes 写临时文件再用 `curl --data-binary @file`。

1. **建 5 产品** `/api/Product`（code/name/unitId=9/routingId=2/supplier/price）。
2. **建 8 张 10 月工单** `/api/WorkOrder`（orderNo/productId/qty/dueDate）→ `/api/WorkOrder/{id}/transition` body `{"action":"start"}` 启动。
3. **控超期**：该账套 `dueDate <= 今天 且未完成 → overdue`。要凑「超期=2」，把其余单 `PUT /api/WorkOrder/{id}` body `{"dueDate":"明天"}` 推到明天。
4. **报工** `/api/Report` 单条（orderId/operationId/goodQty/defectQty/defectId），造达成率梯度。
5. **建异常** `/api/Abnormal`：**必须 multipart/form-data**（JSON body 不绑定，返回「异常类型无效」）；中文用 Python `http.client + ssl 跳过校验` 发（curl `-F` 中文会乱码）。
6. **建历史完成单**：`/api/WorkOrder` 建 N 张 + start + `/api/Report/batch` 一次报满一单 6 工序（allocations 按工序分配不同员工，自动置 status=2 完成）。

**batch DTO**（swagger 核实）：`{orderId, goodQty, allocations:[{operationId, users:[{userId,goodQty}]}], confirmUpstream}`。

**达成率梯度设计**（fid=2 工序 id）：炒锅300=40%、汤锅200=0%（卡死反例）、锅铲500=100%、菜刀400=70%、漏勺300=77%、打蛋器150=80%、炒锅200=100%、汤锅150=33%。

## 3. 结果（实测，2026-10-10）

- 看板 `ReportStat/board`：totalOrders 43 / **已完成 33** / 执行中 10 / **超期 2** / 临期 4 / **待处理异常 3**；良品 33,986 / 不良 20 / 不良率 0.06%。
- 异常 3 条（status=0 未恢复）：设备故障→汤锅、质量异常→炒锅、物料短缺→漏勺。
- 不良分布 `ReportStat/defect`：毛刺 7（46.7%）、划伤 6（40%）、尺寸超差 2（13.3%）——可画条形排行。
- 报工分散：历史单 batch 分配到 张班长/李师傅/王军/赵江陵，非全 admin。

## 4. 注意事项

1. **无 DB 直连**：报工 / 创建时间全是当下（2026-10-10），历史单时间戳为今天，无「9 月历史累积」观感；`dueDate` 仍是 9 月（已完成单 dueState=normal 不预警）。
2. **异常 multipart**：`curl -F` 传中文会乱码；用 Python `http.client`（`ssl._create_unverified_context()`）构造 UTF8 multipart。
3. **重置受限**：无 DELETE 工单端点、无 SQL，只能手工清理或走 API 反操作，**无一键重置**。
4. **报工权限**：admin(uid=6) 无部门仍能报（role=1 放行），无需补部门（与 fid=5 不同）。
5. **完成量口径**：doneQty = 各工序良品最小值；只报首尾工序会全 0%（同 221 的坑），必须给末道工序也补报。

## 5. 演示路径

打开 `https://103.39.222.133:11302`（fid=2）用 admin 登录 → 车间看板 / 生产执行总览：超期 2 + 临期 4 + 异常滚动墙 3 + 不良分布排行 + 达成率梯度。
