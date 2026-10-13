# -*- coding: utf-8 -*-
"""把其它截图/工序01-13.png 上传到 HY001 各工序知识库（refType=operation）。结果写 _shots_result.json。"""
import json, ssl, urllib.request, os, sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
BASE="https://mes.webok.net:11302"
def raw(m,p,b=None,tok=None):
    data=json.dumps(b).encode("utf-8") if b is not None else None
    req=urllib.request.Request(BASE+p,data=data,method=m); req.add_header("Content-Type","application/json")
    if tok: req.add_header("Authorization","Bearer "+tok)
    with urllib.request.urlopen(req,context=ctx,timeout=30) as r: return json.loads(r.read().decode("utf-8"))
tok=raw("POST","/api/auth/login",{"FactoryCode":"HY001","Account":"admin","Password":"Admin123"})["data"]["token"]
SRC=r"D:\workspace\pkub\bee\docs\其它截图"
# 远程工序id -> 截图文件名（按顺序 01..13）
mapping=[
 (18,"工序01-裁剪 Cutting.png"),
 (19,"工序02-包边 Binding.png"),
 (20,"工序03-验布 Inspecting Fabric.png"),
 (21,"工序04-裁三角片 Cutting Triangular Panel.png"),
 (22,"工序05-丝印logo Slik Screen Printing Logo.png"),
 (23,"工序06-缝合 Stitching.png"),
 (24,"工序07-固定织带 Fixing Strap.png"),
 (25,"工序08-固定顶片 Fixing Top.png"),
 (26,"工序09-固定尖端 Fixing Tips.png"),
 (27,"工序10-车缝 Sewing.png"),
 (28,"工序11-质检 Inspecting.png"),
 (29,"工序12-固定手柄顶片 Fixing Handle Top.png"),
 (30,"工序13-包装 Packing.png"),
]
def upload(refType, refId, version, path, filename, ctype):
    boundary="----HYBOUND"+os.urandom(8).hex()
    parts=[]
    for name,val in (("refType",refType),("refId",str(refId)),("version",version)):
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{val}\r\n".encode("utf-8"))
    data=open(path,"rb").read()
    head=f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{filename}\"\r\nContent-Type: {ctype}\r\n\r\n".encode("utf-8")
    tail=f"\r\n--{boundary}--\r\n".encode("utf-8")
    body=b"".join(parts)+head+data+tail
    req=urllib.request.Request(BASE+"/api/knowledgefile",data=body,method="POST")
    req.add_header("Content-Type","multipart/form-data; boundary="+boundary)
    req.add_header("Authorization","Bearer "+tok)
    with urllib.request.urlopen(req,context=ctx,timeout=60) as r: return json.loads(r.read().decode("utf-8"))
out=[]
for oid,fn in mapping:
    p=os.path.join(SRC,fn)
    if not os.path.exists(p):
        out.append((oid,fn,"MISSING")); print("MISSING",oid,fn,flush=True); continue
    try:
        j=upload("operation",oid,"V1.0",p,fn,"image/png")
        out.append((oid,fn,j.get("code"),j.get("data",{}).get("id"),j.get("msg")))
        print("OK",oid,fn,"id=",j.get("data",{}).get("id"),flush=True)
    except Exception as e:
        out.append((oid,fn,"ERR",str(e))); print("ERR",oid,fn,e,flush=True)
rp=r"D:\workspace\pkub\bee\docs\AI模拟数据\PDF输出\_shots_result.json"
with open(rp,"w",encoding="utf-8") as f: json.dump(out,f,ensure_ascii=False,indent=1,default=str)
print("done",flush=True)
