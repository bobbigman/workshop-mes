# -*- coding: utf-8 -*-
"""试点：包装(48) 打基础价 → HY-001/HY-002，保存+回查+预览。"""
import json, ssl, urllib.request, urllib.error, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
BASE = "https://103.39.222.133:11302"
SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\_pilot_包装.json"

def req(m, p, b=None, tok=None):
    data = json.dumps(b).encode("utf-8") if b is not None else None
    r = urllib.request.Request(BASE + p, data=data, method=m)
    r.add_header("Content-Type", "application/json")
    if tok: r.add_header("Authorization", "Bearer " + tok)
    try:
        with urllib.request.urlopen(r, context=CTX, timeout=40) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read().decode("utf-8", "replace"))

# login
j = req("POST", "/api/auth/login", {"Account": "admin", "Password": "Admin123"})[1]
d = j.get("data") or {}
tok = d.get("token")
if not tok and d.get("selectToken"):
    j2 = req("POST", "/api/auth/select-factory", {"SelectToken": d["selectToken"], "FactoryCode": "HY002"})[1]
    tok = (j2.get("data") or {}).get("token")

# 读取源数据，选包装明细打基础价，排除 R013(其他布套)/R014(灯光伞)
with open(SRC, encoding="utf-8") as f:
    recs = json.load(f)["records"]
def norm(s): return (s or "").replace("\n", " ").replace("\r", " ").strip()
rows = [r for r in recs
        if r["sheet"] == "包装 " and r["priceKind"] == "base" and r["unit"] == "打"
        and r["sourceId"] not in ("HY26-S04-R013", "HY26-S04-R014")]

def money(v):
    s = str(v).strip()
    if s.startswith("+"): s = s[1:]
    return float(s)

bases = []
for i, r in enumerate(rows, 1):
    amt = money(r["rawPrice"])
    for pid, pcode in ((22, "HY-001"), (21, "HY-002")):
        bases.append({
            "id": f"PK-{i:02d}-{pcode}",
            "name": f"{norm(r['description'])}（{pcode}）",
            "productId": pid, "price": amt, "deductPrice": 0,
            "priceUnit": "打", "reportUnitId": 18, "unitsPerPriceUnit": 12,
            "allowed": {}, "included": [],
            "_src": r["sourceId"],
        })

config = {"version": "", "groups": [], "bases": bases}

result = {"operationId": 48, "rowCount": len(rows), "baseCount": len(bases), "srcRows": [r["sourceId"] for r in rows]}

# 1) 读当前配置（应为 null）
st, before = req("GET", "/api/operation-pricing/48", tok=tok)
result["before"] = {"status": st, "data": before.get("data")}

# 2) PUT
st, put = req("PUT", "/api/operation-pricing/48", config, tok=tok)
result["put"] = {"status": st, "code": put.get("code"), "msg": put.get("msg"), "data": put.get("data")}

# 3) 回查
st, after = req("GET", "/api/operation-pricing/48", tok=tok)
d2 = after.get("data")
result["after"] = {"status": st, "version": (d2 or {}).get("version"),
                   "groupCount": len((d2 or {}).get("groups") or []),
                   "baseCount": len((d2 or {}).get("bases") or [])}

# 4) 预览：第一个 base，productId=22
first = bases[0]
st, prev = req("POST", "/api/operation-pricing/48/preview?productId=22",
               {"baseId": first["id"], "values": {}}, tok=tok)
result["preview"] = {"status": st, "code": prev.get("code"), "msg": prev.get("msg"),
                     "data": prev.get("data")}

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(result, f, ensure_ascii=False, indent=2, default=str)

print("rows:", len(rows), "bases:", len(bases),
      "putCode:", put.get("code"), "putMsg:", put.get("msg"),
      "afterVersion:", (d2 or {}).get("version"),
      "previewCode:", prev.get("code"))
