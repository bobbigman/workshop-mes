# -*- coding: utf-8 -*-
"""修正：4 个工序仅保留真通用价（移除尺寸/骨数/布种/层/针特定价），重写配置。"""
import json, re, ssl, urllib.request, urllib.error, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
BASE = "https://103.39.222.133:11302"
SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
IMP = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-导入结果.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-修正结果.json"

def norm(s): return (s or "").replace("\n", " ").replace("\r", " ").strip()
SPECIFIC = ("寸", "公分", "PVC", "POE", "黑胶", "彩胶", "银胶", "钛银", "高密度", "记忆布", "网纱",
            "反光条", "热转印", "数码印", "帆布", "棉布", "毛巾布", "纳米", "30D", "素色", "针",
            "层", "个圆", "条边")
BONE = re.compile(r"\d[kK]")
def is_specific(n):
    if "PVC袋" in n: return False
    if any(k in n for k in SPECIFIC): return True
    if BONE.search(n): return True
    return False

recs = json.load(open(SRC, encoding="utf-8"))["records"]
by = {r["sourceId"]: r for r in recs}

# 重构各工序已导入 srcIds
opmap = {"缝伞": 46, "大裁": 37, "合片": 42, "品检": 47, "小裁": 41, "珠尾": 45}
imported = {k: set() for k in opmap}
for r in json.load(open(IMP, encoding="utf-8")):
    imported[r["workType"]].update(r["srcIds"])
# 包装
imported["包装"] = set()
for r in recs:
    if r["sheet"] == "包装 " and r["priceKind"] == "base" and r["unit"] == "打" and r["sourceId"] not in ("HY26-S04-R013", "HY26-S04-R014"):
        imported["包装"].add(r["sourceId"])
OPID = {"缝伞": 46, "大裁": 37, "合片": 42, "品检": 47, "小裁": 41, "珠尾": 45, "包装": 48}

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

j = req("POST", "/api/auth/login", {"Account": "admin", "Password": "Admin123"})[1]
d = j.get("data") or {}
tok = d.get("token")
if not tok and d.get("selectToken"):
    tok = (req("POST", "/api/auth/select-factory", {"SelectToken": d["selectToken"], "FactoryCode": "HY002"})[1].get("data") or {}).get("token")

results = []
for wt, opid in OPID.items():
    rows = sorted(imported[wt])
    keep = [s for s in rows if not is_specific(norm(by[s]["description"]))]
    remove = [s for s in rows if s not in keep]
    entry = {"workType": wt, "operationId": opid, "importedRows": len(rows),
             "keepRows": len(keep), "removeRows": len(remove), "removed": remove}
    if len(keep) == 0:
        entry["action"] = "skip-留待产品细分重绑（无法空清，校验需≥1基础价）"
        results.append(entry)
        continue
    bases = []
    for i, sid in enumerate(keep, 1):
        r = by[sid]
        amt = money(r["rawPrice"])
        for pid, pcode in ((22, "HY-001"), (21, "HY-002")):
            bases.append({"id": f"{wt}-G{i:02d}-{pcode}", "name": f"{norm(r['description'])}（{pcode}）",
                          "productId": pid, "price": amt, "deductPrice": 0,
                          "priceUnit": "打", "reportUnitId": 18, "unitsPerPriceUnit": 12,
                          "allowed": {}, "included": [], "_src": sid})
    config = {"version": "", "groups": [], "bases": bases}
    _, before = req("GET", f"/api/operation-pricing/{opid}", tok=tok)
    cur = (before.get("data") or {}) if before else {}
    if isinstance(cur, dict) and cur.get("version"):
        config["version"] = cur["version"]
    st, put = req("PUT", f"/api/operation-pricing/{opid}", config, tok=tok)
    _, after = req("GET", f"/api/operation-pricing/{opid}", tok=tok)
    ad = (after.get("data") or {}) if after else {}
    entry.update({"putCode": put.get("code"), "putMsg": put.get("msg"),
                  "afterBaseCount": len(ad.get("bases") or []), "keepSrcIds": keep,
                  "action": "rewrote"})
    results.append(entry)

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=2)
for r in results:
    print(f"  {r['workType']}({r['operationId']}): import={r['importedRows']} keep={r['keepRows']} remove={r['removeRows']} -> {r['action']} put={r.get('putCode')} after={r.get('afterBaseCount')}")
