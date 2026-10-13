# -*- coding: utf-8 -*-
"""上传 6 份工序作业指导书到工序知识库（refType=operation, refId=16~21）."""
import json, os, uuid, urllib.request

BASE = "http://localhost:8080"
ROOT = r"D:\workspace\pkub\裁切分析\MP42Txt\黑湖小工单_分析"

def api(path, data=None, token=None, method=None):
    req = urllib.request.Request(BASE + path, method=method or ("POST" if data is not None else "GET"))
    if token: req.add_header("Authorization", "Bearer " + token)
    if data is not None:
        req.add_header("Content-Type", "application/json")
        req.data = json.dumps(data).encode("utf-8")
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))

def upload_file(token, ref_type, ref_id, version, filepath):
    boundary = "----bb" + uuid.uuid4().hex
    fname = os.path.basename(filepath)
    with open(filepath, "rb") as f:
        file_bytes = f.read()
    def field(name, val):
        return ('--%s\r\nContent-Disposition: form-data; name="%s"\r\n\r\n%s\r\n'
                % (boundary, name, val)).encode("utf-8")
    head = ('--%s\r\nContent-Disposition: form-data; name="file"; filename="%s"\r\n'
            'Content-Type: application/octet-stream\r\n\r\n' % (boundary, fname)).encode("utf-8")
    tail = ("\r\n--%s--\r\n" % boundary).encode("utf-8")
    body = (field("refType", ref_type) + field("refId", str(ref_id))
            + field("version", version) + head + file_bytes + tail)
    req = urllib.request.Request(BASE + "/api/KnowledgeFile", data=body)
    req.add_header("Authorization", "Bearer " + token)
    req.add_header("Content-Type", "multipart/form-data; boundary=" + boundary)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))

lg = api("/api/auth/login", {"FactoryCode": "CJ001", "Account": "admin", "Password": "Admin123"})
if lg.get("code") != 0:
    print("[X] login failed:", lg); raise SystemExit(1)
token = lg["data"]["token"]
print("[V] admin ok")

JOBS = [
    ("下料", 16), ("冲压", 17), ("打磨", 18),
    ("质检", 19), ("组装", 20), ("包装", 21),
]
ok = 0
for (op, oid) in JOBS:
    path = os.path.join(ROOT, "%s工序作业指导书.docx" % op)
    r = upload_file(token, "operation", oid, "V1.0", path)
    if r.get("code") == 0:
        d = r["data"]
        print("[V] %s(id=%d) -> fileId=%d %s" % (op, oid, d["id"], d["fileName"])); ok += 1
    else:
        print("[X] %s failed: %s" % (op, r))

print("===== verify by order (产品打蛋器工单id=19 应聚合工序+产品文件) =====")
for (op, oid) in JOBS:
    lst = api("/api/KnowledgeFile?refType=operation&refId=%d" % oid, token=token)
    names = [f["fileName"] for f in lst["data"]]
    print("  %s(id=%d): %s" % (op, oid, names))
print("DONE ok=%d/6" % ok)
