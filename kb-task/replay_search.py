# -*- coding: utf-8 -*-
"""忠实复刻 backend/Services/SupportDocumentService.cs 的检索打分，
对 SupportDocs/ 目录跑基准表 40 条问法，输出每条 top5 命中（文件#章节:得分）。"""
import json
import re
import os
import unicodedata

ROOT = r"D:\workspace\pkub\裁切分析\MP42Txt\黑湖小工单_分析"
SD = os.path.join(ROOT, "SupportDocs")

# ---- SplitFrontMatter ----
def split_front_matter(text):
    meta = {}
    if not text.startswith("---"):
        return meta, text
    lines = text.split("\n")
    end = -1
    for i in range(1, len(lines)):
        if lines[i].strip().rstrip("\r") in ("---", "..."):
            end = i
            break
    if end < 0:
        return meta, text
    for i in range(1, end):
        t = lines[i].strip().rstrip("\r")
        ci = t.find(":")
        if ci > 0:
            meta[t[:ci].strip().lower()] = t[ci + 1:].strip()
    content = "\n".join(lines[end + 1:]).lstrip("\r\n")
    return meta, content

# ---- SplitToUnits (.md) ----
def split_units(content):
    units, cur, sb, started = [], None, [], False
    for raw in content.split("\n"):
        line = raw.rstrip("\r")
        t = line.strip()
        if t.startswith("#"):
            if started:
                units.append((cur, "\n".join(sb)))
            cur = t.lstrip("#").strip()
            sb, started = [], True
        else:
            sb.append(line)
    if started or sb:
        units.append((cur, "\n".join(sb)))
    return units

# ---- 检索术语提取（ExtractTerms）----
KEYPHRASES = [
    "一键补报", "批量补报", "补报", "删不掉", "删除", "报工权限", "权限", "复核", "退回",
    "下工单", "下单", "创建工单", "打印", "二维码", "流转卡", "账号", "用户", "完成数",
    "生产报表", "入账", "报表", "交期", "状态", "工序", "工艺路线", "产品", "工单", "单位",
    "不良品项", "部门", "登录", "密码", "看板", "扫码", "报工", "PDA"]
SYNONYMS = {
    "补报": ["补报", "批量补报", "一键补报"],
    "删不掉": ["删除", "删"],
    "删除": ["删除", "删"],
    "权限": ["权限", "报工权限"],
    "复核": ["复核", "审核", "通过", "退回"],
    "退回": ["退回", "复核"],
    "下工单": ["下单", "创建工单", "工单"],
    "下单": ["下单", "创建工单", "工单"],
    "创建工单": ["创建工单", "下单", "工单"],
    "打印": ["打印", "二维码", "流转卡"],
    "账号": ["账号", "用户"],
    "用户": ["用户", "账号"],
    "完成数": ["完成", "完成数", "进度"],
    "报表": ["报表", "生产报表", "入账"],
    "入账": ["入账", "报表", "生产报表"],
    "工单": ["工单", "下单", "创建工单"],
    "扫码": ["扫码", "报工", "PDA", "二维码"],
    "报工": ["报工", "扫码", "提交报工"],
    "PDA": ["PDA", "扫码", "扫码头"]}
STOP = {"怎么", "如何", "什么", "为什", "一个", "一下", "在哪", "哪里", "哪些", "这个", "那个", "是不是"}

def is_cjk(c):
    return 0x4E00 <= ord(c) <= 0x9FFF

def extract_terms(q):
    q = q.strip()
    terms = set()
    low = q.casefold()
    for kp in KEYPHRASES:
        if kp.casefold() in low:
            terms.add(kp)
    for m in re.finditer(r"[A-Za-z0-9]+", q):
        terms.add(m.group(0))
    for i in range(len(q) - 1):
        if q[i] == " " or q[i + 1] == " ":
            continue
        bg = q[i:i + 2]
        if is_cjk(bg[0]) and is_cjk(bg[1]) and bg not in STOP:
            terms.add(bg)
    if len(q) <= 32:
        terms.add(q)
    expanded = set()
    for t in terms:
        expanded.add(t)
        for s in SYNONYMS.get(t, []):
            expanded.add(s)
        for s in SYNONYMS.get(t.casefold(), []):
            expanded.add(s)
    out = [t for t in expanded if t]
    return out[:64] if len(out) > 64 else out

def count_occ(hay, needle):
    if not hay or not needle:
        return 0
    h, n = hay.casefold(), needle.casefold()
    c, i = 0, 0
    while True:
        j = h.find(n, i)
        if j < 0:
            return c
        c += 1
        i = j + len(n)

# ---- 加载索引 ----
chapters = []
for fn in sorted(os.listdir(SD)):
    if not fn.endswith(".md") or fn == "README.md":
        continue
    meta, content = split_front_matter(open(os.path.join(SD, fn), encoding="utf-8").read())
    title = meta.get("title") or os.path.splitext(fn)[0]
    for i, (chap, body) in enumerate(split_units(content)):
        if not body.strip():
            continue
        # 复刻分块上限 1500（当前无长章节，不触发）
        chapters.append({"file": fn, "title": title, "chapter": chap, "content": body})

def search(query):
    terms = extract_terms(query)
    scored = []
    for ch in chapters:
        tt = (ch["title"] + " " + (ch["chapter"] or "")).strip()
        sc = sum(count_occ(tt, t) * 3 + count_occ(ch["content"], t) for t in terms)
        if sc > 0:
            scored.append((sc, ch))
    scored.sort(key=lambda x: (-x[0], x[1]["title"]))
    return scored[:5]

if __name__ == "__main__":
    rows = json.load(open(os.path.join(ROOT, "kb-task", "queries.json"), encoding="utf-8"))
    for r in rows:
        res = search(r["q"])
        top = "; ".join(f'{c["file"]}#{c["chapter"]}:{s}' for s, c in res)
        print(f'{r["no"]}\t{r["state"]}\t{r["q"]}\t=> {top}')
