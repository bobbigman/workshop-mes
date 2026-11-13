"""从每份指令头部提取记录日期（仅作记录日证据，不当工时）。"""
from pathlib import Path
import re, csv

DOCS = Path(r'D:\workspace\pkub\bee\docs')
rows = []
for p in sorted((DOCS / '已做指令').glob('*.md')):
    try:
        txt = p.read_text(encoding='utf-8-sig')
    except Exception:
        txt = p.read_text(encoding='gbk', errors='ignore')
    head = '\n'.join(txt.splitlines()[:14])
    dates = re.findall(r'20\d{2}[-年]\d{1,2}[-月]\d{1,2}', head)
    rows.append({'文件名': p.name, '头部日期': ';'.join(dict.fromkeys(dates)), '头部原文': head.replace('\n', ' ')[:200]})

out = Path(r'D:\workspace\pkub\bee\docs\经验复盘\复盘台账1112\豆包接续\_头部日期证据.csv')
with out.open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0]))
    w.writeheader()
    w.writerows(rows)
print(len(rows), sum(1 for r in rows if r['头部日期']))
