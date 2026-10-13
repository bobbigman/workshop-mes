# -*- coding: utf-8 -*-
"""用新 Key 对 /mcp 做 initialize + tools/list 握手验证。"""
import json, ssl, urllib.request, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
KEY="REDACTED_MCP_TOKEN"
BASE="https://mes.webok.net:11302/mcp"
def rpc(method, params=None, ident=1):
    body=json.dumps({"jsonrpc":"2.0","id":ident,"method":method,"params":params or {}}).encode("utf-8")
    req=urllib.request.Request(BASE,data=body,method="POST")
    req.add_header("Content-Type","application/json")
    req.add_header("Accept","application/json, text/event-stream")
    req.add_header("Authorization","Bearer "+KEY)
    try:
        with urllib.request.urlopen(req,context=ctx,timeout=30) as r:
            return r.status, json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8","replace")[:300]
out={}
st,res=rpc("initialize",{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"coze-probe","version":"1.0"}})
out["initialize"]=[st,res]
if st==200 and res.get("result"):
    sid=res["result"].get("serverInfo")
    pv=res["result"].get("protocolVersion")
    # 初始化后 Streamable HTTP 通常需再发 initialized 通知；直接试 tools/list
    st2,res2=rpc("tools/list",{"cursor":None},2)
    tools=res2.get("result",{}).get("tools",[]) if isinstance(res2,dict) else []
    out["tools_list"]=[st2,[(t.get("name"),len(t.get("inputSchema",{}).get("properties",{}))) for t in tools]]
    out["serverInfo"]=sid; out["protocolVersion"]=pv
p=r"D:\workspace\pkub\bee\docs\AI模拟数据\_mcp_verify.json"
with open(p,"w",encoding="utf-8") as f: json.dump(out,f,ensure_ascii=False,indent=1,default=str)
print("ok")
