# -*- coding: utf-8 -*-
"""生成 HY伞业V2 方案A 本地配置草稿 + 去向清单。不写服务器、不保存。"""
import json, collections

SRC = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-逐行整理清单.json"
OUT_JSON = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-配置草稿.json"
OUT_MD = r"D:\workspace\pkub\bee\docs\伞业客户加工单价表\HY伞业V2-配置草稿说明.md"

with open(SRC, encoding="utf-8") as f:
    data = json.load(f)
records = data["records"]

def norm(s):
    if s is None:
        return None
    return s.replace("\n", " ").replace("\r", " ").strip()

# ---- work-type assignment ----
def worktype(r):
    sheet = r["sheet"]
    op = norm(r["operation"])
    if sheet == "Sheet1":
        return None
    if sheet == "缝伞大裁计时":
        if op == "大裁":
            return "大裁"
        if op is None:
            return "大裁"  # 第二段 大裁/计时，行56-72；null归属该段
        return "缝伞"
    if sheet == "包装 ":
        return "包装"
    if sheet == "顶带":
        return "打顶打带"
    if sheet == "打珠尾":
        return "珠尾"
    if sheet == "伞带.木耳花":
        return "伞带木耳花"
    if sheet == "合片":
        return "合片"
    if sheet == "小裁":
        return "小裁"
    if sheet == "拉边":
        return "拉边"
    if sheet == "品检":
        return "品检"
    return sheet  # fallback

def money(v):
    # "+0.25" / 0.25 / "*2" / None
    if v is None:
        return None
    s = str(v).strip()
    if s.startswith("+"):
        s = s[1:]
    try:
        return float(s)
    except ValueError:
        return None

detail = [r for r in records if worktype(r) is not None]

ops = collections.OrderedDict()
for r in detail:
    wt = worktype(r)
    ops.setdefault(wt, {"bases": [], "options": [], "pending": [], "remarkExtra": []})

for r in detail:
    wt = worktype(r)
    kind = r["priceKind"]
    unit = r["unit"]
    desc = norm(r["description"])
    srcid = r["sourceId"]
    remark = norm(r["remark"])
    entry = {
        "sourceId": srcid,
        "sheet": r["sheet"], "row": r["row"],
        "description": desc, "unit": unit,
        "rawPrice": r["rawPrice"],
        "remark": remark,
    }
    if kind == "base" and unit == "打":
        entry["amount"] = money(r["rawPrice"])
        entry["含价"] = ("含" in (desc or "") and any(k in (desc or "") for k in ("丝印", "黑胶", "彩胶", "银胶", "钛银")))
        ops[wt]["bases"].append(entry)
        if remark:
            ops[wt]["remarkExtra"].append(entry)
    elif kind == "surcharge" and unit == "打":
        entry["extra"] = money(r["rawPrice"])
        ops[wt]["options"].append(entry)
        if remark:
            ops[wt]["remarkExtra"].append(entry)
    elif kind == "base" and unit == "码":
        entry["reason"] = "码单位，缺每把耗布/长度，不能换算成元/把"
        ops[wt]["pending"].append(entry)
    elif kind == "base" and unit == "小时":
        entry["reason"] = "计时，走独立计时规则（普通工价方式2），不并入方案A"
        ops[wt]["pending"].append(entry)
    elif kind == "multiplier":
        entry["reason"] = "*2倍率，方案A无公式引擎，需确定适用基础价后展开为有限成套价"
        ops[wt]["pending"].append(entry)
    elif kind == "missing":
        entry["reason"] = "空价，名称含*2不等于可推算，不启用"
        ops[wt]["pending"].append(entry)
    else:
        entry["reason"] = "未归类"
        ops[wt]["pending"].append(entry)

# ---- generate per-op draft with stable ids ----
def slug(wt):
    return wt

draft_ops = []
for wt in ops:
    o = ops[wt]
    bases = []
    for i, b in enumerate(o["bases"], 1):
        bases.append({
            "baseId": f"{wt}-B{i:03d}",
            "name": b["description"],
            "price": b["amount"],
            "deductPrice": 0,
            "priceUnit": "打",
            "reportUnit": "把",          # 仅适用于报工单位为把的产品 HY-001/HY-002
            "unitsPerPriceUnit": 12,
            "productId": "待绑定（需产品适用范围确认）",
            "sourceIds": [b["sourceId"]],
            "含价待核实": b.get("含价"),
            "remarkExtra": b["remark"],
        })
    options = []
    for i, opt in enumerate(o["options"], 1):
        options.append({
            "optionId": f"{wt}-O{i:03d}",
            "name": opt["description"],
            "extra": opt["extra"],
            "sourceIds": [opt["sourceId"]],
            "适用基础价": "待确认（每个附加价需明确叠加到哪些基础价）",
            "remarkExtra": opt["remark"],
        })
    draft_ops.append({
        "workType": wt,
        "sourceSheet": wt,
        "basesCount": len(o["bases"]),
        "optionsCount": len(o["options"]),
        "pendingCount": len(o["pending"]),
        "bases": bases,
        "optionCandidates": options,
        "pending": o["pending"],
        "remarkExtra": o["remarkExtra"],
        "proposedGroups": "草案：单选组拓扑（互斥/叠加关系）需业务确认后定稿，本清单仅列出候选附加价，不预设最终组结构",
    })

