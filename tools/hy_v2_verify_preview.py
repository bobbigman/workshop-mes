# -*- coding: utf-8 -*-
"""代表性预览核对：合片 25寸8K（5元/打）→ 1200把 应=500元。"""
import json, ssl, urllib.request, urllib.error, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
BASE = "https://103.39.222.133:11302"

def req(m, p, b=None, tok=None):
    data = json.dumps(b).encode("utf-8") if b is not None else None
    r = urllib.request.Request(BASE + p, data=data, method=m)
    r.add_header("Content-Type", "application/json")
    if tok: r.add_header("Authorization", "Bearer " + tok)
    try:
        with urllib.request.urlopen(r, context=CTX, timeout=40) as resp:
            return json.loads(resp.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as e:
        return json.loads(e.read().decode("utf-8", "replace"))

j = req("POST", "/api/auth/login", {"Account": "admin", "Password": "Admin123"})
d = j.get("data") or {}
tok = d.get("token")
if not tok and d.get("selectToken"):
    tok = (req("POST", "/api/auth/select-factory", {"SelectToken": d["selectToken"], "FactoryCode": "HY002"}).get("data") or {}).get("token")

cfg = (req("GET", "/api/operation-pricing/42", tok=tok).get("data") or {})
bases = cfg.get("bases") or []
# 找 25寸8K HY-001
target = next((b for b in bases if b["name"].startswith("25寸8K") and "HY-001" in b["name"]), None)
print("合片 base count:", len(bases), "| 25寸8K base:", json.dumps(target, ensure_ascii=False) if target else "NOT FOUND")

if target:
    prev = req("POST", "/api/operation-pricing/42/preview?productId=22",
               {"baseId": target["id"], "values": {}}, tok=tok)
    pd = prev.get("data") or {}
    unit_price = pd.get("unitPrice"); upu = pd.get("unitsPerPriceUnit")
    amt_1200 = (unit_price / upu * 1200) if (unit_price is not None and upu) else None
    print("preview:", json.dumps({"code": prev.get("code"), "baseName": pd.get("baseName"),
                                  "basePrice": pd.get("basePrice"), "unitPrice": unit_price,
                                  "priceUnit": pd.get("priceUnit"), "unitsPerPriceUnit": upu,
                                  "1200把金额": amt_1200}, ensure_ascii=False))
