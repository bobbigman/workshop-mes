# -*- coding: utf-8 -*-
"""华煜测试账套 · 2026-10 整月计件工资模拟（约 25 人、人均≈6000）

走接口：清理草稿工资/演示报工 → 停用冲突价目 → 建 OCT 专属价 → 工人 → 大工单
→ 工作日代报（带起止时间）→ 批量复核 → 生成工资。

用法：
  python sim_hy_oct2026.py --base https://103.39.222.133:11302 --token "Bearer <管理员令牌>"
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

# ============================ CONFIG ============================
BASE_URL = "https://103.39.222.133:11302"
TOKEN = ""
PERIOD = "2026-10"
# 新产品：避开 P-A01 上旧 SIM 专属价在修订时间线里仍「历史生效」导致冲突
PRODUCT_CODE = "P-OCT-SIM"
PRODUCT_NAME = "十月模拟直骨25寸"
PRODUCT_ID = 0  # 运行时按编号回查；没有则按 P-A01 属性新建
SOURCE_PRODUCT_ID = 23  # 复制属性/单位/路线
PRODUCT_NO = "P-OCT-SIM"
WO_NO = "HY-OCT202610"
WO_QTY = 300000
EFFECTIVE_FROM = "2026-10-01"
REPORT_UNIT_ID = 18  # 把
PRICE_PREFIX = "OCTS"  # 新编号，保证 create 时修订段从 10-01 起
WORKER_PASSWORD = "123456"
DAY_INCOME_LO, DAY_INCOME_HI = 270.0, 295.0  # 满勤22天 → 约 5940~6490，均值≈6000

# 报工用工序：专属价（元/把）；包边=路线上的「拉边」位（op38，权限在合片组）
PLANS = [
    (42, "HP", "合片", 0.45),
    (46, "FS", "缝伞", 0.40),
    (38, "LB", "包边", 0.30),
    (41, "XD", "小裁", 0.20),
    (44, "DD", "打顶", 0.25),
    (45, "ZW", "打珠尾", 0.30),
    (47, "PJ", "品检", 0.22),
    (48, "BZ", "包装", 0.18),
]
# 路线其余工序：建工单锁价需要，不安排工人报工
FILLER_PLANS = [
    (37, "DC", "大裁", 0.10),
    (40, "YB", "验布", 0.10),
    (43, "BD", "打带", 0.10),
    (49, "PJZ", "配件", 0.10),
    (50, "ZSB", "装伞柄", 0.10),
]

# (账号, 姓名, 部门id, 工序id, 代号)
# 部门须与工序报工权限一致：合片/包边→43，缝伞/包装→40，小裁→44，打顶→42，珠尾→41，品检→37
WORKERS = [
    ("sim01", "张伟", 43, 42, "HP"),
    ("sim02", "李娜", 43, 42, "HP"),
    ("sim11", "周杰", 43, 42, "HP"),
    ("sim12", "吴芳", 43, 42, "HP"),
    ("sim03", "王强", 40, 46, "FS"),
    ("sim04", "赵敏", 40, 46, "FS"),
    ("sim13", "郑浩", 40, 46, "FS"),
    ("sim14", "冯丽", 40, 46, "FS"),
    ("sim15", "陈静", 43, 38, "LB"),
    ("sim16", "刘洋", 43, 38, "LB"),
    ("sim17", "黄蕾", 43, 38, "LB"),
    ("sim05", "孙五", 44, 41, "XD"),
    ("sim18", "马超", 44, 41, "XD"),
    ("sim19", "朱婷", 44, 41, "XD"),
    ("sim06", "周涛", 42, 44, "DD"),
    ("sim07", "吴磊", 42, 44, "DD"),
    ("sim20", "何军", 42, 44, "DD"),
    ("sim08", "郑爽", 41, 45, "ZW"),
    ("sim21", "罗斌", 41, 45, "ZW"),
    ("sim09", "钱芳", 37, 47, "PJ"),
    ("sim22", "谢娜", 37, 47, "PJ"),
    ("sim23", "唐伟", 37, 47, "PJ"),
    ("sim10", "陈明", 40, 48, "BZ"),
    ("sim24", "曹雪", 40, 48, "BZ"),
    ("sim25", "彭飞", 40, 48, "BZ"),
]

# 稳定缺勤：从月末工作日往前扣
ABSENCE = {
    "sim03": 1, "sim07": 2, "sim11": 1, "sim16": 1, "sim19": 2,
    "sim22": 1, "sim25": 1,
}

# 清理时停用：旧 OCT/SIM/VIDEO 专属（避免与新产品 OCTS 叠历史修订）；通用 PL 也可停以免误用高单价
STOP_PRICE_CODES = {
    "PL-HP-01", "PL-FS-01", "PL-LB-01", "PL-XD-01", "PL-DD-01", "PL-ZW-01", "PL-PJ-01", "PL-BZ-01", "PL-DD-02",
    "SIM-HP-A01", "SIM-FS-A01", "SIM-LB-A01", "SIM-XD-A01", "SIM-DD-A01", "SIM-ZW-A01", "SIM-PJ-A01", "SIM-BZ-A01",
    "SIM-HP-VIDEO47", "SIM-BZ-VIDEO47",
    "SIM-HP-HY001", "SIM-FS-HY001", "SIM-XD-HY001", "SIM-DD-HY001", "SIM-BD-HY001",
    "SIM-ZW-HY001", "SIM-PJ-HY001", "SIM-BZ-HY001", "SIM-DC-HY001", "SIM-BB-HY001",
    "SIM-YB-HY001", "SIM-PJZ-HY001", "SIM-ZSB-HY001",
    "OCT-HP-A01", "OCT-FS-A01", "OCT-LB-A01", "OCT-XD-A01", "OCT-DD-A01", "OCT-ZW-A01", "OCT-PJ-A01", "OCT-BZ-A01",
}

DEMO_WO_NOS = {"A0001"}  # 宣传演示小单
# ================================================================

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE


def req(token, method, path, body=None, timeout=120, retries=3):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    last = None
    for attempt in range(retries):
        r = urllib.request.Request(
            BASE_URL + path,
            data=data,
            method=method,
            headers={
                "Authorization": token if token.lower().startswith("bearer ") else f"Bearer {token}",
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


def ok(resp, what):
    if isinstance(resp, dict) and resp.get("code") == 0:
        return True
    print(f"[FAIL] {what}: {json.dumps(resp, ensure_ascii=False)[:500]}")
    return False


def list_all(token, path, page_size=100):
    out, page = [], 1
    while True:
        sep = "&" if "?" in path else "?"
        r = req(token, "GET", f"{path}{sep}page={page}&pageSize={page_size}")
        data = (r.get("data") or {}).get("list") or []
        out.extend(data)
        total = (r.get("data") or {}).get("total")
        if total is not None and len(out) >= int(total):
            break
        if len(data) < page_size:
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
    wave = stable_unit(f"{acc}-{day.isoformat()}-wave", 0.90, 1.10)
    income = base * wave
    return max(1, int(round(income / price)))


def clean(token):
    print("=== 清理旧模拟/冲突数据 ===")
    # 1) 删 10 月草稿工资
    for it in list_all(token, f"/api/Salary?periodType=1&periodValue={PERIOD}"):
        sid = it["id"]
        if it.get("status") == 0:
            ok(req(token, "DELETE", f"/api/Salary/{sid}"), f"删工资草稿 {sid} {it.get('userName')}")
        else:
            print(f"  跳过非草稿工资 {sid} status={it.get('status')}")

    # 2) 撤回待复核报工 + 删演示工单
    reports = list_all(token, "/api/Report?")
    for it in reports:
        if it.get("reviewStatus") == 0:
            ok(req(token, "POST", f"/api/Report/{it['id']}/withdraw"), f"撤回报工 {it['id']}")
    for ono in DEMO_WO_NOS:
        wo = get_by(token, "/api/WorkOrder?", "orderNo", ono)
        if wo:
            ok(req(token, "DELETE", f"/api/WorkOrder/{wo['id']}"), f"删工单 {ono}")

    # 旧十月工单若存在且要重建：先尽量撤回其报工再删
    old = get_by(token, "/api/WorkOrder?", "orderNo", WO_NO)
    if old:
        for it in list_all(token, f"/api/Report?orderId={old['id']}"):
            if it.get("reviewStatus") == 0:
                req(token, "POST", f"/api/Report/{it['id']}/withdraw")
            elif it.get("reviewStatus") == 1:
                # 已通过：先退回再撤（若无退回权限则跳过删单）
                req(token, "POST", f"/api/Review/{it['id']}/reject", {"reason": "十月模拟重建清理"})
                req(token, "POST", f"/api/Report/{it['id']}/withdraw")
        ok(req(token, "DELETE", f"/api/WorkOrder/{old['id']}"), f"删工单 {WO_NO}")

    # 3) 停用冲突价目（不动 OCT-*，后面 ensure 会启用/更新）
    for it in list_all(token, "/api/operation-price-list?"):
        code = it.get("code") or ""
        if code in STOP_PRICE_CODES and it.get("status") == 1:
            ok(
                req(
                    token,
                    "POST",
                    f"/api/operation-price-list/{it['id']}/status",
                    {"status": 2, "expectedVersion": it.get("version")},
                ),
                f"停用价目 {code}",
            )
    print("[clean] 完成")


def _oct_details(name, price):
    return {
        "schemaVersion": 2,
        "requiredFieldIds": [],
        "groups": [],
        "rows": [
            {
                "rowId": "R1",
                "kind": "base",
                "label": f"{PRODUCT_NO}{name}",
                "conditions": [],
                "amount": price,
                "deductPrice": 0,
                "groupKey": None,
                "includedRowIds": [],
                "confirmed": True,
                "source": "十月模拟",
                "note": "",
            }
        ],
    }


def ensure_product(token):
    global PRODUCT_ID
    existing = get_by(token, "/api/Product?", "code", PRODUCT_CODE)
    if existing:
        PRODUCT_ID = existing["id"]
        print(f"[0] 产品复用 {PRODUCT_CODE} id={PRODUCT_ID}")
        return PRODUCT_ID
    src = req(token, "GET", f"/api/Product/{SOURCE_PRODUCT_ID}")
    if not ok(src, "读源产品"):
        sys.exit(1)
    p = src["data"]
    body = {
        "code": PRODUCT_CODE,
        "name": PRODUCT_NAME,
        "unitId": p["unitId"],
        "routingId": p["routingId"],
        "supplier": "十月模拟",
        "price": None,
        "customValues": p.get("customValues") or {},
        "isRiskFocus": False,
    }
    if not ok(req(token, "POST", "/api/Product", body), f"建产品 {PRODUCT_CODE}"):
        sys.exit(1)
    existing = get_by(token, "/api/Product?", "code", PRODUCT_CODE)
    if not existing:
        sys.exit("新建产品后回查失败")
    PRODUCT_ID = existing["id"]
    print(f"[0] 产品新建 {PRODUCT_CODE} id={PRODUCT_ID}")
    return PRODUCT_ID


def _ensure_one_price(token, opid, code, name, price):
    list_key = f"{PRICE_PREFIX}-{code}-A01"
    existing = get_by(token, f"/api/operation-price-list?operationId={opid}&keyword={list_key}&", "code", list_key)
    if existing and existing.get("status") == 1 and existing.get("productId") == PRODUCT_ID:
        print(f"   复用价目表 {list_key} id={existing['id']}")
        return
    if existing and existing.get("status") != 1:
        list_key = f"{PRICE_PREFIX}-{code}-A01B"
        existing = get_by(token, f"/api/operation-price-list?operationId={opid}&keyword={list_key}&", "code", list_key)
        if existing and existing.get("status") == 1:
            print(f"   复用价目表 {list_key} id={existing['id']}")
            return
    if existing and existing.get("status") == 1:
        print(f"   复用价目表 {list_key} id={existing['id']}")
        return
    body = {
        "productId": PRODUCT_ID,
        "remark": "十月模拟专属价目表",
        "code": list_key,
        "name": f"十月模拟-{name}({PRODUCT_NO})",
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
    if ok(r, f"建价目表 {list_key}"):
        print(f"   价目表 {list_key} id={r.get('data')}")


def ensure_price_lists(token):
    for opid, code, name, price in PLANS + FILLER_PLANS:
        _ensure_one_price(token, opid, code, name, price)
    # 冒烟：必须命中专属单价（合片 0.45），不能落到通用表 5 元
    for opid, code, name, price in PLANS:
        for day in ("2026-10-01", "2026-10-31"):
            r = req(
                token,
                "POST",
                "/api/operation-price-list/preview",
                {"productId": PRODUCT_ID, "operationId": opid, "date": day},
            )
            if not ok(r, f"预览 {name} {day}"):
                sys.exit(1)
            unit = float((r.get("data") or {}).get("unitPrice") or 0)
            if abs(unit - price) > 0.001:
                print(f"[FAIL] {name} {day} 单价={unit} 期望={price}（可能命中了通用价目表）")
                sys.exit(1)
    print("[2] OCTS 专属价目表就绪")


def ensure_users(token):
    users = {}
    for acc, name, dept, op, code in WORKERS:
        existing = get_by(token, "/api/User?", "account", acc)
        if existing:
            users[acc] = existing["id"]
            # 对齐姓名/部门
            body = {
                "account": acc,
                "name": name,
                "role": 2,
                "userType": 0,
                "wageCoeff": float(existing.get("wageCoeff") or 1),
                "status": 1,
                "deptIds": [dept],
            }
            # 不强制改密码
            ok(req(token, "PUT", f"/api/User/{existing['id']}", body), f"更新用户 {acc}")
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
        if ok(r, f"建用户 {acc}"):
            ex = get_by(token, "/api/User?", "account", acc)
            users[acc] = ex["id"] if ex else None
    print(f"[1] 用户就绪: {len(users)} 个")
    return users


def ensure_wo(token):
    wo = get_by(token, "/api/WorkOrder?", "orderNo", WO_NO)
    if not wo:
        r = req(token, "POST", "/api/WorkOrder", {"productId": PRODUCT_ID, "qty": WO_QTY, "orderNo": WO_NO, "dueDate": None, "ext": {}})
        if not ok(r, f"建工单 {WO_NO}"):
            sys.exit(1)
        wo = get_by(token, "/api/WorkOrder?", "orderNo", WO_NO)
    print(f"[3] 工单 {WO_NO} id={wo['id']} qty={wo.get('qty')} 就绪")
    return wo


def approve_pending(token, order_id=None):
    pending = list_all(token, "/api/Review/pending?")
    if order_id:
        # pending 列表字段可能带 orderId / orderNo
        wo = get_by(token, "/api/WorkOrder?", "orderNo", WO_NO)
        ono = wo.get("orderNo") if wo else WO_NO
        pending = [x for x in pending if x.get("orderId") == order_id or x.get("orderNo") == ono]
    ids = [x["id"] for x in pending if x.get("reviewStatus", 0) == 0 or "id" in x]
    # pending 接口本身就是待复核
    ids = [x["id"] for x in pending]
    chunk = 50
    for i in range(0, len(ids), chunk):
        part = ids[i : i + chunk]
        if not part:
            continue
        ok(req(token, "POST", "/api/Review/batch-approve", {"ids": part}), f"批量复核 {len(part)}")
    return len(ids)


def run_month(token):
    ensure_product(token)
    users = ensure_users(token)
    ensure_price_lists(token)
    wo = ensure_wo(token)
    wo_id = wo["id"]
    days = workdays_oct_2026()
    assert len(days) == 22, f"期望 22 个工作日，实际 {len(days)}"
    price_by_op = {opid: price for opid, _, _, price in PLANS}
    name_by_acc = {acc: name for acc, name, *_ in WORKERS}

    existing = set()
    for it in list_all(token, f"/api/Report?orderId={wo_id}"):
        if it.get("reviewStatus") == 2:
            continue
        day_s = str(it.get("reportTime") or "")[:10]
        existing.add((it.get("userName"), it.get("operationId"), day_s))
    print(f"[4] 已有报工键 {len(existing)}；工作日 {len(days)}")

    attend = {acc: set(attendance_days(acc, days)) for acc, *_ in WORKERS}
    posted, skipped, failed = 0, 0, 0

    for d in days:
        day_s = d.isoformat()
        start = f"{day_s}T08:00:00"
        end = f"{day_s}T16:00:00"
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
            assigns = []
            for acc in accs:
                good = day_qty(acc, price, d)
                assigns.append(
                    {
                        "userId": users[acc],
                        "goodQty": good,
                        "defectQty": 0,
                        "startTime": start,
                        "endTime": end,
                        "durationMinutes": 480,
                    }
                )
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
            if ok(r, f"代报 {day_s} 工序{opid}×{len(assigns)}"):
                posted += len(assigns)
                for acc in accs:
                    existing.add((name_by_acc[acc], opid, day_s))
            else:
                failed += 1
        # 每天复核一次，避免堆积
        approve_pending(token, wo_id)
        print(f"   日进度 {day_s} posted累计={posted} failed={failed}")

    print(f"[4] 报工完成 posted={posted} skipped={skipped} failed_batches={failed}")
    if failed:
        sys.exit(1)

    n = approve_pending(token, wo_id)
    print(f"[4b] 收尾复核 {n} 条")

    # 工资
    sal = req(token, "GET", f"/api/Salary?periodType=1&periodValue={PERIOD}&page=1&pageSize=5")
    if (sal.get("data") or {}).get("total", 0) > 0:
        print(f"[5] 工资期间 {PERIOD} 已有单据，先删草稿再生成")
        for it in list_all(token, f"/api/Salary?periodType=1&periodValue={PERIOD}"):
            if it.get("status") == 0:
                req(token, "DELETE", f"/api/Salary/{it['id']}")
    r = req(token, "POST", "/api/Salary/generate", {"periodType": 1, "periodValue": PERIOD})
    if not ok(r, f"生成工资 {PERIOD}"):
        sys.exit(1)
    print(f"[5] 工资 {PERIOD} 已生成: {json.dumps(r.get('data'), ensure_ascii=False)[:200]}")

    print(f"\n=== 工资单 {PERIOD} ===")
    amounts = []
    for it in list_all(token, f"/api/Salary?periodType=1&periodValue={PERIOD}"):
        amt = float(it.get("totalAmount") or 0)
        amounts.append(amt)
        print(f"  {it.get('userName')}: 应发 {amt:.2f} 状态{it.get('status')}")
    if amounts:
        mean = sum(amounts) / len(amounts)
        print(f"  人数={len(amounts)} 合计={sum(amounts):.2f} 均值={mean:.2f}")
        if not (5500 <= mean <= 6500):
            print(f"[WARN] 均值 {mean:.2f} 不在目标 [5500,6500]")
        else:
            print("[OK] 人均落在约 6000 目标区间")

    print("\n完成。PC 看 #/salary；工人账号 sim01~sim25 / 123456。")


def main():
    global BASE_URL, PERIOD
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE_URL)
    ap.add_argument("--token", required=True)
    ap.add_argument("--period", default=PERIOD)
    ap.add_argument("--skip-clean", action="store_true")
    args = ap.parse_args()
    BASE_URL = args.base.rstrip("/")
    PERIOD = args.period
    token = args.token
    if not args.skip_clean:
        clean(token)
    run_month(token)


if __name__ == "__main__":
    main()
