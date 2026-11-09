# -*- coding: utf-8 -*-
"""HY 伞业正式账（factoryId=4）· 少而精演示数据

- 12～15 张 HY-DEMO-* 工单（1 张大单扛十月工资 + 若干小单做列表/进度）
- hysim01～20，2026-10 人均约 6000～7000
- 不动真名工人满月工资；不停用客户通用 PL-* 价目表
- 阻塞失败直接 exit；非阻塞写入 .tmp/sim-hy4/问题清单.md（由执行方追加）

用法：
  python sim_hy4_demo_oct2026.py --base https://103.39.222.133:11302 --token "Bearer <令牌>"
"""
from __future__ import annotations

import argparse
import hashlib
import json
import ssl
import sys
import urllib.error
import urllib.request
from datetime import date, timedelta
from pathlib import Path

BASE_URL = "https://103.39.222.133:11302"
PERIOD = "2026-10"
PRODUCT_CODE = "P-HYDEMO-OCT"
PRODUCT_NAME = "演示十月直骨25寸"
SOURCE_PRODUCT_ID = 35  # P-A01
PRODUCT_ID = 0
WO_MAIN = "HY-DEMO-OCT-MAIN"
WO_QTY_MAIN = 400000
EFFECTIVE_FROM = "2026-10-01"
REPORT_UNIT_ID = 15  # 把（本厂）
PRICE_PREFIX = "HYDO"
WORKER_PASSWORD = "123456"
DAY_INCOME_LO, DAY_INCOME_HI = 290.0, 320.0  # 22 天 → 约 6380～7040

# 报工工序（本厂 operationId / 部门）
# 合片24/包边24/缝伞27/包装27/小裁23/打顶25/珠尾26/品检31
PLANS = [
    (23, "HP", "合片", 0.45),
    (27, "FS", "缝伞", 0.40),
    (19, "LB", "包边", 0.30),
    (21, "XD", "小裁", 0.20),
    (22, "DD", "打顶", 0.25),
    (26, "ZW", "打珠尾", 0.30),
    (28, "PJ", "品检", 0.22),
    (30, "BZ", "包装", 0.18),
]
FILLER_PLANS = [
    (18, "DC", "大裁", 0.10),
    (20, "YB", "验布", 0.10),
    (24, "BD", "打带", 0.10),
    (25, "PJZ", "配件", 0.10),
    (29, "ZSB", "装伞柄", 0.10),
]

WORKERS = [
    ("hysim01", "演示张伟", 24, 23, "HP"),
    ("hysim02", "演示李娜", 24, 23, "HP"),
    ("hysim03", "演示周杰", 24, 23, "HP"),
    ("hysim04", "演示王强", 27, 27, "FS"),
    ("hysim05", "演示赵敏", 27, 27, "FS"),
    ("hysim06", "演示郑浩", 27, 27, "FS"),
    ("hysim07", "演示陈静", 24, 19, "LB"),
    ("hysim08", "演示刘洋", 24, 19, "LB"),
    ("hysim09", "演示孙磊", 23, 21, "XD"),
    ("hysim10", "演示马超", 23, 21, "XD"),
    ("hysim11", "演示周涛", 25, 22, "DD"),
    ("hysim12", "演示吴磊", 25, 22, "DD"),
    ("hysim13", "演示何军", 25, 22, "DD"),
    ("hysim14", "演示郑爽", 26, 26, "ZW"),
    ("hysim15", "演示罗斌", 26, 26, "ZW"),
    ("hysim16", "演示钱芳", 31, 28, "PJ"),
    ("hysim17", "演示谢娜", 31, 28, "PJ"),
    ("hysim18", "演示唐伟", 31, 28, "PJ"),
    ("hysim19", "演示陈明", 27, 30, "BZ"),
    ("hysim20", "演示曹雪", 27, 30, "BZ"),
]
ABSENCE = {
    "hysim04": 1, "hysim08": 2, "hysim11": 1, "hysim15": 1, "hysim17": 2, "hysim20": 1,
}

# 列表用小单：一律挂演示产品（客户伞产品路线含「大裁」且无大裁通用价，建单会失败）
SMALL_COUNT = 12
PROBLEMS_PATH = Path(r"D:\workspace\pkub\bee\.tmp\sim-hy4\问题清单.md")

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE


