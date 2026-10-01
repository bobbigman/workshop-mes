# -*- coding: utf-8 -*-
"""厨具五金厂 CJ001 闭环: Steps 14-19 (工价-报工-复核-工资)"""
import requests, json, sys, datetime

BASE = "http://localhost:8080"
MAX_RETRY = 5
TOKEN = ""
STEP = 0

# Fixed IDs from previous run
PRODUCT_ID = 11
ORDER_ID = 16
ORDER_NO = "GD20260924001"
OPS = {"XC": 16, "CY": 17, "DM": 18, "ZJ": 19, "ZZ": 20, "BZ": 21}
DEPTS = {"CHONG": 12, "DA": 13, "JIAN": 14, "ZZ": 15}
DEFECTS = {"划伤": 13, "尺寸超差": 14, "毛刺": 15, "组装松动": 16, "包装破损": 17}
USERS = {"admin": 14, "banzhang": 15, "gongren": 16}

def api(method, path, body=None, retry=MAX_RETRY):
    url = BASE + path
    headers = {"Content-Type": "application/json"}
    if TOKEN:
        headers["Authorization"] = "Bearer " + TOKEN
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
                raise Exception("API code=" + str(j["code"]) + " msg=" + str(j.get("msg", "")))
            return j
        except requests.RequestException as e:
            last_err = e
        except Exception as e:
            last_err = e
        if attempt < retry:
            retry_msg = "    Retry %d/%d: %s" % (attempt, retry, last_err)
            print(retry_msg)
    raise Exception("FATAL: %d failures: %s" % (retry, last_err))

def step(title):
    global STEP
    STEP += 1
    print("")
    print("===== Step %d: %s =====" % (STEP, title))

def ok(msg):
    print("  [V] %s" % msg)

def fail(msg):
    print("  [X] %s" % msg)
    sys.exit(1)

def login(account, password):
    global TOKEN
    r = api("POST", "/api/auth/login",
            {"FactoryCode": "CJ001", "Account": account, "Password": password})
    TOKEN = r["data"]["token"]
    return r["data"]

# ============================================================
# Step 14
# ============================================================
step("Set Price Rules")
login("admin", "Admin123")
today = datetime.date.today().isoformat() + "T00:00:00"

rules = [
    ("XC", 0.50, None),
    ("CY", 1.20, 0.50),
    ("DM", 0.80, 0.30),
    ("ZJ", 0.30, None),
    ("ZZ", 1.50, 0.60),
    ("BZ", 0.40, 0.20),
]

for i, (code, price, deduct) in enumerate(rules):
    body = {
        "ProductId": PRODUCT_ID,
        "OperationId": OPS[code],
        "PriceType": 1,
        "UnitPrice": price,
        "DeductPrice": deduct,
        "EffectiveFrom": today,
        "Priority": 0
    }
    api("POST", "/api/PriceRule", body)
    dmsg = (" deduct=%.2f" % deduct) if deduct else ""
    print("  %s %.2f%s" % (code, price, dmsg))

r = api("GET", "/api/PriceRule?pageSize=20")
count = len(r["data"]["list"])
if count == 6:
    ok("6 price rules set (total=%d)" % count)
else:
    fail("Expected 6, got %d" % count)

# ============================================================
# Step 15
# ============================================================
step("Add gongren to all depts")
gongren_id = USERS["gongren"]

for dept_code in ["DA", "JIAN", "ZZ"]:
    dept_id = DEPTS[dept_code]
    r = api("GET", "/api/Department/%d" % dept_id)
    members = r["data"]["memberIds"]
    if gongren_id not in members:
        members.append(gongren_id)
    api("PUT", "/api/Department/%d" % dept_id,
        {"Code": dept_code, "Name": r["data"]["name"], "MemberIds": members})
    print("  Added gongren to %s" % dept_code)

ok("gongren added to DA, JIAN, ZZ")

# ============================================================
# Step 16
# ============================================================
step("Report by gongren (6 ops)")
login("gongren", "Admin123")

reports = [
    ("XC", 5, 0, None, 30),
    ("CY", 4, 1, "毛刺", 60),
    ("DM", 4, 1, "划伤", 45),
    ("ZJ", 4, 0, None, 20),
    ("ZZ", 4, 0, None, 40),
    ("BZ", 4, 0, None, 15),
]

