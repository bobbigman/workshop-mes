"""扫描开发记录中的耗时/轮次原文，仅做候选定位，不改原文。"""
from pathlib import Path
import re, csv

DOCS = Path(r'D:\workspace\pkub\bee\docs')
pat = re.compile(r'(耗时|用了|花了|投入|实际|共计|约|估|预计|预计用|需要)[^。；\n]{0,24}(分钟|小时|人天|个工作日|工作日|天(?![天气]))')
pat2 = re.compile(r'(第\s?[0-9一二三四五六七八九十]+\s?(轮|版|次))|返工\s?[0-9一二三四五六七八九十]+\s?次|重写\s?[0-9一二三四五六七八九十]+\s?次')
rows = []
for p in sorted(DOCS.rglob('*')):
    if not p.is_file() or p.suffix.lower() not in ('.md', '.txt'):
        continue
    if '经验复盘' in p.parts:
        continue
    try:
        txt = p.read_text(encoding='utf-8-sig')
    except Exception:
        try:
            txt = p.read_text(encoding='gbk')
        except Exception:
            continue
    for i, line in enumerate(txt.splitlines(), 1):
        s = line.strip()
        if not s or len(s) > 400:
            continue
        m = pat.search(s)
        m2 = pat2.search(s)
        if m or m2:
            rows.append({
                '相对路径': str(p.relative_to(DOCS)),
                '行号': i,
                '类型': '耗时候选' if m else '轮次/版本候选',
                '原文': s[:220],
            })
out = Path(r'D:\workspace\pkub\bee\docs\经验复盘\复盘台账1112\豆包接续\_时间候选原始扫描.csv')
out.parent.mkdir(parents=True, exist_ok=True)
with out.open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0]))
    w.writeheader()
    w.writerows(rows)
print(len(rows), out)
