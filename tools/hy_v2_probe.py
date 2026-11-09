# -*- coding: utf-8 -*-
"""HY002 探针：登录 → 查授权档 → 拉取工序/产品/单位/路线 → 写 _probe.json。只读，不写业务。"""
import json, ssl, urllib.request, urllib.error, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

BASES = ["https://103.39.222.133:11302", "https://mes.webok.net:11302"]
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\_probe.json"

def req(base, method, path, body=None, tok=None, timeout=40):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    r = urllib.request.Request(base + path, data=data, method=method)
    r.add_header("Content-Type", "application/json")
    r.add_header("Accept", "application/json")
    if tok:
        r.add_header("Authorization", "Bearer " + tok)
    with urllib.request.urlopen(r, context=CTX, timeout=timeout) as resp:
        raw = resp.read().decode("utf-8", "replace")
        return resp.status, json.loads(raw)

result = {"date": "2026-10-23", "tried": BASES, "login": None, "license": None,
          "operations": None, "products": None, "units": None, "routings": None, "errors": []}

base_ok = None
tok = None

# 1. login
for base in BASES:
    try:
        st, j = req(base, "POST", "/api/auth/login", {"Account": "admin", "Password": "Admin123"})
        result["login"] = {"base": base, "status": st, "code": j.get("code"), "msg": j.get("msg"),
                           "needSelect": j.get("data", {}).get("needSelectFactory"),
                           "candidates": j.get("data", {}).get("candidates")}
        data = j.get("data") or {}
        if data.get("token"):
            tok = data["token"]
            base_ok = base
            result["login"]["directJwt"] = True
        elif data.get("needSelectFactory") and data.get("selectToken"):
            stk = data["selectToken"]
            st2, j2 = req(base, "POST", "/api/auth/select-factory",
                          {"SelectToken": stk, "FactoryCode": "HY002"})
            d2 = j2.get("data") or {}
            if d2.get("token"):
                tok = d2["token"]
                base_ok = base
                result["login"]["selectFactory"] = {"status": st2, "code": j2.get("code"),
                                                    "msg": j2.get("msg"), "factoryCode": d2.get("factoryCode"),
                                                    "factoryName": d2.get("factoryName"),
                                                    "licenseTier": d2.get("licenseTier")}
            else:
                result["errors"].append({"step": "select-factory", "base": base, "resp": j2})
        else:
            result["errors"].append({"step": "login-no-token", "base": base, "resp": j})
        break
    except Exception as e:
        result["errors"].append({"step": "login", "base": base, "error": repr(e)})

result["base_ok"] = base_ok
result["hasToken"] = bool(tok)

if tok:
    def get(path):
        st, j = req(base_ok, "GET", path, tok=tok)
        return st, j
    try:
        result["license"] = get("/api/license/status")[1]
    except Exception as e:
        result["errors"].append({"step": "license", "error": repr(e)})
    for name, path in [("operations", "/api/operation?pageSize=200"),
                       ("products", "/api/product?pageSize=200"),
                       ("units", "/api/unit"),
                       ("routings", "/api/routing?pageSize=200")]:
        try:
            st, j = get(path)
            result[name] = {"status": st, "code": j.get("code"), "msg": j.get("msg"), "data": j.get("data")}
        except Exception as e:
            result["errors"].append({"step": name, "error": repr(e)})

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(result, f, ensure_ascii=False, indent=2, default=str)

print("base_ok:", base_ok, "hasToken:", bool(tok),
      "tier:", (result["license"] or {}).get("data", {}).get("effectiveTier") if result["license"] else None)
print("written:", OUT)
