# -*- coding: utf-8 -*-
"""昌盛雅畅 CSYC001 演示账套 · 模拟报工/工资（workshop-data-sim 变体）
幂等可重跑。令牌用 --token 传入，不落脚本。

模式：
  --mode day      单日快照（默认 PERIOD=2026-10，沿用 414）
  --mode sep2026  2026-09 全月（415：人均 7000~8000，管理员代报带起止时间）
"""
import argparse, hashlib, json, math, os, ssl, subprocess, sys, urllib.request, urllib.error
from datetime import date, datetime, timedelta

# ============================ CONFIG ============================
BASE_URL = "https://localhost:8080"
TOKEN    = ""
FACTORY  = "昌盛雅畅"
PERIOD   = "2026-10"
FACTORY_ID = 7

PRODUCT_ID = 10035
PRODUCT_NO = "CSYC-GM001"
PRODUCT_SUFFIX = "GM001"
WO_NO      = "CSYC-GM20261001"
WO_QTY     = 12000

# 415：9 月专用工单（计划量盖住 22 天×各工序峰值）
WO_NO_SEP  = "CSYC-GM20260901"
WO_QTY_SEP = 800000
PERIOD_SEP = "2026-09"
EFFECTIVE_FROM_SEP = "2026-09-01"
# 日计件收入目标（元），满勤 22 天×≈340 → 月薪≈7500
DAY_INCOME_LO, DAY_INCOME_HI = 320.0, 360.0

PLANS = [
    (46, "SMT", "SMT贴片", 0.05),
    (47, "DIP", "DIP插件", 0.05),
    (48, "BHH", "波峰焊", 0.04),
    (49, "CS",  "测试",   0.15),
    (50, "ZZ",  "组装",   0.35),
    (51, "BZ",  "包装",   0.15),
]
# 工人：(账号, 姓名, 部门id, 工序id, 代号, 单日演示良品数[day模式])
WORKERS = [
    ("csyc01", "张伟", 39, 46, "SMT", 3000),
    ("csyc02", "李娜", 39, 46, "SMT", 2900),
    ("csyc03", "王强", 39, 46, "SMT", 2800),
    ("csyc04", "赵敏", 39, 46, "SMT", 2700),
    ("csyc05", "陈杰", 40, 47, "DIP", 3200),
    ("csyc06", "刘洋", 40, 47, "DIP", 3100),
    ("csyc07", "杨梅", 40, 47, "DIP", 3000),
    ("csyc08", "周军", 40, 48, "BHH", 4300),
    ("csyc09", "吴磊", 40, 48, "BHH", 4200),
    ("csyc10", "郑爽", 41, 49, "CS",  1100),
    ("csyc11", "孙浩", 41, 49, "CS",  1050),
    ("csyc12", "钱芳", 41, 49, "CS",  1000),
    ("csyc13", "冯凯", 41, 49, "CS",   950),
    ("csyc14", "蒋丽", 42, 50, "ZZ",   560),
    ("csyc15", "沈斌", 42, 50, "ZZ",   540),
    ("csyc16", "韩雪", 42, 50, "ZZ",   520),
    ("csyc17", "杨帆", 42, 50, "ZZ",   500),
    ("csyc18", "朱明", 43, 51, "BZ",  1200),
    ("csyc19", "秦芳", 43, 51, "BZ",  1100),
]
DEFECTS = {
    "csyc01": (41, 20),
    "csyc02": (42, 15),
    "csyc08": (44, 30),
    "csyc09": (45, 20),
    "csyc10": (46, 25),
}
# 稳定缺勤：账号 → 从月末往前扣的工作日数（1 或 2）
ABSENCE = {
    "csyc03": 1, "csyc07": 2, "csyc11": 1, "csyc16": 1, "csyc19": 2,
}
WORKER_PASSWORD = "123456"
EFFECTIVE_FROM = "2026-10-29"
REPORT_UNIT_ID = 20
SQLCMD_DB = "WorkshopMes"
SQLCMD_SERVER = "localhost"
# ================================================================

CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE

