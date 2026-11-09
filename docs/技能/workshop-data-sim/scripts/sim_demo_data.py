# -*- coding: utf-8 -*-
"""车间小工单 · 演示数据一键模拟（workshop-data-sim）

用途：给目标演示账套生成一组能看的"工人 + 报工 + 工资"数据，
     每名工人日工资 ≈ 目标值（默认 200 元）。
原则：幂等可重跑。已存在的用户/价目表/工单/报工/工资单会自动复用，
     重复执行不会叠加翻倍。
依赖：python3 标准库（urllib/ssl/json），无需第三方包。

用法（在技能 scripts 目录下执行）：
    python sim_demo_data.py
    python sim_demo_data.py --period 2026-10
    python sim_demo_data.py --base https://127.0.0.1:8080 --token "Bearer xxx"

顶部 CONFIG 段可按目标账套调整（产品/单价/数量方案/人数/目标日工资）。
"""
import argparse, json, ssl, sys, urllib.request, urllib.error

# ============================ CONFIG（按目标账套调整） ============================
BASE_URL = "https://127.0.0.1:8080"          # 去掉前端 hash 后的 API 根地址（本地演示）
TOKEN    = ""                                 # 管理员 Bearer 令牌；也可用 --token 传入
FACTORY  = "小蜜蜂报工"                        # 仅展示用，脚本不回传
PERIOD   = "2026-10"                           # 工资期间，YYYY-MM

# 产品（演示主体）：用"专属价目表"绑一个产品，绕开产品属性的规格校验
PRODUCT_ID = 10016                            # P-A01 直骨25寸
PRODUCT_NO = "P-A01"
PRODUCT_SUFFIX = "A01"                        # 价目表代号里的产品短名（P-A01 → A01）
WO_NO      = "A0009"                          # 工单号（幂等依据）
WO_QTY     = 10000                            # 工单计划数量（把）

# 8 工序：operationId → (价目表代号, 名称, 单价[元/把])
# 每人日工资 ≈ 单价 × 该人数量（默认目标 200）
PLANS = [
    (28, "HP", "合片", 0.45),
    (29, "FS", "缝伞", 0.40),
    (30, "LB", "拉边", 0.30),
    (31, "XD", "小裁", 0.20),
    (32, "DD", "顶带", 0.25),
    (33, "ZW", "打珠尾", 0.30),
    (34, "PJ", "品检", 0.22),
    (35, "BZ", "包装", 0.18),
]
# 每名工人：账号 → (姓名, 部门id, 所属工序, 该工序代号, 良品数量)
# 部门id与工序报工权限需匹配：23合片/24缝伞/25拉边/26小裁/27顶带/28珠尾/29品检/30包装
WORKERS = [
    ("sim01", "模拟·张一", 23, 28, "HP", 445),
    ("sim02", "模拟·李二", 23, 28, "HP", 445),
    ("sim03", "模拟·王三", 24, 29, "FS", 500),
    ("sim04", "模拟·赵四", 24, 29, "FS", 500),
    ("sim05", "模拟·孙五", 25, 30, "LB", 667),
    ("sim06", "模拟·周六", 26, 31, "XD", 1000),
    ("sim07", "模拟·吴七", 27, 32, "DD", 800),
    ("sim08", "模拟·郑八", 28, 33, "ZW", 667),
    ("sim09", "模拟·钱九", 29, 34, "PJ", 910),
    ("sim10", "模拟·陈十", 30, 35, "BZ", 1112),
]
WORKER_PASSWORD = "123456"
EFFECTIVE_FROM = "2026-10-25"                 # 价目表生效日
# ================================================================================

CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE

def make_token(args):
    return args.token or TOKEN

