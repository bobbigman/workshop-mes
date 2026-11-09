# -*- coding: utf-8 -*-
"""277 条明细最终去向清单（与导入结果一致）。"""
import json, re, collections
SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
OUT = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-去向清单.md"

with open(SRC, encoding="utf-8") as f:
    recs = json.load(f)["records"]

def norm(s): return (s or "").replace("\n", " ").replace("\r", " ").strip()

def worktype(r):
    s = r["sheet"]; op = norm(r["operation"])
    if s == "Sheet1": return None
    if s == "缝伞大裁计时": return "大裁" if (op == "大裁" or op is None) else "缝伞"
    if s == "包装 ": return "包装"
    if s == "顶带": return "打顶打带"
    if s == "打珠尾": return "珠尾"
    if s == "伞带.木耳花": return "伞带木耳花"
    return norm(s)

OTHER = ("直骨", "高尔夫", "风扇", "头盔", "海滩", "沙滩", "玩具", "情侣", "偏心", "五折", "六折",
         "四折", "反三折", "灯光", "莲花", "帽子", "耳朵", "夹具", "背包", "荷叶", "拐杖")
HY003 = ("二折", "2折", "两折", "童伞", "儿童伞", "童")
SPECIFIC = ("寸", "公分", "PVC", "POE", "黑胶", "彩胶", "银胶", "钛银", "高密度", "记忆布", "网纱",
            "反光条", "热转印", "数码印", "帆布", "棉布", "毛巾布", "纳米", "30D", "素色", "针", "层", "个圆", "条边")
BONE = re.compile(r"\d[kK]")

def is_specific(n):
    if "PVC袋" in n: return False
    if any(k in n for k in SPECIFIC): return True
    if BONE.search(n): return True
    return False

def classify(name):
    n = name
    if any(k in n for k in HY003): return "HY003"
    # 四方/鸭子：伞型 vs 盒/伞头样式
    if "四方伞" in n or "四方帆布" in n: return "OTHER"
    if "鸭子头伞" in n or "鸭子伞" in n: return "OTHER"
    if any(k in n for k in OTHER): return "OTHER"
    if any(k in n for k in ("三折", "3折")): return "GO"
    if any(k in n for k in ("反向", "反伞")): return "OTHER"
    return "GO"

detail = [r for r in recs if r["disposition"] != "reference-only"]
disp = collections.Counter()
rows = []
for r in detail:
    wt = worktype(r); kind = r["priceKind"]; unit = r["unit"]; desc = norm(r["description"])
    if kind == "surcharge": d = "附加价·待组结构"
    elif kind == "multiplier": d = "倍率·待展开"
    elif kind == "missing": d = "空价"
    elif unit == "码": d = "码·待每把耗用量"
    elif unit == "小时": d = "计时·独立规则"
    elif kind == "base" and unit == "打":
        c = classify(desc)
        if c == "HY003": d = "二折/儿童·HY003套换算待定"
        elif c == "OTHER": d = "其他伞型·待产品细分"
        elif wt == "打顶打带": d = "打顶/打带·拆分待定"
        elif wt == "伞带木耳花": d = "伞带木耳花·归属待定"
        elif is_specific(desc): d = "尺寸/骨/布/层/针特定·待产品规格"
        else: d = "真通用·已导入HY001/002"
    else: d = "未归类"
    disp[d] += 1
    rows.append((d, r["sourceId"], wt, kind, unit, desc))

lines = ["# HY伞业V2 明细277条去向清单（2026-10-24 修正后）", "",
         "## 去向统计", "| 去向 | 条数 |", "|---|---|"]
for d, n in disp.most_common():
    lines.append(f"| {d} | {n} |")
lines.append("")
lines.append("## 待处理明细")
for d in sorted(set(x[0] for x in rows)):
    if d == "真通用·已导入HY001/002":
        continue
    sub = [x for x in rows if x[0] == d]
    lines.append(f"\n### {d}（{len(sub)} 条）")
    for _, sid, wt, kind, unit, desc in sub:
        lines.append(f"- `{sid}` [{wt}] 单位={unit} 价型={kind} — {desc}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines))
print(dict(disp), "total", len(detail))