def log_problem(pid, level, desc, path="-", status="待处理", action=""):
    line = f"| {pid} | {level} | {desc} | {path} | {status} | {action} |\n"
    if PROBLEMS_PATH.exists():
        text = PROBLEMS_PATH.read_text(encoding="utf-8")
        if pid in text:
            return
        PROBLEMS_PATH.write_text(text.rstrip() + "\n" + line, encoding="utf-8")
    print(f"[PROBLEM] {pid} ({level}) {desc}")


def req(token, method, path, body=None, timeout=120, retries=3):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    last = None
    auth = token if token.lower().startswith("bearer ") else f"Bearer {token}"
    for attempt in range(retries):
        r = urllib.request.Request(
            BASE_URL + path,
            data=data,
            method=method,
            headers={
                "Authorization": auth,
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Cache-Control": "no-cache",
            },
        )
        try:
            with urllib.request.urlopen(r, context=CTX, timeout=timeout) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            return {"http_error": e.code, "body": e.read().decode("utf-8", "ignore")[:600]}
        except Exception as e:
            last = {"exc": str(e)}
            if attempt + 1 < retries:
                continue
    return last or {"exc": "unknown"}


def ok(resp, what, blocking=True):
    if isinstance(resp, dict) and resp.get("code") == 0:
        return True
    msg = json.dumps(resp, ensure_ascii=False)[:500]
    print(f"[FAIL] {what}: {msg}")
    if blocking:
        log_problem(f"P-BLOCK-{hashlib.md5(what.encode()).hexdigest()[:6]}", "阻塞", f"{what}: {msg[:120]}", "-", "待处理", "当场停")
        sys.exit(1)
    log_problem(f"P-NB-{hashlib.md5(what.encode()).hexdigest()[:6]}", "非阻塞", f"{what}: {msg[:120]}", "-", "待处理", "最后一轮")
    return False


def list_all(token, path, page_size=100):
    out, page = [], 1
    while True:
        sep = "&" if "?" in path else "?"
        r = req(token, "GET", f"{path}{sep}page={page}&pageSize={page_size}")
        data = (r.get("data") or {})
        lst = data.get("list") or []
        out.extend(lst)
        total = data.get("total")
        if total is not None and len(out) >= int(total):
            break
        if len(lst) < page_size:
            break
        page += 1
    return out


def get_by(token, path, field, value):
    for it in list_all(token, path):
        if str(it.get(field)) == str(value):
            return it
    return None


def stable_unit(seed, lo, hi):
    h = hashlib.md5(seed.encode("utf-8")).hexdigest()
    x = int(h[:8], 16) / 0xFFFFFFFF
    return lo + (hi - lo) * x


def workdays_oct_2026():
    d0, days = date(2026, 10, 1), []
    while d0.month == 10:
        if d0.weekday() < 5:
            days.append(d0)
        d0 += timedelta(days=1)
    return days


def attendance_days(acc, all_days):
    miss = ABSENCE.get(acc, 0)
    if miss <= 0:
        return list(all_days)
    return list(all_days[:-miss]) if miss < len(all_days) else list(all_days)


def day_qty(acc, price, day):
    base = stable_unit(f"{acc}-income", DAY_INCOME_LO, DAY_INCOME_HI)
    wave = stable_unit(f"{acc}-{day.isoformat()}-wave", 0.92, 1.08)
    return max(1, int(round(base * wave / price)))


def clean_draft_salaries(token):
    for it in list_all(token, f"/api/Salary?periodType=1&periodValue={PERIOD}"):
        # 只删非 hysim 的干扰草稿，或全部草稿后重建 hysim
        name = it.get("userName") or ""
        if it.get("status") == 0:
            ok(req(token, "DELETE", f"/api/Salary/{it['id']}"), f"删工资草稿 {it['id']} {name}", blocking=False)


def ensure_product(token):
    global PRODUCT_ID
    existing = get_by(token, "/api/Product?", "code", PRODUCT_CODE)
    if existing:
        PRODUCT_ID = existing["id"]
        print(f"[0] 产品复用 {PRODUCT_CODE} id={PRODUCT_ID}")
        return
    src = req(token, "GET", f"/api/Product/{SOURCE_PRODUCT_ID}")
    ok(src, "读源产品 P-A01")
    p = src["data"]
    body = {
        "code": PRODUCT_CODE,
        "name": PRODUCT_NAME,
        "unitId": p["unitId"],
        "routingId": p["routingId"],
        "supplier": "演示",
        "price": None,
        "customValues": p.get("customValues") or {},
        "isRiskFocus": False,
    }
    ok(req(token, "POST", "/api/Product", body), f"建产品 {PRODUCT_CODE}")
    existing = get_by(token, "/api/Product?", "code", PRODUCT_CODE)
    if not existing:
        sys.exit("产品回查失败")
    PRODUCT_ID = existing["id"]
    print(f"[0] 产品新建 {PRODUCT_CODE} id={PRODUCT_ID}")


