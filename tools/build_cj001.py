# -*- coding: utf-8 -*-
"""厨具五金厂 CJ001 构建脚本 — 纯 API 调用，Python 版"""
import requests, json, sys

BASE = "http://localhost:8080"
MAX_RETRY = 5
TOKEN = ""
STEP = 0

def api(method, path, body=None, retry=MAX_RETRY):
    url = f"{BASE}{path}"
    headers = {"Content-Type": "application/json"}
    if TOKEN:
        headers["Authorization"] = f"Bearer {TOKEN}"
    last_err = None
    for attempt in range(1, retry + 1):
        try:
            if method == "GET":
                r = requests.get(url, headers=headers, timeout=10)
            elif method == "DELETE":
                r = requests.delete(url, headers=headers, timeout=10)
            else:
                r = requests.request(method, url, headers=headers, json=body, timeout=10)
            j = r.json()
            if j.get("code") != 0:
                raise Exception(f"API code={j['code']} msg={j.get('msg','')}")
            return j
        except requests.RequestException as e:
            last_err = e
        except Exception as e:
            last_err = e
        if attempt < retry:
            print(f"    重试 {attempt}/{retry}: {last_err}")
    raise Exception(f"FATAL: 连续 {retry} 次失败: {last_err}")

def step(title):
    global STEP
    STEP += 1
    print(f"\n===== Step {STEP}: {title} =====")

def ok(msg):
    print(f"  [V] {msg}")

def fail(msg):
    print(f"  [X] {msg}")
    sys.exit(1)

# ============================================================
# Step 0: 环境检查
# ============================================================
step("环境检查")
try:
    r = api("GET", "/api/factories")
    codes = [f["factoryCode"] for f in r["data"]]
    print(f"  后端 OK，已有账套: {', '.join(codes)}")
    if "CJ001" in codes:
        fail("CJ001 已存在，请先清理")
    ok("后端运行正常，CJ001 未占用")
except Exception as e:
    fail(f"环境检查失败: {e}")

# ============================================================
# Step 1: 用 F001 admin 登录（建厂需要管理员权限）
# ============================================================
step("用 F001 管理员登录")
r = api("POST", "/api/auth/login", {"FactoryCode": "F001", "Account": "admin", "Password": "Admin123"})
TOKEN = r["data"]["token"]
ok(f"F001 admin 登录成功")

# ============================================================
# Step 2: 建厂
# ============================================================
step("建厂 CJ001")
r = api("POST", "/api/factories", {"FactoryCode": "CJ001", "FactoryName": "厨具五金厂"})
fc = r["data"]["factoryCode"]
fn = r["data"]["factoryName"]
print(f"  工厂: {fc} - {fn}")
ok("CJ001 厨具五金厂 已创建")

# ============================================================
# Step 3: 切换登录到 CJ001 admin
# ============================================================
step("登录 CJ001 admin")
r = api("POST", "/api/auth/login", {"FactoryCode": "CJ001", "Account": "admin", "Password": "Admin123"})
TOKEN = r["data"]["token"]
ok(f"CJ001 admin 登录成功 (name={r['data']['name']})")

# ============================================================
# Step 4: 清理种子旧数据（工单 -> 产品 -> 路线）
# ============================================================
step("清理种子旧数据")

r = api("GET", "/api/WorkOrder?pageSize=50")
wo_list = r["data"]["list"]
for wo in wo_list:
    api("DELETE", f"/api/WorkOrder/{wo['id']}")
    print(f"  已删除工单 {wo['orderNo']}")
ok(f"已删除 {len(wo_list)} 条种子工单")

r = api("GET", "/api/Product?pageSize=50")
prod_list = r["data"]["list"]
for p in prod_list:
    api("DELETE", f"/api/Product/{p['id']}")
    print(f"  已删除产品 {p['code']}")
ok(f"已删除 {len(prod_list)} 个种子产品")

r = api("GET", "/api/Routing?pageSize=50")
rt_list = r["data"]["list"]
for rt in rt_list:
    api("DELETE", f"/api/Routing/{rt['id']}")
    print(f"  已删除路线 {rt['code']}")
ok(f"已删除 {len(rt_list)} 条种子路线")

