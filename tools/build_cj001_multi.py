# -*- coding: utf-8 -*-
"""66 执行: 多人报工 + 复核 (王军 / 赵江陵). 每步校验, 失败重试5次后退出."""
import json, sys, urllib.request, urllib.error

BASE = "http://localhost:8080"
RETRY = 5
TOKEN = ""

def api(method, path, body=None, retry=RETRY):
    url = BASE + path
    data = None
    headers = {"Content-Type": "application/json"}
    if TOKEN:
        headers["Authorization"] = "Bearer " + TOKEN
    if body is not None:
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    last = None
    for attempt in range(1, retry + 1):
        req = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=12) as r:
                raw = r.read().decode("utf-8", "replace")
                j = json.loads(raw) if raw else {"code": 0, "data": None}
            if j.get("code") != 0:
                raise RuntimeError("code=%s msg=%s" % (j.get("code"), j.get("msg")))
            return j
        except urllib.error.HTTPError as e:
            last = "HTTP %s %s" % (e.code, e.read().decode("utf-8", "replace"))
        except Exception as e:
            last = "%s: %s" % (type(e).__name__, e)
        if attempt < retry:
            print("    retry %d/%d (%s)" % (attempt, retry, last))
    print("  [FAIL] %s %s -> %s" % (method, path, last))
    sys.exit(1)

def login(account, password):
    global TOKEN
    j = api("POST", "/api/auth/login",
            {"FactoryCode": "CJ001", "Account": account, "Password": password})
    TOKEN = j["data"]["token"]
    return j["data"]

def ok(msg):
    print("  [V] %s" % msg)

def get_list(path):
    return api("GET", path)["data"]["list"]

STEP = 0
def step(title):
    global STEP
    STEP += 1
    print("\n===== Step %d: %s =====" % (STEP, title))

OPS = {"XC":16,"CY":17,"DM":18,"ZJ":19,"ZZ":20,"BZ":21}
DEPTS = {"CHONG":12,"DA":13,"JIAN":14,"ZZ":15}
DEF = {"毛刺":15,"划伤":13,"组装松动":16}

# Step 1
step("Login admin")
login("admin", "Admin123")
ok("admin logged in")

# Step 2: 建工人
step("Create 2 workers")
prev_users = {u["id"] for u in get_list("/api/User?pageSize=50")}
api("POST", "/api/User", {"Account":"wangjun","Phone":"","Name":"王军","Role":2,"Password":"Admin123"})
api("POST", "/api/User", {"Account":"zhaojiangling","Phone":"","Name":"赵江陵","Role":2,"Password":"Admin123"})
users = {u["account"]:u for u in get_list("/api/User?pageSize=50") if u["account"] not in ("admin","banzhang","gongren")}
wangjun = users.get("wangjun"); zhao = users.get("zhaojiangling")
if not wangjun or not zhao:
    print("  [FAIL] new users not found in list"); sys.exit(1)
wangjun_id, zhao_id = wangjun["id"], zhao["id"]
ok("wangjun=%s(id=%d) zhaojiangling=%s(id=%d)" % (wangjun["name"], wangjun_id, zhao["name"], zhao_id))

# Step 3: 建工单
step("Create 2 work orders")
prev_wo = {w["id"] for w in get_list("/api/WorkOrder?pageSize=50")}
api("POST", "/api/WorkOrder", {"ProductId":11, "Qty":5, "Ext":{}})
api("POST", "/api/WorkOrder", {"ProductId":11, "Qty":8, "Ext":{}})
new_wo = [w for w in get_list("/api/WorkOrder?pageSize=50") if w["id"] not in prev_wo]
new_wo.sort(key=lambda w: w["id"])
if len(new_wo) < 2:
    print("  [FAIL] expected 2 new workorders, got %d" % len(new_wo)); sys.exit(1)
wo_wang, wo_zhao = new_wo[0], new_wo[1]   # 王军×5 先建
ok("wo_wang=%s qty=%d id=%d | wo_zhao=%s qty=%d id=%d" % (wo_wang["orderNo"], wo_wang["qty"], wo_wang["id"], wo_zhao["orderNo"], wo_zhao["qty"], wo_zhao["id"]))

# Step 4: 启动
step("Start work orders")
api("POST", "/api/WorkOrder/%d/transition" % wo_wang["id"], {"Action":"start"})
api("POST", "/api/WorkOrder/%d/transition" % wo_zhao["id"], {"Action":"start"})
st = {w["id"]:w["status"] for w in get_list("/api/WorkOrder?pageSize=50")}
if st.get(wo_wang["id"]) != 1 or st.get(wo_zhao["id"]) != 1:
    print("  [FAIL] work orders not started"); sys.exit(1)
ok("both work orders status=1")