def req(token, method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    r = urllib.request.Request(BASE_URL + path, data=data, method=method,
        headers={"Authorization": token, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(r, context=CTX, timeout=60) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"http_error": e.code, "body": e.read().decode("utf-8", "ignore")[:500]}
    except Exception as e:
        return {"exc": str(e)}

def ok(resp, what):
    if isinstance(resp, dict) and resp.get("code") == 0:
        return True
    print(f"[FAIL] {what}: {json.dumps(resp, ensure_ascii=False)[:500]}")
    return False

def list_all(token, path, page_size=50):
    out, page = [], 1
    while True:
        sep = "&" if "?" in path else "?"
        r = req(token, "GET", f"{path}{sep}page={page}&pageSize={page_size}")
        data = (r.get("data") or {}).get("list") or []
        out.extend(data)
        if len(data) < page_size:
            break
        page += 1
    return out

def get_by_no(token, path, no_field, no):
    for it in list_all(token, path):
        if str(it.get(no_field)) == str(no):
            return it
    return None

def stable_unit(seed, lo, hi):
    h = hashlib.md5(seed.encode("utf-8")).hexdigest()
    x = int(h[:8], 16) / 0xFFFFFFFF
    return lo + (hi - lo) * x

def workdays_sep_2026():
    """2026-09 周一~周五（指令假设无法定假）→ 22 天。"""
    d0, days = date(2026, 9, 1), []
    while d0.month == 9:
        if d0.weekday() < 5:
            days.append(d0)
        d0 += timedelta(days=1)
    return days

def backdate_price_effective_sql():
    """B08：API 时间线基线起自「今天/UpdatedAt」，无法把历史生效段回填到 9 月。
    价目表属基础配置，允许 SQL（严格 factory_id=7），只前移生效日、不改单价。"""
    sql = f"""
SET NOCOUNT ON;
UPDATE base_operation_price_list
SET effective_from = '{EFFECTIVE_FROM_SEP}', updated_at = SYSUTCDATETIME()
WHERE factory_id = {FACTORY_ID} AND effective_from > '{EFFECTIVE_FROM_SEP}';
UPDATE base_operation_price_revision
SET effective_from = '{EFFECTIVE_FROM_SEP}'
WHERE factory_id = {FACTORY_ID} AND superseded = 0 AND effective_from > '{EFFECTIVE_FROM_SEP}';
SELECT 'list' AS kind, COUNT(*) AS cnt FROM base_operation_price_list
 WHERE factory_id={FACTORY_ID} AND effective_from='{EFFECTIVE_FROM_SEP}'
UNION ALL
SELECT 'rev', COUNT(*) FROM base_operation_price_revision
 WHERE factory_id={FACTORY_ID} AND superseded=0 AND effective_from='{EFFECTIVE_FROM_SEP}';"""
    r = subprocess.run(
        ["sqlcmd", "-S", SQLCMD_SERVER, "-d", SQLCMD_DB, "-E", "-Q", sql],
        capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        print("[FAIL] B08 sqlcmd:", r.stderr or r.stdout)
        sys.exit(1)
    print("[B08] 价目表生效日已前移到", EFFECTIVE_FROM_SEP)
    print(r.stdout.strip())

def ensure_users(token):
    users = {}
    for acc, name, dept, op, code, qty in WORKERS:
        existing = get_by_no(token, "/api/User?", "account", acc)
        if existing:
            users[acc] = existing["id"]; continue
        r = req(token, "POST", "/api/User", {"account": acc, "name": name, "role": 2, "userType": 0,
                                            "wageCoeff": 1.0, "password": WORKER_PASSWORD, "deptIds": [dept]})
        if ok(r, f"建用户 {acc}"):
            ex = get_by_no(token, "/api/User?", "account", acc)
            users[acc] = ex["id"] if ex else None
    print(f"[1] 用户就绪: {len(users)} 个")
    return users

def ensure_price_lists(token):
    for opid, code, name, price in PLANS:
        list_key = f"SIM-{code}-{PRODUCT_SUFFIX}"
        existing = get_by_no(token, f"/api/operation-price-list?operationId={opid}&keyword={list_key}&", "code", list_key)
        if existing and existing.get("status") == 1:
            print(f"   复用价目表 {list_key} id={existing['id']}")
            continue
        body = {"productId": PRODUCT_ID, "remark": "模拟：演示专属价目表", "code": list_key,
                "name": f"模拟价目表-{name}({PRODUCT_NO}专属)",
                "operationId": opid, "pricingType": 1, "status": 0, "effectiveFrom": EFFECTIVE_FROM,
                "effectiveTo": None, "priceUnit": "台", "reportUnitId": REPORT_UNIT_ID, "unitsPerPriceUnit": 1,
                "expectedVersion": "",
                "details": {"schemaVersion": 2, "requiredFieldIds": [], "groups": [],
                            "rows": [{"rowId": "R1", "kind": "base", "label": f"{PRODUCT_NO}{name}（演示价）",
                                      "conditions": [], "amount": price, "deductPrice": 0, "groupKey": None,
                                      "includedRowIds": [], "confirmed": True, "source": "演示模拟", "note": ""}]}}
        r = req(token, "POST", "/api/operation-price-list", body)
        if ok(r, f"建专属价目表 {list_key}"):
            print(f"   价目表 {list_key} 已启用 id={r.get('data')}")
    print("[2] 专属价目表就绪")

def ensure_wo(token, order_no, qty):
    wo = get_by_no(token, "/api/WorkOrder?", "orderNo", order_no)
    if not wo:
        r = req(token, "POST", "/api/WorkOrder", {"productId": PRODUCT_ID, "qty": qty, "orderNo": order_no,
                                                  "dueDate": None, "ext": {}})
        if not ok(r, f"建工单 {order_no}"):
            sys.exit(1)
        wo = get_by_no(token, "/api/WorkOrder?", "orderNo", order_no)
    print(f"[3] 工单 {order_no} id={wo['id']} qty={wo.get('qty')} 就绪")
    return wo

def generate_salary(token, period):
    sal = req(token, "GET", f"/api/Salary?periodType=1&periodValue={period}&page=1&pageSize=5")
    if (sal.get("data") or {}).get("total", 0) > 0:
        print(f"[5] 工资期间 {period} 已生成，跳过")
        return
    r = req(token, "POST", "/api/Salary/generate", {"periodType": 1, "periodValue": period})
    if not ok(r, f"生成工资 {period}"):
        print("  ", json.dumps(r, ensure_ascii=False)[:400])
        sys.exit(1)
    print(f"[5] 工资 {period} 已生成")

def print_salary(token, period):
    print(f"\n=== 工资单 {period} ===")
    sl = req(token, "GET", f"/api/Salary?periodType=1&periodValue={period}&page=1&pageSize=30")
    amounts = []
    for it in (sl.get("data") or {}).get("list", []):
        amt = float(it.get("totalAmount") or 0)
        amounts.append(amt)
        print(f"  {it.get('userName')}: 应发 {amt} 状态{it.get('status')}")
    if amounts:
        mean = sum(amounts) / len(amounts)
        print(f"  人数={len(amounts)} 合计={sum(amounts):.2f} 均值={mean:.2f}")
        if len(amounts) >= 19 and not (7000 <= mean <= 8000):
            print(f"[WARN] 均值 {mean:.2f} 不在 [7000,8000]")
    return amounts

def run_day(token):
    users = ensure_users(token)
    ensure_price_lists(token)
    wo = ensure_wo(token, WO_NO, WO_QTY)
    wo_id = wo["id"]

    by_op = {}
    for acc, name, dept, op, code, qty in WORKERS:
        by_op.setdefault(op, []).append((acc, qty))
    reported_ops = set()
    all_reports = list_all(token, f"/api/Report?orderNo={WO_NO}")
    for it in all_reports:
        if it.get("operationId"):
            reported_ops.add(it.get("operationId"))
    for opid, items in by_op.items():
        if opid in reported_ops:
            print(f"   工序 {opid} 已报，跳过")
            continue
        assigns = []
        for acc, q in items:
            d = {"userId": users[acc], "goodQty": q, "defectQty": 0, "durationMinutes": 480}
            if acc in DEFECTS:
                did, dq = DEFECTS[acc]
                d["defectQty"] = dq
                d["defectId"] = did
            assigns.append(d)
        body = {"orderId": wo_id, "operationId": opid, "goodQty": 0, "defectQty": 0, "source": 1,
                "confirmUpstream": True, "assignees": assigns}
        r = req(token, "POST", "/api/Report", body)
        if ok(r, f"代报工序 {opid}"):
            print(f"   工序 {opid} 报工 OK")
    print("[4] 报工完成")
    generate_salary(token, PERIOD)
    print_salary(token, PERIOD)
    print("=== 报工抽样 ===")
    for it in list_all(token, f"/api/Report?orderNo={WO_NO}")[:20]:
        print(f"  {it.get('userName')}: 工序{it.get('operationName')} 良品{it.get('goodQty')} "
              f"不良{it.get('defectQty')} 单价{it.get('unitPrice')} 时间{it.get('reportTime')}")
    print("\n完成。前端：PC #/salary；H5 用 csyc01~csyc19 / 123456。")

def day_qty_and_defect(acc, opid, price, day):
    """按日反推良品；不良按 B04 集中在少数工人/部分天。"""
    base = stable_unit(f"{acc}-income", DAY_INCOME_LO, DAY_INCOME_HI)
    wave = stable_unit(f"{acc}-{day.isoformat()}-wave", 0.88, 1.12)
    income = base * wave
    good = max(1, int(round(income / price)))
    defect_qty, defect_id = 0, None
    if acc in DEFECTS and int(stable_unit(f"{acc}-{day.isoformat()}-def", 0, 3)) < 1.2:
        defect_id, _ = DEFECTS[acc]
        rate = stable_unit(f"{acc}-{day.isoformat()}-dr", 0.01, 0.02)
        defect_qty = max(1, int(round(good * rate)))
    return good, defect_qty, defect_id

def attendance_days(acc, all_days):
    miss = ABSENCE.get(acc, 0)
    if miss <= 0:
        return list(all_days)
    return list(all_days[:-miss]) if miss < len(all_days) else list(all_days)

def run_sep2026(token):
    backdate_price_effective_sql()
    users = ensure_users(token)
    ensure_price_lists(token)  # 幂等：已有则复用
    backdate_price_effective_sql()  # 新建表若带回新生效日再刷一次
    wo = ensure_wo(token, WO_NO_SEP, WO_QTY_SEP)
    wo_id = wo["id"]
    days = workdays_sep_2026()
    assert len(days) == 22, f"期望 22 个工作日，实际 {len(days)}"
    price_by_op = {opid: price for opid, _, _, price in PLANS}
    name_by_acc = {acc: name for acc, name, *_ in WORKERS}

    # 已有报工：按 姓名+工序+业务日 判重（列表无 userId）
    existing = set()
    for it in list_all(token, f"/api/Report?orderNo={WO_NO_SEP}", page_size=100):
        rt = it.get("reportTime") or ""
        day_s = str(rt)[:10]
        existing.add((it.get("userName"), it.get("operationId"), day_s))
    print(f"[4] 已有 9 月报工键 {len(existing)} 条；工作日 {len(days)}")

    # 每人出勤日
    attend = {acc: set(attendance_days(acc, days)) for acc, *_ in WORKERS}

    posted, skipped, failed = 0, 0, 0
    for d in days:
        day_s = d.isoformat()
        start = f"{day_s}T08:00:00"
        end = f"{day_s}T16:00:00"
        by_op = {}
        for acc, name, dept, op, code, _qty in WORKERS:
            if d not in attend[acc]:
                continue
            if (name, op, day_s) in existing:
                skipped += 1
                continue
            by_op.setdefault(op, []).append(acc)
        for opid, accs in by_op.items():
            price = price_by_op[opid]
            assigns = []
            for acc in accs:
                good, dq, did = day_qty_and_defect(acc, opid, price, d)
                row = {"userId": users[acc], "goodQty": good, "defectQty": dq or 0,
                       "startTime": start, "endTime": end}
                if did is not None and dq:
                    row["defectId"] = did
                assigns.append(row)
            if not assigns:
                continue
            body = {"orderId": wo_id, "operationId": opid, "goodQty": 0, "defectQty": 0, "source": 1,
                    "confirmUpstream": True, "assignees": assigns}
            r = req(token, "POST", "/api/Report", body)
            if ok(r, f"代报 {day_s} 工序{opid}×{len(assigns)}"):
                posted += len(assigns)
                for acc in accs:
                    existing.add((name_by_acc[acc], opid, day_s))
            else:
                failed += 1
                # 不吞错：继续其它日/工序，最后非 0 退出
    print(f"[4] 报工完成 posted={posted} skipped={skipped} failed_batches={failed}")
    if failed:
        sys.exit(1)

    generate_salary(token, PERIOD_SEP)
    amounts = print_salary(token, PERIOD_SEP)

    # 出勤天数抽查
    days_by_name = {}
    for it in list_all(token, f"/api/Report?orderNo={WO_NO_SEP}", page_size=100):
        days_by_name.setdefault(it.get("userName"), set()).add(str(it.get("reportTime") or "")[:10])
    print("\n=== 出勤天数 ===")
    for name, ds in sorted(days_by_name.items(), key=lambda x: x[0]):
        print(f"  {name}: {len(ds)} 天")

    print("\n=== reportTime 抽样（应落在 2026-09）===")
    sample = list_all(token, f"/api/Report?orderNo={WO_NO_SEP}", page_size=100)[:5]
    for it in sample:
        print(f"  {it.get('userName')} {it.get('reportTime')} 良品{it.get('goodQty')} 工价{it.get('wageAmount')}")

    if amounts and len(amounts) >= 19:
        mean = sum(amounts) / len(amounts)
        if not (7000 <= mean <= 8000):
            sys.exit(f"验收失败：均值 {mean:.2f} 不在 [7000,8000]")
    print("\n完成。PC #/salary 选 2026-09；10 月一天快照保留不动。")

def run(args):
    token = args.token or TOKEN
    if not token:
        sys.exit("缺少管理员 Bearer 令牌：用 --token 传入。")
    if not token.startswith("Bearer "):
        token = "Bearer " + token
    if args.mode == "sep2026":
        run_sep2026(token)
    else:
        global PERIOD
        PERIOD = args.period or PERIOD
        run_day(token)

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE_URL)
    ap.add_argument("--token", default="")
    ap.add_argument("--period", default=PERIOD, help="仅 day 模式")
    ap.add_argument("--mode", choices=["day", "sep2026"], default="sep2026")
    args = ap.parse_args()
    BASE_URL = args.base.rstrip("/")
    run(args)
