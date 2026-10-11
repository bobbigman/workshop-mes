# -*- coding: utf-8 -*-
"""为 HY001(factoryId=4) 建一把 MCP Key，明文写 _mcp_key_result.json。"""
import json, ssl, urllib.request, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
BASE="https://mes.webok.net:11302"
def raw(m,p,b=None,tok=None):
    data=json.dumps(b).encode("utf-8") if b is not None else None
    req=urllib.request.Request(BASE+p,data=data,method=m); req.add_header("Content-Type","application/json")
    if tok: req.add_header("Authorization","Bearer "+tok)
    with urllib.request.urlopen(req,context=ctx,timeout=30) as r: return json.loads(r.read().decode("utf-8"))
t=raw("POST","/api/auth/login",{"FactoryCode":"HY001","Account":"admin","Password":"Admin123"})["data"]["token"]
out={}
j=raw("POST","/api/McpKey",{"factoryId":4,"keyAlias":"coze-hy001","remark":"扣子对接","expireAt":None},t)
out["resp"]=j
# 顺便列一下这把厂的钥匙
try:
    out["list"]=raw("GET","/api/McpKey?factoryId=4",tok=t).get("data")
except Exception as e:
    out["list_err"]=str(e)[:150]
p=r"D:\workspace\pkub\bee\docs\AI模拟数据\_mcp_key_result.json"
with open(p,"w",encoding="utf-8") as f: json.dump(out,f,ensure_ascii=False,indent=1,default=str)
print("ok")
