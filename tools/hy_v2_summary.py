# -*- coding: utf-8 -*-
import json, collections

SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\_summary.txt"

with open(SRC, encoding="utf-8") as f:
    data = json.load(f)

records = data["records"]

def norm(s):
    if s is None:
        return None
    return s.replace("\n", " ").replace("\r", " ").strip()

lines = []
lines.append(f"total records: {len(records)}")
lines.append(f"candidateCount(declared): {data.get('candidateCount')}, detailCount: {data.get('detailCount')}")
lines.append(f"kindCounts(declared): {data.get('kindCounts')}")

ops = collections.OrderedDict()
units = collections.Counter()
disps = collections.Counter()
kinds = collections.Counter()

for r in records:
    op_raw = r["operation"]
    op = norm(op_raw) or "<null>"
    ops.setdefault(op, {"raw": op_raw, "sheets": collections.Counter(), "byKind": collections.Counter(), "byUnit": collections.Counter()})
    ops[op]["sheets"][r["sheet"]] += 1
    ops[op]["byKind"][r["priceKind"]] += 1
    units[r["unit"]] += 1
    disps[r["disposition"]] += 1
    kinds[r["priceKind"]] += 1

lines.append("\n# distinct operation (normalized) counts")
for op, d in ops.items():
    lines.append(f"  op={op!r}: total={sum(d['byKind'].values())} sheets={dict(d['sheets'])} kinds={dict(d['byKind'])}")

lines.append("\n# unit distribution")
for u, n in units.most_common():
    lines.append(f"  unit={u!r}: {n}")

lines.append("\n# disposition distribution")
for u, n in disps.most_common():
    lines.append(f"  disp={u!r}: {n}")

lines.append("\n# priceKind distribution")
for u, n in kinds.most_common():
    lines.append(f"  kind={u!r}: {n}")

# special records
lines.append("\n# surcharge (附加价) records")
for r in records:
    if r["priceKind"] == "surcharge":
        lines.append(f"  {r['sourceId']} [{norm(r['sheet'])}/{r['row']}] op={norm(r['operation'])!r} unit={r['unit']!r} raw={r['rawPrice']!r} desc={norm(r['description'])!r} remark={norm(r['remark'])!r}")

lines.append("\n# multiplier (*2) records")
for r in records:
    if r["priceKind"] == "multiplier":
        lines.append(f"  {r['sourceId']} [{norm(r['sheet'])}/{r['row']}] op={norm(r['operation'])!r} unit={r['unit']!r} raw={r['rawPrice']!r} desc={norm(r['description'])!r} remark={norm(r['remark'])!r}")

lines.append("\n# missing (空价) records")
for r in records:
    if r["priceKind"] == "missing":
        lines.append(f"  {r['sourceId']} [{norm(r['sheet'])}/{r['row']}] op={norm(r['operation'])!r} unit={r['unit']!r} raw={r['rawPrice']!r} desc={norm(r['description'])!r} remark={norm(r['remark'])!r}")

lines.append("\n# 码 / 小时 / 非打 units (detail records)")
for r in records:
    if r["disposition"] != "reference-only" and r["unit"] not in (None, "打"):
        lines.append(f"  {r['sourceId']} [{norm(r['sheet'])}/{r['row']}] op={norm(r['operation'])!r} unit={r['unit']!r} kind={r['priceKind']} raw={r['rawPrice']!r} desc={norm(r['description'])!r}")

lines.append("\n# unitMapping.status distinct values")
um_status = collections.Counter()
for r in records:
    um_status[r["unitMapping"]["status"]] += 1
for s, n in um_status.most_common():
    lines.append(f"  status={s!r}: {n}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))

print("OK", len(records))