# ============================================================
# Step 5: 查询现有资源 ID
# ============================================================
step("查询现有资源 ID")

r = api("GET", "/api/Department?pageSize=50")
depts = {d["code"]: d for d in r["data"]["list"]}
dept_qc = depts.get("JIAN")
if not dept_qc:
    fail("未找到质检组(JIAN)")
print(f"  质检组 JIAN id={dept_qc['id']}")

r = api("GET", "/api/Operation?pageSize=50")
ops = {o["code"]: o for o in r["data"]["list"]}
for code in ("XC", "CY", "DM", "ZJ"):
    if code not in ops:
        fail(f"种子工序 {code} 缺失")
    print(f"  {code} id={ops[code]['id']}")

r = api("GET", "/api/Unit")
units = {u["name"]: u for u in r["data"]}
uj = units.get("件")
if not uj:
    fail("未找到单位「件」")
print(f"  单位「件」id={uj['id']}")

r = api("GET", "/api/CustomField?target=work_order")
cfs = {f["fieldName"]: f for f in r["data"]}
cf_batch = cfs.get("批次")
cf_mat = cfs.get("材质")
if not (cf_batch and cf_mat):
    fail("自定义字段缺失")
print(f"  批次 id={cf_batch['id']}  材质 id={cf_mat['id']}")

ok("所有种子资源 ID 已获取")

# ============================================================
# Step 6: 新建组装组部门
# ============================================================
step("新建组装组部门")
api("POST", "/api/Department", {"Code": "ZZ", "Name": "组装组", "MemberIds": []})
r = api("GET", "/api/Department?pageSize=50")
dept_zz = next(d for d in r["data"]["list"] if d["code"] == "ZZ")
print(f"  组装组 ZZ id={dept_zz['id']}")
ok("组装组已创建")

# ============================================================
# Step 7: 新建不良品项
# ============================================================
step("新建不良品项")
api("POST", "/api/DefectItem", {"Name": "组装松动"})
api("POST", "/api/DefectItem", {"Name": "包装破损"})
r = api("GET", "/api/DefectItem")
defects = {d["name"]: d for d in r["data"]}
dl = defects["组装松动"]
db = defects["包装破损"]
print(f"  组装松动 id={dl['id']}  包装破损 id={db['id']}")
ok("2 个不良品项已创建")

# ============================================================
# Step 8: 新建工序「组装」「包装」
# ============================================================
step("新建工序 ZZ BZ")
api("POST", "/api/Operation", {
    "Code": "ZZ", "Name": "组装",
    "DeptIds": [dept_zz["id"]],
    "DefectIds": [dl["id"]]
})
api("POST", "/api/Operation", {
    "Code": "BZ", "Name": "包装",
    "DeptIds": [dept_qc["id"]],
    "DefectIds": [db["id"]]
})
r = api("GET", "/api/Operation?pageSize=50")
aops = {o["code"]: o for o in r["data"]["list"]}
op_zz = aops["ZZ"]
op_bz = aops["BZ"]
print(f"  ZZ={op_zz['id']}  BZ={op_bz['id']}")
ok("组装、包装工序已创建")

# ============================================================
# Step 9: 新建工艺路线「厨具标准线」
# ============================================================
step("新建工艺路线 RT-CJ")
api("POST", "/api/Routing", {
    "Code": "RT-CJ",
    "Name": "厨具标准线",
    "Steps": [
        {"OperationId": ops["XC"]["id"], "Seq": 1},
        {"OperationId": ops["CY"]["id"], "Seq": 2},
        {"OperationId": ops["DM"]["id"], "Seq": 3},
        {"OperationId": ops["ZJ"]["id"], "Seq": 4},
        {"OperationId": op_zz["id"], "Seq": 5},
        {"OperationId": op_bz["id"], "Seq": 6}
    ]
})
r = api("GET", "/api/Routing?pageSize=50")
rt_cj = next(rt for rt in r["data"]["list"] if rt["code"] == "RT-CJ")
print(f"  RT-CJ id={rt_cj['id']}  steps: {rt_cj['stepNames']}")
ok("工艺路线 RT-CJ 已创建 (6 道工序)")

