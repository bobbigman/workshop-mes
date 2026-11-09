# -*- coding: utf-8 -*-
"""仅重查 HY002 授权档：登录→选厂→license/status。"""
import json, ssl, urllib.request, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
BASE="https://103.39.222.133:11302"
def req(m,p,b=None,tok=None):
    data=json.dumps(b).encode("utf-8") if b is not None else None
    r=urllib.request.Request(BASE+p,data=data,method=m); r.add_header("Content-Type","application/json")
    if tok: r.add_header("Authorization","Bearer "+tok)
    with urllib.request.urlopen(r,context=ctx,timeout=40) as resp:
        return json.loads(resp.read().decode("utf-8","replace"))
j=req("POST","/api/auth/login",{"Account":"admin","Password":"Admin123"})
d=j.get("data") or {}
stk=d.get("selectToken")
j2=req("POST","/api/auth/select-factory",{"SelectToken":stk,"FactoryCode":"HY002"}) if stk else j
tok=(j2.get("data") or {}).get("token") or d.get("token")
lic=req("GET","/api/license/status",tok=tok)
print(json.dumps({"selectTier":(j2.get("data") or {}).get("licenseTier"), "license":lic}, ensure_ascii=False))
