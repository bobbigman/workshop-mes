# 64 · API 参考 —— 厨具五金厂构建全流程

> 本文件是 63 号指令的执行记录，包含每一步的完整请求/响应报文，供 Cursor 或任意 API 客户端复现。

---

## 0. 前置约定

- Base URL：`http://localhost:8080`
- 统一返回格式：`{ "code": 0, "msg": "ok", "data": ... }`
  - `code=0` 表示成功，`code≠0` 表示业务错误
  - `msg` 在失败时包含具体错误信息
- 所有分页接口用 `?pageSize=N` 控制返回条数
- 路由命名规则：PascalCase 单数（`/api/WorkOrder` 不是 `/api/workorders`）
- 认证方式：Header `Authorization: Bearer {token}`

---

## 1. 建厂 `POST /api/factories`

需要管理员 token，先登录已有账套。

### 1a. 前置：登录 F001 获取管理员 token

```
POST /api/auth/login
Content-Type: application/json

{
  "FactoryCode": "F001",
  "Account": "admin",
  "Password": "Admin123"
}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "name": "总管理员",
    "role": 1,
    "factoryCode": "F001",
    "factoryName": "黑湖川菜馆"
  }
}
```

**失败示例 (200, code≠0)：**
```json
{ "code": 1, "msg": "账号不存在或已停用", "data": null }
```

### 1b. 建厂

```
POST /api/factories
Content-Type: application/json
Authorization: Bearer {F001的token}

{
  "FactoryCode": "CJ001",
  "FactoryName": "厨具五金厂"
}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "factoryCode": "CJ001",
    "factoryName": "厨具五金厂"
  }
}
```

**失败示例：**
```json
{ "code": 1, "msg": "账套代码已存在，请换一个", "data": null }
```

**关键副作用：** 建厂成功后自动灌入种子数据，包含：
- 3 个用户（admin / banzhang / gongren，密码 `Admin123`）
- 3 个部门（冲压组 CHONG / 打磨组 DA / 质检组 JIAN）
- 2 个单位（件 / 套）
- 3 个不良品项（划伤 / 尺寸超差 / 毛刺）
- 4 道工序（下料 XC / 冲压 CY / 打磨 DM / 质检 ZJ）
- 1 条路线（RT-WJ 五金件标准线）
- 2 个产品（WJ-001 / WJ-002）
- 1 张演示工单 DEMO001
- 2 个自定义字段（批次 / 材质）

---

## 2. 登录 CJ001 `POST /api/auth/login`

```
POST /api/auth/login
Content-Type: application/json

{
  "FactoryCode": "CJ001",
  "Account": "admin",
  "Password": "Admin123"
}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "name": "管理员",
    "role": 1,
    "factoryCode": "CJ001",
    "factoryName": "厨具五金厂"
  }
}
```

> 此后所有请求均带 `Authorization: Bearer {此token}`

---

## 3. 清理种子旧数据

必须按删除倒序：**工单 → 产品 → 路线**。

### 3a. 查询工单 `GET /api/WorkOrder?pageSize=50`

```
GET /api/WorkOrder?pageSize=50
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      {
        "id": 4,
        "orderNo": "DEMO001",
        "productCode": "WJ-001",
        "productName": "五金冲压件A",
        "qty": 2,
        "planQty": 2,
        "doneQty": 0,
        "remainQty": 2,
        "progressPercent": 0,
        "progressHint": null,
        "status": 1,
        "dueDate": null,
        "dueState": "normal",
        "createdAt": "2026-09-24T...",
        "ops": [
          { "operationName": "下料", "planQty": 2, "doneQty": 0 },
          { "operationName": "冲压", "planQty": 2, "doneQty": 0 },
          { "operationName": "打磨", "planQty": 2, "doneQty": 0 },
          { "operationName": "质检", "planQty": 2, "doneQty": 0 }
        ],
        "ext": { "10": "第一批", "11": "304不锈钢" }
      }
    ],
    "total": 1
  }
}
```

### 3b. 删除工单 `DELETE /api/WorkOrder/{id}`

```
DELETE /api/WorkOrder/4
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

### 3c. 查询产品 `GET /api/Product?pageSize=50`

```
GET /api/Product?pageSize=50
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      { "id": 5, "code": "WJ-002", "name": "五金冲压件B", "unitName": "件", "routingName": "五金件标准线" },
      { "id": 4, "code": "WJ-001", "name": "五金冲压件A", "unitName": "件", "routingName": "五金件标准线" }
    ],
    "total": 2
  }
}
```

### 3d. 删除产品 `DELETE /api/Product/{id}`

```
DELETE /api/Product/5
DELETE /api/Product/4
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**失败示例（外键约束）：**
```json
{
  "code": 1,
  "msg": "该产品下有关联工单，请先删除工单",
  "data": null
}
```

### 3e. 查询路线 `GET /api/Routing?pageSize=50`

```
GET /api/Routing?pageSize=50
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      {
        "id": 2,
        "code": "RT-WJ",
        "name": "五金件标准线",
        "stepNames": "下料 → 冲压 → 打磨 → 质检"
      }
    ],
    "total": 1
  }
}
```