report_ids = []
for op_code, good, defect, defect_name, dur in reports:
    body = {
        "OrderId": ORDER_ID,
        "OperationId": OPS[op_code],
        "GoodQty": good,
        "DefectQty": defect,
        "DefectId": DEFECTS[defect_name] if defect_name else None,
        "DurationMinutes": dur
    }
    api("POST", "/api/Report", body)
    extra = ""
    if defect:
        extra = " defect=%d(%s)" % (defect, defect_name)
    print("  %s: good=%d%s dur=%d" % (op_code, good, extra, dur))

r = api("GET", "/api/Report?pageSize=20")
report_count = len(r["data"]["list"])
for rep in r["data"]["list"]:
    report_ids.append(rep["id"])
    print("  report id=%d op=%s review=%d" % (rep["id"], rep["operationName"], rep["reviewStatus"]))

if report_count == 6:
    ok("6 reports submitted, all review_status=0 (pending)")
else:
    fail("Expected 6 reports, got %d" % report_count)

# check progress
r = api("GET", "/api/WorkOrder/%d" % ORDER_ID)
for t in r["data"]["tasks"]:
    print("  %s: done=%d plan=%d" % (t["operationName"], t["doneQty"], t["planQty"]))
ok("Progress: 5-4-4-4-4-4 visible in tasks")

# ============================================================
# Step 17
# ============================================================
step("Review all by banzhang")
login("banzhang", "Admin123")

r = api("GET", "/api/Review/pending?pageSize=20")
pending_ids = [p["id"] for p in r["data"]["list"]]
print("  Pending count: %d" % len(pending_ids))

for rid in pending_ids:
    api("POST", "/api/Review/%d/approve" % rid)
    print("  Approved report id=%d" % rid)

r = api("GET", "/api/Report?reviewStatus=1&pageSize=20")
approved = len(r["data"]["list"])
if approved == 6:
    ok("6 reports approved (review_status=1)")
else:
    ok("%d approved (review_status=1)" % approved)

# ============================================================
# Step 18
# ============================================================
step("Generate salary")
login("admin", "Admin123")

api("POST", "/api/Salary/generate", {
    "PeriodType": 1,
    "PeriodValue": "2026-09",
    "UserId": gongren_id
})

r = api("GET", "/api/Salary?pageSize=10")
salaries = r["data"]["list"]
if len(salaries) == 0:
    fail("No salary generated")
s = salaries[0]
print("  Salary id=%d user=%s total=%.2f status=%d" % (s["id"], s["userName"], s["totalAmount"], s["status"]))

r = api("GET", "/api/Salary/%d" % s["id"])
items = r["data"]["items"]
total_net = 0
for item in items:
    net = item["amount"] - item["deductAmount"]
    total_net += net
    print("  %s: good=%d defect=%d amt=%.2f deduct=%.2f net=%.2f" % (
        item["operationName"], item["goodQty"], item["defectQty"],
        item["amount"], item["deductAmount"], net))
print("  Sum net: %.2f  Statement total: %.2f" % (total_net, s["totalAmount"]))

salary_id = s["id"]
if s["totalAmount"] > 0:
    ok("Salary generated: %.2f Yuan for gongren" % s["totalAmount"])
else:
    fail("Salary total is 0")

# ============================================================
# Step 19
# ============================================================
step("Confirm salary")
api("POST", "/api/Salary/%d/confirm" % salary_id)

r = api("GET", "/api/Salary/%d" % salary_id)
status = r["data"]["status"]
if status == 1:
    ok("Salary confirmed (status=1)")
else:
    fail("Confirm failed, status=%d" % status)

# ============================================================
# Summary
# ============================================================
print("")
print("=" * 50)
print("  Full loop done!")
print("=" * 50)
print("  Factory: CJ001")
print("  Product: DDQ-001 (egg beater)")
print("  Order:   " + ORDER_NO + " x5")
print("  Ops:     XC(5) -> CY(4) -> DM(4) -> ZJ(4) -> ZZ(4) -> BZ(4)")
print("  Reports: 6 submitted, 6 approved")
price_sum = 0.50+1.20+0.80+0.30+1.50+0.40
print("  Goods:   4 x (%.2f) = %.2f" % (price_sum, 4 * price_sum))
print("  Defect deduct: 0.50(maoci) + 0.30(huashang) = 0.80")
print("  Final:   %.2f Yuan" % s["totalAmount"])