def req(token, method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    r = urllib.request.Request(BASE_URL + path, data=data, method=method,
        headers={"Authorization": token, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(r, context=CTX, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"http_error": e.code, "body": e.read().decode("utf-8", "ignore")[:400]}

def ok(resp, what):
    if isinstance(resp, dict) and resp.get("code") == 0:
        return True
    print(f"[FAIL] {what}: {json.dumps(resp, ensure_ascii=False)[:400]}")
    return False

def list_all(token, path, key="keyword"):
    out, page = [], 1
    while True:
        r = req(token, "GET", f"{path}&page={page}&pageSize=50" if "?" in path else f"{path}?page={page}&pageSize=50")
        data = (r.get("data") or {}).get("list") or []
        out.extend(data)
        if len(data) < 50:
            break
        page += 1
    return out

def get_by_no(token, path, no_field, no):
    for it in list_all(token, path):
        if str(it.get(no_field)) == str(no):
            return it
    return None

def run(args):
    token = make_token(args)
    if not token:
        sys.exit("缺少管理员 Bearer 令牌：请用 --token 传入，或填到 CONFIG.TOKEN")

    # 1) 建用户（幂等：已存在则复用，POST 返回无 id，需回查）
    users = {}
    for acc, name, dept, op, code, qty in WORKERS:
        existing = get_by_no(token, "/api/User?", "account", acc)
        if existing:
            users[acc] = existing["id"]
            continue
        r = req(token, "POST", "/api/User", {"account": acc, "name": name, "role": 2, "userType": 0,
                                            "wageCoeff": 1.0, "password": WORKER_PASSWORD, "deptIds": [dept]})
        if ok(r, f"建用户 {acc}"):
            ex = get_by_no(token, "/api/User?", "account", acc)
            users[acc] = ex["id"] if ex else None
    print(f"[1] 用户就绪: {len(users)} 个")

    # 2) 建专属价目表（幂等；创建传完整 details 会自动启用 status=1，勿再调 status 接口）
    for opid, code, name, price in PLANS:
        list_key = f"SIM-{code}-{PRODUCT_SUFFIX}"
        existing = get_by_no(token, f"/api/operation-price-list?operationId={opid}&keyword={list_key}&", "code", list_key)
        if existing and existing.get("status") == 1:
            print(f"   复用价目表 {list_key} id={existing['id']}")
            continue
        body = {"productId": PRODUCT_ID, "remark": "模拟：演示专属价目表", "code": list_key,
                "name": f"模拟价目表-{name}({PRODUCT_NO}专属)",
                "operationId": opid, "pricingType": 1, "status": 0, "effectiveFrom": EFFECTIVE_FROM,
                "effectiveTo": None, "priceUnit": "把", "reportUnitId": 16, "unitsPerPriceUnit": 1,
                "expectedVersion": "",
                "details": {"schemaVersion": 2, "requiredFieldIds": [], "groups": [],
                            "rows": [{"rowId": "R1", "kind": "base", "label": f"{PRODUCT_NO}{name}（演示价）",
                                      "conditions": [], "amount": price, "deductPrice": 0, "groupKey": None,
                                      "includedRowIds": [], "confirmed": True, "source": "演示模拟", "note": ""}]}}
        r = req(token, "POST", "/api/operation-price-list", body)
        if ok(r, f"建专属价目表 {list_key}"):
            print(f"   价目表 {list_key} 已启用 id={r.get('data')}")
    print("[2] 专属价目表就绪")

    # 3) 建工单（幂等：自动锁价到专属表；POST 返回无 id，需回查）
    wo = get_by_no(token, "/api/WorkOrder?", "orderNo", WO_NO)
    if not wo:
        r = req(token, "POST", "/api/WorkOrder", {"productId": PRODUCT_ID, "qty": WO_QTY, "orderNo": WO_NO,
                                                  "dueDate": None, "ext": {}})
        if not ok(r, "建工单"):
            sys.exit(1)
        wo = get_by_no(token, "/api/WorkOrder?", "orderNo", WO_NO)
    wo_id = wo["id"]
    print(f"[3] 工单 {WO_NO} id={wo_id} 就绪")

    # 4) 代报（幂等：先查该工序已报记录避免重复；跨工序超上道通过量用 confirmUpstream 放行）
    by_op = {}
    for acc, name, dept, op, code, qty in WORKERS:
        by_op.setdefault(op, []).append((acc, qty))
    for opid, items in by_op.items():
        reported = req(token, "GET", f"/api/Report?orderId={wo_id}&operationId={opid}&page=1&pageSize=5")
        if (reported.get("data") or {}).get("total", 0) > 0:
            print(f"   工序 {opid} 已报，跳过")
            continue
        body = {"orderId": wo_id, "operationId": opid, "goodQty": 0, "defectQty": 0, "source": 1,
                "confirmUpstream": True,
                "assignees": [{"userId": users[a], "goodQty": q, "defectQty": 0, "durationMinutes": 480}
                              for a, q in items]}
        r = req(token, "POST", "/api/Report", body)
        if ok(r, f"代报工序 {opid}"):
            print(f"   工序 {opid} 报工 OK")
    print("[4] 报工完成")

    # 5) 生成工资（幂等：期间已有工资单则跳过）
    sal = req(token, "GET", f"/api/Salary?periodType=1&periodValue={PERIOD}&page=1&pageSize=5")
    if (sal.get("data") or {}).get("total", 0) > 0:
        print(f"[5] 工资期间 {PERIOD} 已生成，跳过")
    else:
        r = req(token, "POST", "/api/Salary/generate", {"periodType": 1, "periodValue": PERIOD})
        ok(r, "生成工资") or print("  ", json.dumps(r, ensure_ascii=False)[:400])
        print(f"[5] 工资 {PERIOD} 已生成")

    # 6) 回查汇总
    print("\n=== 工资单 ===")
    sl = req(token, "GET", f"/api/Salary?periodType=1&periodValue={PERIOD}&page=1&pageSize=30")
    for it in (sl.get("data") or {}).get("list", []):
        print(f"  {it.get('userName')}: 应发 {it.get('totalAmount')} 状态{it.get('status')}")
    print("=== 报工 ===")
    rp = req(token, "GET", f"/api/Report?orderId={wo_id}&page=1&pageSize=50")
    for it in (rp.get("data") or {}).get("list", []):
        print(f"  {it.get('userName')}: 工序{it.get('operationName')} 良品{it.get('goodQty')} 单价{it.get('unitPrice')} 工资{it.get('wageAmount')}")
    print("\n完成。前端查看：PC 工资报表 #/salary；H5 工人端用 sim01~sim10/{PASSWORD} 登录看'我的工资'。")

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE_URL)
    ap.add_argument("--token", default="")
    ap.add_argument("--period", default=PERIOD)
    args = ap.parse_args()
    BASE_URL = args.base
    PERIOD = args.period
    run(args)
