# -*- coding: utf-8 -*-
"""上传打蛋器作业指导书(docx)与图纸(pdf)到产品知识库(KnowledgeFile, refType=product, refId=11)."""
import json, mimetypes, os, uuid, urllib.request

BASE = "http://localhost:8080"

def api(path, data=None, token=None, raw_json=True):
    url = BASE + path
    req = urllib.request.Request(url)
    if token:
        req.add_header("Authorization", "Bearer " + token)
    if data is not None:
        body = json.dumps(data).encode("utf-8")
        req.add_header("Content-Type", "application/json")
        req.data = body
    with urllib.request.urlopen(req, timeout=30) as r:
        if raw_json:
            return json.loads(r.read().decode("utf-8"))
        return r.read().decode("utf-8")

def upload_file(token, ref_type, ref_id, version, filepath):
    boundary = "----bb" + uuid.uuid4().hex
    fname = os.path.basename(filepath)
    ctype = "application/octet-stream"
    with open(filepath, "rb") as f:
        file_bytes = f.read()
    def field(name, val):
        return ('--%s\r\nContent-Disposition: form-data; name="%s"\r\n\r\n%s\r\n'
                % (boundary, name, val)).encode("utf-8")
    head = ('--%s\r\nContent-Disposition: form-data; name="file"; filename="%s"\r\n'
            'Content-Type: %s\r\n\r\n' % (boundary, fname, ctype)).encode("utf-8")
    tail = ("\r\n--%s--\r\n" % boundary).encode("utf-8")
    body = (field("refType", ref_type) + field("refId", str(ref_id))
            + field("version", version) + head + file_bytes + tail)
    req = urllib.request.Request(BASE + "/api/KnowledgeFile", data=body)
    req.add_header("Authorization", "Bearer " + token)
    req.add_header("Content-Type", "multipart/form-data; boundary=" + boundary)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"http_error": e.code, "body": e.read().decode("utf-8", "ignore")}

print("===== Step 1: login admin =====")
lg = api("/api/auth/login", {"FactoryCode": "CJ001", "Account": "admin", "Password": "Admin123"})
if lg.get("code") != 0:
    print("[X] login failed:", lg); raise SystemExit(1)
token = lg["data"]["token"]
print("[V] admin token ok")

for (label, path, ver) in [
    ("作业指导书", r"D:\workspace\pkub\裁切分析\MP42Txt\黑湖小工单_分析\打蛋器_作业指导书.docx", "V1.0"),
    ("图纸", r"D:\workspace\pkub\裁切分析\MP42Txt\黑湖小工单_分析\打蛋器_图纸.pdf", "V1.0"),
]:
    print("===== Step 2: upload %s =====" % label)
    r = upload_file(token, "product", 11, ver, path)
    if r.get("code") == 0:
        d = r["data"]
        print("[V] uploaded id=%s type=%s file=%s (ref=%s/%s)" % (d["id"], d["fileType"], d["fileName"], d["refType"], d["refId"]))
    else:
        print("[X] upload failed:", r)

print("===== Step 3: verify list =====")
lst = api("/api/KnowledgeFile?refType=product&refId=11", token=token)
for f in lst["data"]:
    print("  - id=%s | %s | %s | ver=%s" % (f["id"], f["fileName"], f["fileType"], f["version"]))
print("DONE")