### 3f. 删除路线 `DELETE /api/Routing/{id}`

```
DELETE /api/Routing/2
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

---

## 4. 查询现有资源 ID

以下四个查询并行执行，获取后续构建所需的 ID。

### 4a. 查询部门 `GET /api/Department?pageSize=50`

```
GET /api/Department?pageSize=50
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      { "id": 6, "code": "CHONG", "name": "冲压组", "members": "张班长、李师傅" },
      { "id": 7, "code": "DA", "name": "打磨组", "members": "" },
      { "id": 8, "code": "JIAN", "name": "质检组", "members": "" }
    ],
    "total": 3
  }
}
```

> 记下 `JIAN` 的 id=8（包装工序挂到质检组）

### 4b. 查询工序 `GET /api/Operation?pageSize=50`

```
GET /api/Operation?pageSize=50
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      { "id": 9, "code": "ZJ", "name": "质检", "deptNames": "质检组", "defectNames": "" },
      { "id": 8, "code": "DM", "name": "打磨", "deptNames": "打磨组", "defectNames": "划伤" },
      { "id": 7, "code": "CY", "name": "冲压", "deptNames": "冲压组", "defectNames": "尺寸超差、毛刺" },
      { "id": 6, "code": "XC", "name": "下料", "deptNames": "冲压组", "defectNames": "" }
    ],
    "total": 4
  }
}
```

> 记下 XC=6, CY=7, DM=8, ZJ=9

### 4c. 查询单位 `GET /api/Unit`

```
GET /api/Unit
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": [
    { "id": 3, "name": "件" },
    { "id": 4, "name": "套" }
  ]
}
```

> 记下「件」id=3

### 4d. 查询自定义字段 `GET /api/CustomField?target=work_order`

```
GET /api/CustomField?target=work_order
Authorization: Bearer {CJ001 token}
```

**成功返回 (200)：**
```json
{
  "code": 0,
  "msg": "ok",
  "data": [
    {
      "id": 7,
      "factoryId": 2,
      "target": "work_order",
      "fieldName": "批次",
      "fieldType": "single",
      "showInOrderList": false,
      "options": [
        { "id": 11, "fieldId": 7, "label": "第一批", "seq": 1 },
        { "id": 12, "fieldId": 7, "label": "第二批", "seq": 2 }
      ]
    },
    {
      "id": 8,
      "factoryId": 2,
      "target": "work_order",
      "fieldName": "材质",
      "fieldType": "single",
      "showInOrderList": false,
      "options": [
        { "id": 13, "fieldId": 8, "label": "304不锈钢", "seq": 1 },
        { "id": 14, "fieldId": 8, "label": "201不锈钢", "seq": 2 }
      ]
    }
  ]
}
```

> 记下「批次」id=7、「材质」id=8

---

## 5. 新建组装组 `POST /api/Department`

```
POST /api/Department
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{
  "Code": "ZZ",
  "Name": "组装组",
  "MemberIds": []
}
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**获取 ID：** 再调 `GET /api/Department?pageSize=50`，找到 `code="ZZ"` 的记录，记下 id。

---

## 6. 新建不良品项 `POST /api/DefectItem`

```
POST /api/DefectItem
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{ "Name": "组装松动" }
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

```
POST /api/DefectItem
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{ "Name": "包装破损" }
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**获取 ID：** 调 `GET /api/DefectItem` 找到 `name="组装松动"` 和 `name="包装破损"` 的 id。

---

## 7. 新建工序 `POST /api/Operation`

### 7a. 组装 ZZ

```
POST /api/Operation
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{
  "Code": "ZZ",
  "Name": "组装",
  "DeptIds": [15],
  "DefectIds": [16]
}
```

> DeptIds 填组装组 ID，DefectIds 填「组装松动」ID

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**失败示例（部门不存在）：**
```json
{ "code": 1, "msg": "报工权限部门不存在", "data": null }
```

### 7b. 包装 BZ

```
POST /api/Operation
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{
  "Code": "BZ",
  "Name": "包装",
  "DeptIds": [8],
  "DefectIds": [17]
}
```

> DeptIds 填质检组 ID，DefectIds 填「包装破损」ID

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**获取 ID：** 调 `GET /api/Operation?pageSize=50` 找到 `code="ZZ"` 和 `code="BZ"`。

---

## 8. 新建工艺路线 `POST /api/Routing`

```
POST /api/Routing
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{
  "Code": "RT-CJ",
  "Name": "厨具标准线",
  "Steps": [
    { "OperationId": 6,  "Seq": 1 },
    { "OperationId": 7,  "Seq": 2 },
    { "OperationId": 8,  "Seq": 3 },
    { "OperationId": 9,  "Seq": 4 },
    { "OperationId": 10, "Seq": 5 },
    { "OperationId": 11, "Seq": 6 }
  ]
}
```

> OperationId 依次为：XC / CY / DM / ZJ / ZZ / BZ

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**失败示例（缺少工序）：**
```json
{ "code": 1, "msg": "请至少添加一道工序", "data": null }
```