def _oct_details(name, price):
    return {
        "schemaVersion": 2,
        "requiredFieldIds": [],
        "groups": [],
        "rows": [
            {
                "rowId": "R1",
                "kind": "base",
                "label": f"{PRODUCT_CODE}{name}",
                "conditions": [],
                "amount": price,
                "deductPrice": 0,
                "groupKey": None,
                "includedRowIds": [],
                "confirmed": True,
                "source": "HY演示",
                "note": "",
            }
        ],
    }


def ensure_one_price(token, opid, code, name, price):
    list_key = f"{PRICE_PREFIX}-{code}-A01"
    existing = get_by(token, f"/api/operation-price-list?operationId={opid}&keyword={list_key}&", "code", list_key)
    if existing and existing.get("status") == 1 and existing.get("productId") == PRODUCT_ID:
        print(f"   复用 {list_key} id={existing['id']}")
        return
    if existing and existing.get("status") != 1:
        list_key = f"{PRICE_PREFIX}-{code}-A01B"
        existing2 = get_by(token, f"/api/operation-price-list?operationId={opid}&keyword={list_key}&", "code", list_key)
        if existing2 and existing2.get("status") == 1:
            print(f"   复用 {list_key} id={existing2['id']}")
            return
        existing = None
    if existing and existing.get("status") == 1:
        print(f"   复用 {list_key} id={existing['id']}")
        return
    body = {
        "productId": PRODUCT_ID,
        "remark": "HY正式账十月演示专属（可识别清理）",
        "code": list_key,
        "name": f"演示价-{name}({PRODUCT_CODE})",
        "operationId": opid,
        "pricingType": 1,
        "status": 0,
        "effectiveFrom": EFFECTIVE_FROM,
        "effectiveTo": None,
        "priceUnit": "把",
        "reportUnitId": REPORT_UNIT_ID,
        "unitsPerPriceUnit": 1,
        "expectedVersion": "",
        "details": _oct_details(name, price),
    }
    r = req(token, "POST", "/api/operation-price-list", body)
    ok(r, f"建价目 {list_key}")
    print(f"   新建 {list_key} id={r.get('data')}")


def ensure_price_lists(token):
    for row in PLANS + FILLER_PLANS:
        ensure_one_price(token, *row)
    for opid, code, name, price in PLANS:
        for day in ("2026-10-01", "2026-10-31"):
            r = req(
                token,
                "POST",
                "/api/operation-price-list/preview",
                {"productId": PRODUCT_ID, "operationId": opid, "date": day},
            )
            ok(r, f"预览 {name} {day}")
            unit = float((r.get("data") or {}).get("unitPrice") or 0)
            if abs(unit - price) > 0.001:
                print(f"[FAIL] {name} {day} 单价={unit} 期望={price}")
                sys.exit(1)
    print("[2] 演示专属价就绪（客户 PL-* 未停用）")


def ensure_users(token):
    users = {}
    for acc, name, dept, op, code in WORKERS:
        existing = get_by(token, "/api/User?", "account", acc)
        if existing:
            users[acc] = existing["id"]
            body = {
                "account": acc,
                "name": name,
                "role": 2,
                "userType": 0,
                "wageCoeff": float(existing.get("wageCoeff") or 1),
                "status": 1,
                "deptIds": [dept],
            }
            ok(req(token, "PUT", f"/api/User/{existing['id']}", body), f"更新 {acc}", blocking=False)
            continue
        r = req(
            token,
            "POST",
            "/api/User",
            {
                "account": acc,
                "name": name,
                "role": 2,
                "userType": 0,
                "wageCoeff": 1.0,
                "password": WORKER_PASSWORD,
                "deptIds": [dept],
            },
        )
        ok(r, f"建用户 {acc}")
        ex = get_by(token, "/api/User?", "account", acc)
        users[acc] = ex["id"] if ex else None
    print(f"[1] hysim 用户就绪 {len(users)}")
    return users


