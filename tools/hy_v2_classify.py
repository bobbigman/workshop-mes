# -*- coding: utf-8 -*-
"""按伞型分类 243 条打基础价 → 目标产品；输出分类复核文件。"""
import json, collections

SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\_分类复核.txt"

with open(SRC, encoding="utf-8") as f:
    data = json.load(f)
records = data["records"]

def norm(s):
    return (s or "").replace("\n", " ").replace("\r", " ").strip()

def classify(name):
    n = name
    # 1 二次包装
    if any(k in n for k in ("二次包装", "二包")):
        return "二次包装→HY004(件待换算)"
    # 2 二折/儿童 → HY003（单位换算待定）
    if any(k in n for k in ("二折", "2折", "两折", "童伞", "儿童伞", "童")):
        return "二折/儿童→HY003(套待换算)"
    # 3 明确其他伞型 → 待核实（反三折须在 三折 之前）
    if any(k in n for k in ("直骨", "高尔夫", "风扇", "头盔", "海滩", "沙滩", "玩具",
                            "情侣", "偏心", "五折", "六折", "四折", "反三折", "灯光",
                            "莲花", "帽子", "耳朵", "夹具", "背包", "荷叶", "四方", "拐杖", "鸭子")):
        return "其他伞型→待核实"
    # 4 三折 → HY001/002
    if any(k in n for k in ("三折", "3折")):
        return "三折→HY001/002(把)"
    # 5 反向/反伞（无三折字样）→ 待核实
    if any(k in n for k in ("反向", "反伞")):
        return "其他伞型→待核实"
    # 6 无伞型关键词 → 通用（绑三折产品 HY001/002）
    return "通用→HY001/002(把)"

# 仅明细里的 打 基础价（可成为 base 的）
detail_bases = [r for r in records
                if r["disposition"] != "reference-only"
                and r["priceKind"] == "base" and r["unit"] == "打"]

cats = collections.Counter()
rows = []
for r in detail_bases:
    c = classify(norm(r["description"]))
    cats[c] += 1
    rows.append((c, r["sourceId"], norm(r["sheet"]), r["row"], norm(r["description"]), r["rawPrice"]))

lines = ["# 打基础价分类（明细，共 %d 条）" % len(detail_bases), ""]
for c, n in cats.most_common():
    lines.append(f"## {c}: {n}")
lines.append("")
lines.append("## 逐条明细")
for c, sid, sheet, row, desc, price in rows:
    lines.append(f"{c}\t{sid}\t{sheet}/{row}\t{price}\t{desc}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))
print("detail_bases:", len(detail_bases), "cats:", dict(cats))