**验证：** `GET /api/Routing?pageSize=50`

```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      {
        "id": 3,
        "code": "RT-CJ",
        "name": "厨具标准线",
        "stepNames": "下料 → 冲压 → 打磨 → 质检 → 组装 → 包装"
      }
    ],
    "total": 1
  }
}
```

---

## 9. 新建产品 `POST /api/Product`

```
POST /api/Product
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{
  "Code": "DDQ-001",
  "Name": "打蛋器",
  "UnitId": 3,
  "RoutingId": 3
}
```

> UnitId=「件」ID, RoutingId=RT-CJ 的 ID

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**失败示例（编号重复）：**
```json
{ "code": 1, "msg": "产品编号不可重复", "data": null }
```

---

## 10. 建工单 `POST /api/WorkOrder`

不传 `OrderNo`，系统自动生成 `GD{日期}{三位序号}` 格式。

```
POST /api/WorkOrder
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{
  "ProductId": 11,
  "Qty": 5,
  "Ext": {
    "7": "第一批",
    "8": "304不锈钢"
  }
}
```

> ProductId=打蛋器 ID, Ext 的 key 是自定义字段 ID（字符串），value 是选项 label

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**失败示例：**
```json
{ "code": 1, "msg": "数量必须大于0", "data": null }
```

```json
{ "code": 1, "msg": "产品不存在", "data": null }
```

**验证：** `GET /api/WorkOrder?pageSize=10`

```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [
      {
        "id": 6,
        "orderNo": "GD20260924001",
        "productCode": "DDQ-001",
        "productName": "打蛋器",
        "qty": 5,
        "planQty": 5,
        "doneQty": 0,
        "remainQty": 5,
        "progressPercent": 0,
        "status": 0,
        "dueDate": null,
        "dueState": "normal",
        "createdAt": "2026-09-24T...",
        "ops": [
          { "operationName": "下料", "planQty": 5, "doneQty": 0 },
          { "operationName": "冲压", "planQty": 5, "doneQty": 0 },
          { "operationName": "打磨", "planQty": 5, "doneQty": 0 },
          { "operationName": "质检", "planQty": 5, "doneQty": 0 },
          { "operationName": "组装", "planQty": 5, "doneQty": 0 },
          { "operationName": "包装", "planQty": 5, "doneQty": 0 }
        ],
        "ext": { "7": "第一批", "8": "304不锈钢" }
      }
    ],
    "total": 1
  }
}
```

---

## 11. 启动工单 `POST /api/WorkOrder/{id}/transition`

```
POST /api/WorkOrder/6/transition
Content-Type: application/json
Authorization: Bearer {CJ001 token}

{ "Action": "start" }
```

**成功返回 (200)：**
```json
{ "code": 0, "msg": "ok", "data": null }
```

**失败示例（状态不允许）：**
```json
{ "code": 1, "msg": "仅未开始工单可开始", "data": null }
```

**状态流转速查：**

| Action | 前置状态 | 目标状态 | 说明 |
|---|---|---|---|
| `start` | 0 (未开始) | 1 (执行中) | 启动工单 |
| `finish` | 1 (执行中) | 2 (已结束) | 完成工单 |
| `withdraw` | 2 → 1, 1 → 0 | 回退 | 撤回 |
| `cancel` | 0/1 | 3 (已取消) | 取消工单 |
| `restore` | 3 (已取消) | 0 (未开始) | 恢复工单 |

---

## 12. 验收清单

| 序号 | 验证接口 | 判定标准 |
|---|---|---|
| 1 | `GET /api/factories` | 含 `factoryCode=CJ001, factoryName=厨具五金厂` |
| 2 | `GET /api/Product?pageSize=50` | 只有 `DDQ-001`，无 `WJ-*` |
| 3 | `GET /api/Routing?pageSize=50` | 只有 `RT-CJ`，无 `RT-WJ` |
| 4 | `GET /api/Operation?pageSize=50` | 含 ZZ(组装)、BZ(包装)，deptNames 正确 |
| 5 | `GET /api/WorkOrder?pageSize=10` | 1 条工单，orderNo 不含 DEMO，status=1 |
| 6 | 登录 F001，查工单 | F001 原有数据完整 |

---

## 附：踩坑记录

1. **路由命名**：ASP.NET Core 默认按控制器名路由，`WorkOrderController` → `/api/WorkOrder`（PascalCase 单数）。全小写/复数 → 404。
2. **建厂需管理员 token**：`POST /api/factories` 接口标记 `[Authorize]`，必须先登录已有账套获取 token。
3. **删除倒序**：工单 → 产品 → 路线，否则外键约束报错。
4. **`Ext` 字段 key 是字符串**：`WorkOrderCreateDto.Ext` 类型是 `Dictionary<long, string>`，但 JSON 传输时 key 必须写成字符串 `"7"` 而非数字 `7`。
5. **PowerShell 5.1 中文编码**：`ConvertTo-Json` 对非 ASCII 转 `\uXXXX`，DB 存乱码。推荐 Python `requests` 库发 UTF-8。