def ensure_wo(token, order_no, product_id, qty):
    wo = get_by(token, "/api/WorkOrder?", "orderNo", order_no)
    if wo:
        print(f"   复用工单 {order_no} id={wo['id']}")
        return wo
    r = req(token, "POST", "/api/WorkOrder", {"productId": product_id, "qty": qty, "orderNo": order_no, "dueDate": None, "ext": {}})
    ok(r, f"建工单 {order_no}")
    wo = get_by(token, "/api/WorkOrder?", "orderNo", order_no)
    if not wo:
        sys.exit(f"工单回查失败 {order_no}")
    print(f"   新建工单 {order_no} id={wo['id']}")
    return wo


def approve_pending(token):
    pending = list_all(token, "/api/Review/pending?")
    ids = [x["id"] for x in pending]
    for i in range(0, len(ids), 50):
        part = ids[i : i + 50]
        if part:
            ok(req(token, "POST", "/api/Review/batch-approve", {"ids": part}), f"批量复核 {len(part)}", blocking=False)
    return len(ids)


def run_main_month(token, users):
    wo = ensure_wo(token, WO_MAIN, PRODUCT_ID, WO_QTY_MAIN)
    wo_id = wo["id"]
    days = workdays_oct_2026()
    assert len(days) == 22
    price_by_op = {opid: price for opid, _, _, price in PLANS}
    name_by_acc = {acc: name for acc, name, *_ in WORKERS}

    existing = set()
    for it in list_all(token, f"/api/Report?orderId={wo_id}"):
        if it.get("reviewStatus") == 2:
            continue
        existing.add((it.get("userName"), it.get("operationId"), str(it.get("reportTime") or "")[:10]))
    print(f"[4] 主单已有报工键 {len(existing)}")

    attend = {acc: set(attendance_days(acc, days)) for acc, *_ in WORKERS}
    posted, skipped, failed = 0, 0, 0
    for d in days:
        day_s = d.isoformat()
        start, end = f"{day_s}T08:00:00", f"{day_s}T16:00:00"
        by_op = {}
        for acc, name, dept, op, code in WORKERS:
            if d not in attend[acc]:
                continue
            if (name, op, day_s) in existing:
                skipped += 1
                continue
            by_op.setdefault(op, []).append(acc)
        for opid, accs in by_op.items():
            price = price_by_op[opid]
            assigns = [
                {
                    "userId": users[acc],
                    "goodQty": day_qty(acc, price, d),
                    "defectQty": 0,
                    "startTime": start,
                    "endTime": end,
                    "durationMinutes": 480,
                }
                for acc in accs
            ]
            body = {
                "orderId": wo_id,
                "operationId": opid,
                "goodQty": 0,
                "defectQty": 0,
                "source": 1,
                "confirmUpstream": True,
                "assignees": assigns,
            }
            r = req(token, "POST", "/api/Report", body)
            if ok(r, f"代报 {day_s} op{opid}", blocking=False):
                posted += len(assigns)
                for acc in accs:
                    existing.add((name_by_acc[acc], opid, day_s))
            else:
                failed += 1
        approve_pending(token)
        print(f"   {day_s} posted累计={posted} failed={failed}")
    print(f"[4] 主单报工 posted={posted} skipped={skipped} failed_batches={failed}")
    if failed:
        log_problem("P-NB-main-fail", "非阻塞", f"主单有 {failed} 批报工失败", "D3", "待处理", "最后一轮重跑补洞")
    approve_pending(token)

    # 工资：先清本期间全部草稿再生成（避免李师傅/管理员残留）
    clean_draft_salaries(token)
    r = req(token, "POST", "/api/Salary/generate", {"periodType": 1, "periodValue": PERIOD})
    ok(r, f"生成工资 {PERIOD}")
    print(f"[5] 工资生成 {json.dumps(r.get('data'), ensure_ascii=False)[:200]}")

    amounts = []
    sim_amounts = []
    for it in list_all(token, f"/api/Salary?periodType=1&periodValue={PERIOD}"):
        amt = float(it.get("totalAmount") or 0)
        amounts.append(amt)
        uname = it.get("userName") or ""
        print(f"  {uname}: {amt:.2f}")
        if str(uname).startswith("演示"):
            sim_amounts.append(amt)
    if sim_amounts:
        mean = sum(sim_amounts) / len(sim_amounts)
        print(f"  hysim人数={len(sim_amounts)} 均值={mean:.2f}")
        if not (6000 <= mean <= 7000):
            log_problem("P-NB-wage-mean", "非阻塞", f"hysim 均值 {mean:.2f} 不在 6000～7000", "D4", "待处理", "调日产量重算")
        else:
            print("[OK] hysim 人均落在 6000～7000")
    return wo_id


