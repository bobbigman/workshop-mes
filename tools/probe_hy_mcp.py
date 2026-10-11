# -*- coding: utf-8 -*-
"""探明 HY001 线上 MCP 端点状态 + 建 Key 接口入参。写 _mcp_probe.json。"""
import json, ssl, urllib.request, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
BASE="https://mes.webok.net:11302"
def probe(path):
    req=urllib.request.Request(BASE+path, method="GET")
    try:
        with urllib.request.urlopen(req,context=ctx,timeout=20) as r:
            b=r.read().decode("utf-8","replace")
            return r.status, r.headers.get("Content-Type"), b[:200]
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get("Content-Type"), e.read().decode("utf-8","replace")[:200]
    except Exception as e:
        return "ERR", None, str(e)[:150]
out={}
out["mcp_get"]=probe("/mcp")
out["mcp_initialized"]=probe("/mcp?token=dummy")
# 用 admin 拿 token，试 POST /api/McpKey/factories 看可用厂选项（只读探测）
def raw(m,p,b=None,tok=None):
    data=json.dumps(b).encode("utf-8") if b is not None else None
    req=urllib.request.Request(BASE+p,data=data,method=m); req.add_header("Content-Type","application/json")
    if tok: req.add_header("Authorization","Bearer "+tok)
    with urllib.request.urlopen(req,context=ctx,timeout=20) as r: return json.loads(r.read().decode("utf-8"))
try:
    t=raw("POST","/api/auth/login",{"FactoryCode":"HY001","Account":"admin","Password":"Admin123"})["data"]["token"]
    out["login"]="ok"
    try:
        j=raw("GET","/api/McpKey/factories",tok=t); out["factories"]=j.get("data")
    except Exception as e:
        out["factories_err"]=str(e)[:150]
except Exception as e:
    out["login_err"]=str(e)[:150]
p=r"D:\workspace\pkub\bee\docs\AI模拟数据\_mcp_probe.json"
with open(p,"w",encoding="utf-8") as f: json.dump(out,f,ensure_ascii=False,indent=1,default=str)
print("ok")
