# -*- coding: utf-8 -*-
"""删除打蛋器产品知识库中旧登记记录 id=2,3（保留新上传 id=4,5）."""
import json, urllib.request, urllib.error

BASE = "http://localhost:8080"

def api(path, data=None, token=None, method=None):
    url = BASE + path
    req = urllib.request.Request(url, method=method or ("POST" if data is not None else "GET"))
    if token:
        req.add_header("Authorization", "Bearer " + token)
    if data is not None:
        req.add_header("Content-Type", "application/json")
        req.data = json.dumps(data).encode("utf-8")
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"http_error": e.code, "body": e.read().decode("utf-8", "ignore")}

lg = api("/api/auth/login", {"FactoryCode": "CJ001", "Account": "admin", "Password": "Admin123"})
if lg.get("code") != 0:
    print("[X] login failed:", lg); raise SystemExit(1)
token = lg["data"]["token"]
print("[V] admin ok")

for fid in (2, 3):
    r = api("/api/KnowledgeFile/%d" % fid, token=token, method="DELETE")
    print("DELETE id=%s -> %s" % (fid, "OK" if r.get("code") == 0 else r))

print("===== verify =====")
lst = api("/api/KnowledgeFile?refType=product&refId=11", token=token)
for f in lst["data"]:
    print("  - id=%s | %s | %s | ver=%s" % (f["id"], f["fileName"], f["fileType"], f["version"]))