# ---- disposition map for all 330 ----
disp = []
cat_counts = collections.Counter()
for r in records:
    wt = worktype(r)
    if wt is None:
        cat = "reference-only"
        reason = "Sheet1仅参考，不新增第二套工价"
    else:
        kind = r["priceKind"]; unit = r["unit"]
        if kind == "base" and unit == "打":
            cat = "base-draft"; reason = "候选基础价（打→把=12，产品待绑定）"
        elif kind == "surcharge" and unit == "打":
            cat = "option-draft"; reason = "候选附加价（适用基础价待确认）"
        elif kind == "base" and unit == "码":
            cat = "pending-码"; reason = "码单位缺每把耗用量"
        elif kind == "base" and unit == "小时":
            cat = "pending-计时"; reason = "独立计时"
        elif kind == "multiplier":
            cat = "pending-倍率"; reason = "*2倍率待展开"
        elif kind == "missing":
            cat = "pending-空价"; reason = "空价不启用"
        else:
            cat = "pending-其它"; reason = "未归类"
    cat_counts[cat] += 1
    disp.append({
        "sourceId": r["sourceId"],
        "sheet": r["sheet"], "row": r["row"],
        "workType": wt,
        "kind": r["priceKind"], "unit": r["unit"],
        "disposition": cat,
        "reason": reason,
        "description": norm(r["description"]),
    })

out = {
    "date": "2026-10-23",
    "source": data["source"],
    "sha256": data["sha256"],
    "status": "本地配置草稿，未保存；正式写入等待HY002企业版及以上有效授权；不绕过门控",
    "scope": "仅处理可确定项；码/小时/空价/倍率/含价范围/产品适用范围/备注额外加价全部进入待核实",
    "workTypes": draft_ops,
    "dispositionCounts": dict(cat_counts),
    "dispositionMap": disp,
    "pendingSummary": {
        "码7": "大裁2+拉边5，缺每把耗布/长度；可新增码单位用于对应加工产品，不能套成品把数",
        "小时1": "缝伞大裁计时71行14元/小时，独立计时规则，不改产品单位",
        "倍率2": "顶带17(3公分打2次带)、顶带18(童伞加耳朵打顶)，需适用基础价后展开",
        "空价1": "伞带.木耳花5(鸭子头伞加衬纸*2)",
        "含价待核实": "描述含'含丝印/含黑胶/含彩胶/含银胶/含钛银'的成套价，included范围待确认",
        "产品适用范围": "旧4款产品只给品名单位，不能推定所有尺寸骨数布种都适用；打→把只用于HY-001/002",
        "备注额外加价": "remark字段含+加价（如偏心伞+0.4、34寸方形伞+0.08、27寸+0.2等），需确认叠加到哪些基础价",
    },
}

with open(OUT_JSON, "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=2)

# ---- markdown report ----
md = []
md.append("# HY伞业V2 方案A 本地配置草稿说明\n")
md.append(f"- 日期：2026-10-23；来源SHA256：`{data['sha256'][:16]}…`")
md.append("- 状态：**本地草稿，未保存**。正式写入等待 HY002 企业版及以上有效授权；不绕过门控、不改旧HY账套、不把未验证项标成通过。")
md.append("- 口径：只落可确定项；`码/小时/空价/倍率/含价范围/产品适用范围/备注额外加价` 全部进入待核实，不猜。")
md.append("")
md.append("## 去向统计（330条全量）")
md.append("| 去向 | 条数 |")
md.append("|---|---|")
for c, n in cat_counts.most_common():
    md.append(f"| {c} | {n} |")
md.append("")
md.append("## 各工序草稿规模")
md.append("| 工序(工作类型) | 基础价(打) | 附加价(打) | 待核实 |")
md.append("|---|---|---|---|")
for d in draft_ops:
    md.append(f"| {d['workType']} | {d['basesCount']} | {d['optionsCount']} | {d['pendingCount']} |")
md.append("")
md.append("## 待核实明细")
for d in draft_ops:
    if d["pending"]:
        md.append(f"\n### {d['workType']}")
        for p in d["pending"]:
            md.append(f"- `{p['sourceId']}` [{p['sheet']}/{p['row']}] 单位={p['unit']} 价={p['rawPrice']} — {p['description']}（{p['reason']}）")
md.append("")
md.append("## 备注额外加价（需确认叠加到哪些基础价，不能遗漏）")
for d in draft_ops:
    if d["remarkExtra"]:
        md.append(f"\n### {d['workType']}")
        for e in d["remarkExtra"]:
            md.append(f"- `{e['sourceId']}` — {e['description']}；备注：{e['remark']}")
md.append("")
md.append("## 未验证/限制")
md.append("- 本清单是本地草稿，未做任何服务器写入；243条基础价与23条附加价均为候选，未保存、未回查、未预览。")
md.append("- 方案A组/选项拓扑（互斥 vs 叠加）需业务确认后定稿；本清单只列候选，不预设最终结构。")
md.append("- `含价待核实` 的成套价需确认 included 范围（含哪个丝印/黑胶档），不能默认允许全部档位。")
md.append("- 基础价 `productId`、`reportUnitId` 待产品适用范围确认后绑定；打→把=12 只适用于报工单位为把的产品（HY-001/HY-002）。HY-003(套)/HY-004(件) 需补每打对应套/件换算依据，暂不配置、不猜1:1。")
md.append("- 工作类型 → 14个系统工序ID 的绑定需在写入时按目标账套实时查询（执行记录仅有旧→新ID映射，未含工序名称）。")

with open(OUT_MD, "w", encoding="utf-8") as f:
    f.write("\n".join(md))

print("bases", sum(len(d['bases']) for d in draft_ops),
      "options", sum(len(d['optionCandidates']) for d in draft_ops),
      "pending", sum(len(d['pending']) for d in draft_ops),
      "disp", dict(cat_counts))
