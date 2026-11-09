import sys, json
d = json.load(sys.stdin)
for o in d['data']['list']:
    due = (o.get('dueDate') or '')[:10]
    print(f"{o['orderNo']}  {o['productName']:<14} qty={o['qty']:<6} done={o['doneQty']:<6} pct={o['progressPercent']:<4} due={due} state={o['dueState']}")
