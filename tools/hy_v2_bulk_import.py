# -*- coding: utf-8 -*-
"""批量写入 6 工序基础价（三折+通用 → HY-001/HY-002），不含附加价。"""
import json, ssl, urllib.request, urllib.error, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
BASE = "https://103.39.222.133:11302"
SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-导入结果.json"

def norm(s): return (s or "").replace("\n", " ").replace("\r", " ").strip()

def classify(name):
    n = name
    if any(k in n for k in ("二次包装", "二包")): return "HY004"
    if any(k in n for k in ("二折", "2折", "两折", "童伞", "儿童伞", "童")): return "HY003"
    if any(k in n for k in ("直骨", "高尔夫", "风扇", "头盔", "海滩", "沙滩", "玩具", "情侣",
                            "偏心", "五折", "六折", "四折", "反三折", "灯光", "莲花", "帽子",
                            "耳朵", "夹具", "背包", "荷叶", "四方", "拐杖", "鸭子")): return "PEND"
    if any(k in n for k in ("三折", "3折")): return "GO"
    if any(k in n for k in ("反向", "反伞")): return "PEND"
    return "GO"  # 通用

def worktype(r):
    s = r["sheet"]; op = norm(r["operation"])
    if s == "缝伞大裁计时":
        return "大裁" if (op == "大裁" or op is None) else "缝伞"
    if s == "包装 ": return "包装"
    if s == "顶带": return "打顶打带"
    if s == "打珠尾": return "珠尾"
    if s == "伞带.木耳花": return "伞带木耳花"
    return norm(s)

OPMAP = {"缝伞": 46, "大裁": 37, "合片": 42, "品检": 47, "小裁": 41, "珠尾": 45, "包装": 48}

with open(SRC, encoding="utf-8") as f:
    recs = json.load(f)["records"]

def money(v):
    s = str(v).strip()
    if s.startswith("+"): s = s[1:]
    return float(s)

def req(m, p, b=None, tok=None):
    data = json.dumps(b).encode("utf-8") if b is not None else None
    r = urllib.request.Request(BASE + p, data=data, method=m)
    r.add_header("Content-Type", "application/json")
    if tok: r.add_header("Authorization", "Bearer " + tok)
    try:
        with urllib.request.urlopen(r, context=CTX, timeout=60) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read().decode("utf-8", "replace"))

# login
j = req("POST", "/api/auth/login", {"Account": "admin", "Password": "Admin123"})[1]
d = j.get("data") or {}
tok = d.get("token")
if not tok and d.get("selectToken"):
    tok = (req("POST", "/api/auth/select-factory", {"SelectToken": d["selectToken"], "FactoryCode": "HY002"})[1].get("data") or {}).get("token")

results = []
for wt, opid in OPMAP.items():
    if wt == "包装":
        continue  # 已导
    rows = [r for r in recs if worktype(r) == wt and r["priceKind"] == "base" and r["unit"] == "打"
            and classify(norm(r["description"])) == "GO"]
    if not rows:
        results.append({"workType": wt, "operationId": opid, "rowCount": 0, "skipped": True})
        continue
    bases = []
    for i, r in enumerate(rows, 1):
        amt = money(r["rawPrice"])
        for pid, pcode in ((22, "HY-001"), (21, "HY-002")):
            bases.append({
                "id": f"{wt}-{i:02d}-{pcode}", "name": f"{norm(r['description'])}（{pcode}）",
                "productId": pid, "price": amt, "deductPrice": 0,
                "priceUnit": "打", "reportUnitId": 18, "unitsPerPriceUnit": 12,
                "allowed": {}, "included": [], "_src": r["sourceId"],
            })
    config = {"version": "", "groups": [], "bases": bases}
    # 读当前版本（防覆盖）
    _, before = req("GET", f"/api/operation-pricing/{opid}", tok=tok)
    cur = (before.get("data") or {}) if before else {}
    config["version"] = (cur.get("version") or "") if isinstance(cur, dict) else ""
    st, put = req("PUT", f"/api/operation-pricing/{opid}", config, tok=tok)
    _, after = req("GET", f"/api/operation-pricing/{opid}", tok=tok)
    ad = (after.get("data") or {}) if after else {}
    results.append({
        "workType": wt, "operationId": opid, "rowCount": len(rows), "baseCount": len(bases),
        "putCode": put.get("code"), "putMsg": put.get("msg"),
        "afterBaseCount": len(ad.get("bases") or []), "version": ad.get("version"),
        "srcIds": [r["sourceId"] for r in rows],
    })

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=2)

total_rows = sum(r["rowCount"] for r in results)
total_bases = sum(r["baseCount"] for r in results)
print("operations:", len(results), "totalRows:", total_rows, "totalBases:", total_bases)
for r in results:
    print(f"  {r['workType']}({r['operationId']}): rows={r['rowCount']} bases={r['baseCount']} put={r['putCode']}/{r['putMsg']} after={r['afterBaseCount']}")
