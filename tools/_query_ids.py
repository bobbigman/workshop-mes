import requests, json

BASE='http://localhost:8080'

r=requests.post(f'{BASE}/api/auth/login', json={'FactoryCode':'CJ001','Account':'admin','Password':'Admin123'}, timeout=10)
token=r.json()['data']['token']
hdr={'Authorization':f'Bearer {token}','Content-Type':'application/json'}

def get(path):
    r=requests.get(f'{BASE}{path}', headers=hdr, timeout=10)
    return r.json()['data']

print('=== Departments ===')
for d in get('/api/Department?pageSize=50')['list']:
    print(f"  id={d['id']} code={d['code']} name={d['name']}")

print('=== Operations ===')
for o in get('/api/Operation?pageSize=50')['list']:
    print(f"  id={o['id']} code={o['code']} name={o['name']}")

print('=== Products ===')
for p in get('/api/Product?pageSize=50')['list']:
    print(f"  id={p['id']} code={p['code']} name={p['name']}")

print('=== Work Orders ===')
for w in get('/api/WorkOrder?pageSize=10')['list']:
    print(f"  id={w['id']} orderNo={w['orderNo']} product={w['productName']} qty={w['qty']} status={w['status']}")

print('=== Defects ===')
for d in get('/api/DefectItem'):
    print(f"  id={d['id']} name={d['name']}")

print('=== Users ===')
for u in get('/api/User?pageSize=50')['list']:
    print(f"  id={u['id']} account={u['account']} name={u['name']} role={u['role']}")

print('=== Units ===')
for u in get('/api/Unit'):
    print(f"  id={u['id']} name={u['name']}")

print('=== PriceRules (existing) ===')
for r in get('/api/PriceRule?pageSize=50')['list']:
    print(f"  id={r['id']} op={r.get('operationName')} type={r['priceType']} price={r['unitPrice']} deduct={r.get('deductPrice')}")