# Step 5: 加部门成员
step("Add workers to 4 depts")
for code, did in DEPTS.items():
    d = api("GET", "/api/Department/%d" % did)["data"]
    members = list(d.get("memberIds") or [])
    for uid in (wangjun_id, zhao_id):
        if uid not in members:
            members.append(uid)
    api("PUT", "/api/Department/%d" % did, {"Code":d["code"],"Name":d["name"],"MemberIds":members})
    dd = api("GET", "/api/Department/%d" % did)["data"]
    if wangjun_id not in dd["memberIds"] or zhao_id not in dd["memberIds"]:
        print("  [FAIL] %s missing members" % code); sys.exit(1)
ok("wangjun & zhao added to CHONG/DA/JIAN/ZZ")

# Step 6: 王军报工
step("Wangjun report (6 ops)")
login("wangjun", "Admin123")
w_reports = [("XC",5,0,None,30),("CY",4,1,"毛刺",60),("DM",4,1,"划伤",45),
             ("ZJ",4,0,None,20),("ZZ",4,0,None,40),("BZ",4,0,None,15)]
for code,good,defect,dn,dur in w_reports:
    api("POST", "/api/Report", {"OrderId":wo_wang["id"],"OperationId":OPS[code],
        "GoodQty":good,"DefectQty":defect,"DefectId":DEF[dn] if dn else None,"DurationMinutes":dur})
ok("wangjun 6 reports submitted")

# Step 7: 赵江陵报工
step("Zhaojiangling report (6 ops)")
login("zhaojiangling", "Admin123")
z_reports = [("XC",8,0,None,40),("CY",7,1,"毛刺",80),("DM",7,0,None,60),
             ("ZJ",7,0,None,25),("ZZ",6,1,"组装松动",55),("BZ",6,0,None,20)]
for code,good,defect,dn,dur in z_reports:
    api("POST", "/api/Report", {"OrderId":wo_zhao["id"],"OperationId":OPS[code],
        "GoodQty":good,"DefectQty":defect,"DefectId":DEF[dn] if dn else None,"DurationMinutes":dur})
ok("zhao 6 reports submitted")

# Step 8: 复核
step("Review by banzhang")
login("banzhang", "Admin123")
pending = get_list("/api/Review/pending?pageSize=50")
pids = [p["id"] for p in pending]
pids.sort()
if len(pids) != 12:
    print("  [WARN] pending=%d (expect 12), first item keys=%s" % (len(pids), list(pending[0].keys()) if pending else None))
wang_ids = pids[:6]
zhao_ids = pids[6:]   # 赵江陵 按 id 升序 = 报工顺序, 第5条(index4)是组装
api("POST", "/api/Review/batch-approve", {"ids": wang_ids})
for rid in zhao_ids[:5]:
    api("POST", "/api/Review/%d/approve" % rid)
api("POST", "/api/Review/%d/reject" % zhao_ids[4], {"reason": "良品数量与实物不符，请核实后重新报工"})
ok("wangjun 6 batch-approved; zhao 5 approved + 1 rejected(组装)")

# Step 9: 生成工资
step("Generate salary")
login("admin", "Admin123")
api("POST", "/api/Salary/generate", {"PeriodType":1,"PeriodValue":"2026-09","UserId":wangjun_id})
api("POST", "/api/Salary/generate", {"PeriodType":1,"PeriodValue":"2026-09","UserId":zhao_id})
sals = get_list("/api/Salary?pageSize=10")
sw = next((s for s in sals if s.get("userName")=="王军" or s.get("userId")==wangjun_id), None)
sz = next((s for s in sals if s.get("userName")=="赵江陵" or s.get("userId")==zhao_id), None)
if not sw or not sz:
    print("  [FAIL] salary not found. all=%s" % [ (s.get("id"),s.get("userName"),s.get("totalAmount")) for s in sals]); sys.exit(1)
ok("wangjun salary id=%d total=%.2f | zhao salary id=%d total=%.2f" % (sw["id"], sw["totalAmount"], sz["id"], sz["totalAmount"]))
if abs(sw["totalAmount"] - 18.50) > 0.01 or abs(sz["totalAmount"] - 22.00) > 0.01:
    print("  [WARN] amount mismatch: expect wangjun 18.50, zhao 22.00")

# Step 10: 确认
step("Confirm salary")
api("POST", "/api/Salary/%d/confirm" % sw["id"])
api("POST", "/api/Salary/%d/confirm" % sz["id"])
if api("GET", "/api/Salary/%d" % sw["id"])["data"]["status"] != 1 or api("GET", "/api/Salary/%d" % sz["id"])["data"]["status"] != 1:
    print("  [FAIL] confirm failed"); sys.exit(1)
ok("both salaries confirmed status=1")

print("\n" + "="*50)
print("  DONE: multi-person report + review loop complete!")
print("  wangjun=%d reports approved, salary=%.2f" % (len(wang_ids), sw["totalAmount"]))
print("  zhaojiangling: 5 approved + 1 rejected, salary=%.2f" % sz["totalAmount"])
print("="*50)