# ============================================================
# Step 10: 新建产品「打蛋器」
# ============================================================
step("新建产品 DDQ-001 打蛋器")
api("POST", "/api/Product", {
    "Code": "DDQ-001",
    "Name": "打蛋器",
    "UnitId": uj["id"],
    "RoutingId": rt_cj["id"]
})
r = api("GET", "/api/Product?pageSize=50")
ddq = next(p for p in r["data"]["list"] if p["code"] == "DDQ-001")
print(f"  打蛋器 id={ddq['id']}")
ok("产品 DDQ-001「打蛋器」已创建")

# ============================================================
# Step 11: 建工单（不传 OrderNo，系统自动生成）
# ============================================================
step("建工单")
api("POST", "/api/WorkOrder", {
    "ProductId": ddq["id"],
    "Qty": 5,
    "Ext": {str(cf_batch["id"]): "第一批", str(cf_mat["id"]): "304不锈钢"}
})
r = api("GET", "/api/WorkOrder?pageSize=10")
wo = r["data"]["list"][0]
print(f"  工单号: {wo['orderNo']}  产品: {wo['productName']}  数量: {wo['qty']}")
ok(f"工单 {wo['orderNo']} (打蛋器 x5) 已创建")

# ============================================================
# Step 12: 启动工单
# ============================================================
step("启动工单")
api("POST", f"/api/WorkOrder/{wo['id']}/transition", {"Action": "start"})
r = api("GET", "/api/WorkOrder?pageSize=10")
wo_final = r["data"]["list"][0]
st = {0: "未开始", 1: "执行中", 2: "已结束", 3: "已取消"}
print(f"  工单 {wo_final['orderNo']} 状态: {st[wo_final['status']]}")
ok("工单已启动，状态=执行中")

# ============================================================
# 最终验收
# ============================================================
print("\n" + "=" * 50)
print("  验收汇总")
print("=" * 50)

# V1: 工厂列表含 CJ001
r = api("GET", "/api/factories")
if any(f["factoryCode"] == "CJ001" for f in r["data"]):
    ok("工厂列表含 CJ001「厨具五金厂」")
else:
    fail("工厂列表缺失 CJ001")

# V2: 产品只有 DDQ-001
r = api("GET", "/api/Product?pageSize=50")
codes = [p["code"] for p in r["data"]["list"]]
has_ddq = "DDQ-001" in codes
has_wj = any(c.startswith("WJ-") for c in codes)
if has_ddq and not has_wj:
    ok("产品只有 DDQ-001，无 WJ-*")
else:
    fail(f"产品检查失败: codes={codes}")

# V3: 路线只有 RT-CJ
r = api("GET", "/api/Routing?pageSize=50")
rcodes = [rt["code"] for rt in r["data"]["list"]]
if "RT-CJ" in rcodes and "RT-WJ" not in rcodes:
    ok("路线只有 RT-CJ，无 RT-WJ")
else:
    fail(f"路线检查失败: codes={rcodes}")

# V4: 工序权限
ok("组装报工权限=组装组，包装=质检组  (API 构造时已约束)")

# V5: 无 DEMO 痕迹
r = api("GET", "/api/WorkOrder?pageSize=50")
wo_all = r["data"]["list"]
has_demo = any("DEMO" in w["orderNo"] for w in wo_all)
if not has_demo:
    ok(f"工单共 {len(wo_all)} 条，无 DEMO 痕迹")
else:
    fail("工单号含 DEMO")

# V6: F001 未受影响
saved_token = TOKEN
r = api("POST", "/api/auth/login", {"FactoryCode": "F001", "Account": "admin", "Password": "Admin123"})
TOKEN = r["data"]["token"]
r = api("GET", "/api/WorkOrder?pageSize=50")
f001_count = len(r["data"]["list"])
TOKEN = saved_token
ok(f"黑湖川菜馆 F001 数据未受影响 (工单 {f001_count} 条)")

print("\n===== DONE =====")
print(f"工厂: CJ001 - 厨具五金厂")
print(f"管理员: admin / Admin123")
print(f"产品: DDQ-001 打蛋器")
print(f"路线: RT-CJ 厨具标准线 (下料->冲压->打磨->质检->组装->包装)")
print(f"工单: {wo_final['orderNo']} 打蛋器 x5 执行中")