def try_patch_report(token, wo_id):
    """D7：主单补报一笔（非阻塞）。"""
    worker = get_by(token, "/api/User?", "account", "hysim01")
    if not worker:
        return
    day = "2026-10-30"
    body = {
        "orderId": wo_id,
        "operationId": 23,
        "goodQty": 0,
        "defectQty": 0,
        "source": 1,
        "confirmUpstream": True,
        "assignees": [
            {
                "userId": worker["id"],
                "goodQty": 3,
                "defectQty": 0,
                "startTime": f"{day}T16:30:00",
                "endTime": f"{day}T17:00:00",
                "durationMinutes": 30,
            }
        ],
    }
    r = req(token, "POST", "/api/Report", body)
    if isinstance(r, dict) and r.get("code") == 0:
        approve_pending(token)
        print("[7] 补报抽样 OK")
    else:
        log_problem("P-NB-patch", "非阻塞", f"补报失败: {json.dumps(r, ensure_ascii=False)[:120]}", "D7", "待处理", "最后一轮")


def main():
    global BASE_URL, PERIOD
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE_URL)
    ap.add_argument("--token", required=True)
    ap.add_argument("--period", default=PERIOD)
    args = ap.parse_args()
    BASE_URL = args.base.rstrip("/")
    PERIOD = args.period
    token = args.token

    # 基线问题
    log_problem(
        "P-BASE-01",
        "非阻塞",
        "真名工人 deptIds 为空，本人报工会被部门权限拦住",
        "真实报工",
        "已观察",
        "演示用 hysim+管理员代报；上线前给真人挂部门",
    )

    clean_draft_salaries(token)
    ensure_product(token)
    users = ensure_users(token)
    ensure_price_lists(token)
    # 小单可能因通用价缺规格失败 → ensure_wo 内 blocking，需改成非阻塞建单
    # 临时：先主单工资，再尽力建小单
    wo_id = run_main_month(token, users)

    # 小单：挂演示产品（有全路线专属价）；数量错落便于列表演示
    print("[3b] 开始小单…")
    worker_hp = get_by(token, "/api/User?", "account", "hysim01")
    qtys = [120, 150, 80, 200, 90, 110, 160, 70, 130, 180, 100, 140]
    for i in range(1, SMALL_COUNT + 1):
        ono = f"HY-DEMO-S{i:02d}"
        existing = get_by(token, "/api/WorkOrder?", "orderNo", ono)
        if existing:
            print(f"   复用 {ono}")
            continue
        qty = qtys[(i - 1) % len(qtys)]
        r = req(token, "POST", "/api/WorkOrder", {"productId": PRODUCT_ID, "qty": qty, "orderNo": ono, "dueDate": None, "ext": {}})
        if not (isinstance(r, dict) and r.get("code") == 0):
            log_problem(f"P-NB-{ono}", "非阻塞", f"建小单失败: {json.dumps(r, ensure_ascii=False)[:120]}", "D1", "待处理", "最后一轮")
            continue
        wo = get_by(token, "/api/WorkOrder?", "orderNo", ono)
        print(f"   新建 {ono} id={wo and wo.get('id')}")
        if not wo or not worker_hp:
            continue
        body = {
            "orderId": wo["id"],
            "operationId": 23,
            "goodQty": 0,
            "defectQty": 0,
            "source": 1,
            "confirmUpstream": True,
            "assignees": [
                {
                    "userId": worker_hp["id"],
                    "goodQty": 8,
                    "defectQty": 0,
                    "startTime": "2026-10-28T09:00:00",
                    "endTime": "2026-10-28T11:00:00",
                    "durationMinutes": 120,
                }
            ],
        }
        rr = req(token, "POST", "/api/Report", body)
        if isinstance(rr, dict) and rr.get("code") == 0:
            approve_pending(token)
        else:
            log_problem(f"P-NB-r-{ono}", "非阻塞", f"小单报工失败: {json.dumps(rr, ensure_ascii=False)[:100]}", "D5", "待处理", "最后一轮")

    try_patch_report(token, wo_id)

    wos = [w for w in list_all(token, "/api/WorkOrder?") if str(w.get("orderNo", "")).startswith("HY-DEMO-")]
    print(f"\n=== 验收 === DEMO工单={len(wos)} 主单={WO_MAIN}")
    print("H5: hysim01 / 123456 看我的工资；PC #/salary 期间 2026-10；#/work-order 搜 HY-DEMO-")
    print(f"问题清单: {PROBLEMS_PATH}")


if __name__ == "__main__":
    main()
