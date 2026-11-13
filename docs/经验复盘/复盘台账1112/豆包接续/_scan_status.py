"""提取每份已做指令的状态/性质/日期行（正文原文），用于实施与验证状态判定。"""
from pathlib import Path
import re, csv

DOCS = Path(r'D:\workspace\pkub\bee\docs')
rows = []
for p in sorted((DOCS / '已做指令').glob('*.md')):
    try:
        txt = p.read_text(encoding='utf-8-sig')
    except Exception:
        txt = p.read_text(encoding='gbk', errors='ignore')
    lines = [l.strip() for l in txt.splitlines()[:18] if l.strip()]
    status = [l for l in lines if re.search(r'(状态|性质|交付|日期)', l)]
    rows.append({'文件名': p.name, '头部关键行': ' / '.join(status)[:260]})
out = Path(r'D:\workspace\pkub\bee\docs\经验复盘\复盘台账1112\豆包接续\_状态行证据.csv')
with out.open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0]))
    w.writeheader()
    w.writerows(rows)
print(len(rows))
