# -*- coding: utf-8 -*-
"""核验 HY001 各工序知识库：应各含 PDF 指导书 + 截图 2 份。"""
import json, ssl, urllib.request, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
BASE="https://mes.webok.net:11302"
def raw(m,p,b=None,tok=None):
    data=json.dumps(b).encode("utf-8") if b is not None else None
    req=urllib.request.Request(BASE+p,data=data,method=m); req.add_header("Content-Type","application/json")
    if tok: req.add_header("Authorization","Bearer "+tok)
    with urllib.request.urlopen(req,context=ctx,timeout=30) as r: return json.loads(r.read().decode("utf-8"))
tok=raw("POST","/api/auth/login",{"FactoryCode":"HY001","Account":"admin","Password":"Admin123"})["data"]["token"]
def get_raw(p, want_bytes=False):
    req=urllib.request.Request(BASE+p); req.add_header("Authorization","Bearer "+tok)
    try:
        with urllib.request.urlopen(req,context=ctx,timeout=30) as r:
            b=r.read(); return r.status, (b if want_bytes else b.decode("utf-8","replace")), r.headers.get("Content-Type")
    except urllib.error.HTTPError as e: return e.code, e.read().decode("utf-8","replace"), None
def lst(p):
    st,body,ct=get_raw(p)
    try: d=json.loads(body).get("data"); return (d.get("list",[]) if isinstance(d,dict) else d)
    except: return None
opnames={18:"裁布",19:"包边",20:"面料检验",21:"三角片裁切",22:"丝网印刷",23:"车缝",24:"固定绑带",25:"上伞帽",26:"装伞头珠",27:"缝制",28:"质检",29:"装伞柄",30:"包装"}
out={}
for oid,nm in opnames.items():
    kf=lst(f"/api/knowledgefile?refType=operation&refId={oid}") or []
    out[nm]=[(x.get("id"),x.get("fileName")) for x in kf]
# 抽查截图 raw 预览（id 32/37/44）
rawcheck=[]
for fid in (32,37,44):
    st,b,ct=get_raw(f"/api/knowledgefile/{fid}/raw", want_bytes=True)
    rawcheck.append((fid,st,ct,len(b)))
p=r"D:\workspace\pkub\bee\docs\AI模拟数据\PDF输出\_shots_verify.json"
with open(p,"w",encoding="utf-8") as f: json.dump({"perOp":out,"raw":rawcheck},f,ensure_ascii=False,indent=1,default=str)
print("ok")
