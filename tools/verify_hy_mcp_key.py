# -*- coding: utf-8 -*-
"""用 Key 对 /mcp 做 initialize + tools/list 握手验证。

令牌从环境变量 HY_MCP_KEY 或参数 --key 读取，不写进本文件。
"""
import argparse, json, os, ssl, urllib.request, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

def parse_args():
    p = argparse.ArgumentParser(description="用 Key 对 /mcp 做 initialize + tools/list 握手验证。")
    p.add_argument("--key", default=os.environ.get("HY_MCP_KEY", ""), help="MCP Bearer 令牌，也可设环境变量 HY_MCP_KEY")
    p.add_argument("--base", default=os.environ.get("HY_MCP_BASE", "https://mes.webok.net:11302/mcp"), help="MCP 地址，也可设环境变量 HY_MCP_BASE")
    args = p.parse_args()
    if not str(args.key).strip():
        print("缺少令牌：请设置环境变量 HY_MCP_KEY，或传入 --key", file=sys.stderr)
        sys.exit(2)
    return args

args = parse_args()
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
KEY = args.key
BASE = args.base
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
