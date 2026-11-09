# -*- coding: utf-8 -*-
"""复核已导入127条：区分真通用 vs 尺寸/骨数/布种特定。"""
import json, re, collections
SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
IMP = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-导入结果.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\_复核127.txt"

def norm(s): return (s or "").replace("\n", " ").replace("\r", " ").strip()

recs = json.load(open(SRC, encoding="utf-8"))["records"]
by = {r["sourceId"]: r for r in recs}

imported = set()
for r in json.load(open(IMP, encoding="utf-8")):
    imported.update(r["srcIds"])
for r in recs:
    if r["sheet"] == "包装 " and r["priceKind"] == "base" and r["unit"] == "打" and r["sourceId"] not in ("HY26-S04-R013", "HY26-S04-R014"):
        imported.add(r["sourceId"])

# 尺寸/骨数/布种/针数/层数/边数 关键词（真通用 = 不含任何规格限定）
SPECIFIC = ("寸", "公分", "PVC", "POE", "黑胶", "彩胶", "银胶", "钛银", "高密度", "记忆布", "网纱",
            "反光条", "热转印", "数码印", "帆布", "棉布", "毛巾布", "纳米", "30D", "素色", "针",
            "层", "个圆", "条边")
BONE = re.compile(r"\d[kK]")

def is_specific(desc):
    n = desc
    if any(k in n for k in SPECIFIC): return True
    if BONE.search(n): return True
    return False

keep, remove = [], []
for sid in sorted(imported):
    r = by[sid]
    desc = norm(r["description"])
    if is_specific(desc):
        remove.append((sid, r["sheet"], desc))
    else:
        keep.append((sid, r["sheet"], desc))

lines = [f"# 已导入 {len(imported)} 条复核", "",
         f"## 真通用（保留 HY-001/002）：{len(keep)} 条"]
for sid, sheet, desc in keep:
    lines.append(f"- {sid} [{sheet}] {desc}")
lines.append("")
lines.append(f"## 尺寸/骨数/布种/针数特定（建议从 HY-001/002 移除）：{len(remove)} 条")
for sid, sheet, desc in remove:
    lines.append(f"- {sid} [{sheet}] {desc}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))
print("imported:", len(imported), "keep:", len(keep), "remove:", len(remove))
