# -*- coding: utf-8 -*-
"""造 10 条【待复核】报工: 新建2工单, 王军报10条, 不复核(review=0)."""
import json, sys, urllib.request, urllib.error

BASE = "http://localhost:8080"
RETRY = 5
TOKEN = ""

def api(method, path, body=None, retry=RETRY):
    global TOKEN
    url = BASE + path
    data = None
    headers = {"Content-Type": "application/json"}
    if TOKEN: headers["Authorization"] = "Bearer " + TOKEN
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

def login(acc, pwd):
    global TOKEN
    j = api("POST", "/api/auth/login", {"FactoryCode":"CJ001","Account":acc,"Password":pwd})
    TOKEN = j["data"]["token"]

def get_list(path):
    return api("GET", path)["data"]["list"]

OPS = {"XC":16,"CY":17,"DM":18,"ZJ":19,"ZZ":20,"BZ":21}

print("===== Step 1: login admin =====")
login("admin", "Admin123")
print("  [V] admin ok")

print("===== Step 2: create 2 work orders (x5) =====")
prev = {w["id"] for w in get_list("/api/WorkOrder?pageSize=50")}
api("POST", "/api/WorkOrder", {"ProductId":11,"Qty":5,"Ext":{}})
api("POST", "/api/WorkOrder", {"ProductId":11,"Qty":5,"Ext":{}})
new = [w for w in get_list("/api/WorkOrder?pageSize=50") if w["id"] not in prev]
new.sort(key=lambda w: w["id"])
if len(new) < 2:
    print("  [FAIL] need 2 new orders"); sys.exit(1)
woA, woB = new[0], new[1]
print("  woA=%s id=%d | woB=%s id=%d" % (woA["orderNo"], woA["id"], woB["orderNo"], woB["id"]))

print("===== Step 3: start both =====")
api("POST", "/api/WorkOrder/%d/transition" % woA["id"], {"Action":"start"})
api("POST", "/api/WorkOrder/%d/transition" % woB["id"], {"Action":"start"})
print("  [V] started")

print("===== Step 4: wangjun report 10 (no review) =====")
login("wangjun", "Admin123")
# woA: 6 ops (XC..BZ), each good=5
# woB: 4 ops (XC,CY,DM,ZJ), each good=5
planA = [("XC",5),("CY",5),("DM",5),("ZJ",5),("ZZ",5),("BZ",5)]
planB = [("XC",5),("CY",5),("DM",5),("ZJ",5)]
for code, good in planA:
    api("POST", "/api/Report", {"OrderId":woA["id"],"OperationId":OPS[code],
        "GoodQty":good,"DefectQty":0,"DefectId":None,"DurationMinutes":20})
    print("  A %s good=%d" % (code, good))
for code, good in planB:
    api("POST", "/api/Report", {"OrderId":woB["id"],"OperationId":OPS[code],
        "GoodQty":good,"DefectQty":0,"DefectId":None,"DurationMinutes":20})
    print("  B %s good=%d" % (code, good))

print("===== Step 5: verify pending count =====")
r = get_list("/api/Report?pageSize=100")
pend = [x for x in r if x["reviewStatus"] == 0]
print("  total reports=%d | pending(review=0)=%d" % (len(r), len(pend)))
if len(pend) == 10:
    print("  [V] exactly 10 pending reports created")
else:
    print("  [WARN] pending=%d (expect 10)" % len(pend))
print("DONE")
