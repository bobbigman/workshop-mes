# -*- coding: utf-8 -*-
"""昌盛雅畅演示账套 API 辅助（本任务一次性用）
用法：
  python csyc_api.py create --code CSYC001 --name "昌盛雅畅科技有限公司" --token "Bearer xxx"
  python csyc_api.py login  --code CSYC001 --acct admin --pwd Admin123 --base https://localhost:8080
令牌只走环境变量/命令行参数，不写入脚本常量。
"""
import argparse, json, ssl, sys, urllib.request, urllib.error

CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
BASE = "https://localhost:8080"

def req(token, method, path, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None
    r = urllib.request.Request(BASE + path, data=data, method=method,
        headers={"Authorization": token or "", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(r, context=CTX, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        return {"http_error": e.code, "body": e.read().decode("utf-8", "ignore")[:600]}
    except Exception as e:
        return {"exc": str(e)}

def main():
    global BASE
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE)
    sub = ap.add_subparsers(dest="cmd", required=True)

    pc = sub.add_parser("create"); pc.add_argument("--code"); pc.add_argument("--name"); pc.add_argument("--token")
    pl = sub.add_parser("login");  pl.add_argument("--code"); pl.add_argument("--acct", default="admin"); pl.add_argument("--pwd"); pl.add_argument("--token")
    ps = sub.add_parser("select"); ps.add_argument("--select-token"); ps.add_argument("--factory"); ps.add_argument("--token")
    plq = sub.add_parser("list");  plq.add_argument("--token")
    args = ap.parse_args()
    if args.base: BASE = args.base.rstrip("/")

    if args.cmd == "create":
        r = req(args.token, "POST", "/api/factories", {"FactoryCode": args.code, "FactoryName": args.name})
        print(json.dumps(r, ensure_ascii=False))
    elif args.cmd == "login":
        r = req(args.token, "POST", "/api/auth/login", {"FactoryCode": args.code, "Account": args.acct, "Password": args.pwd})
        print(json.dumps(r, ensure_ascii=False))
    elif args.cmd == "select":
        r = req(args.token, "POST", "/api/auth/select-factory", {"SelectToken": args.select_token, "FactoryCode": args.factory})
        print(json.dumps(r, ensure_ascii=False))
    elif args.cmd == "list":
        r = req(args.token, "GET", "/api/factories")
        print(json.dumps(r, ensure_ascii=False))

if __name__ == "__main__":
    main()
