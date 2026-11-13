"""仅整理既有逐文件台账；不将文件行冒充去重开发事件。Python 3，无外部依赖。"""
from pathlib import Path
from collections import Counter
import csv, hashlib, re

ROOT = Path(__file__).resolve().parent
DOCS = ROOT.parents[1]

def save_csv(name, rows, fields):
    with (ROOT / name).open('w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)

rows = []
for batch in sorted(ROOT.glob('台账-*.md')):
    for line in batch.read_text(encoding='utf-8-sig').splitlines():
        if not re.match(r'^\d+\s*\|', line):
            continue
        parts = [p.strip() for p in line.split(' | ', 8)]
        if len(parts) != 9:
            raise ValueError(f'{batch.name}: 字段数不符: {line}')
        row = dict(zip(['序号','文件名','模块原称','修改描述','原分类','分类依据','耗时或轮次原文','关联链','备注'], parts))
        row['批次来源'] = batch.name
        row['序号'] = int(row['序号'])
        rows.append(row)
rows.sort(key=lambda r:r['序号'])
assert len({r['序号'] for r in rows}) == len(rows)
assert [r['序号'] for r in rows] == list(range(1,282))
queue = {}
for line in (ROOT/'00-待读队列.txt').read_text(encoding='utf-8-sig').splitlines():
    m = re.match(r'^(\d+)\s+(.+)$', line)
    if m: queue[int(m[1])] = m[2]
assert len(queue) == 281
for row in rows:
    assert queue[row['序号']] == row['文件名'], row
    p = DOCS/'已做指令'/row['文件名']
    row['源文件存在'] = '是' if p.exists() else '否'
    row['复核状态'] = '继承千问摘要，未逐条独立复核'
save_csv('01-千问逐文件台账.csv', rows, list(rows[0]))

inventory=[]
mapped={r['文件名'] for r in rows}
for p in sorted(DOCS.rglob('*')):
    if not p.is_file(): continue
    rel=p.relative_to(DOCS)
    method=rel.parts[0]=='经验复盘'
    covered=rel.parts[0]=='已做指令' and p.name in mapped
    inventory.append({'路径':str(rel),'目录':rel.parts[0] if len(rel.parts)>1 else '根目录',
      '扩展名':p.suffix.lower(),'字节数':p.stat().st_size,
      'SHA256':hashlib.sha256(p.read_bytes()).hexdigest(),
      '覆盖状态':'方法及本轮产物，排除业务统计' if method else ('有千问逐文件摘要，待独立复核' if covered else '未纳入千问台账')})
save_csv('00-资料快照.csv',inventory,list(inventory[0]))

times=[]
for row in rows:
    raw=row['耗时或轮次原文']
    if raw=='未知': continue
    kind=('计划估时' if any(s in raw for s in ['粗估','非实测','预估']) else
          '计划步骤时间，未执行计时' if '各节约' in raw else '轮次/版本/修正数，非耗时')
    times.append({'序号':row['序号'],'文件名':row['文件名'],'原文':raw,'类型':kind,'纳入实际工时合计':'否'})
save_csv('03-时间候选记录.csv',times,list(times[0]))

def table(counter, left):
    return '\n'.join([f'| {left} | 文件行数 |','|---|---:|']+[f'| {k} | {v} |' for k,v in counter.most_common()])

categories=Counter(r['原分类'] for r in rows)
modules=Counter(r['模块原称'] for r in rows)
dirs=Counter(i['目录'] for i in inventory if i['扩展名']=='.md' and i['目录']!='经验复盘')
report='''# 千问执行进度核对与接续统计（第一阶段）

日期：2026-11-12。统计单位：既有台账中的**文件行**，不是去重事件。

## 已核实做到哪里

- 留有15份分批台账，序号1—281连续、无重号；与待读队列281项逐项匹配。
- 281行均可定位源文件；“已读281/281”是千问的自述，本次不将其冒充Codex已全文复核。
- 已做的是逐篇摘要和初分；未完成事项归并、事件拆分、实现状态结构化、时间原文核对、原因统计及结论复核。
- 原“事项”列实际上大多为模块（例如工单管理），不是独立用户目标。原分类混合改动性质、原因和界面标签，不能直接计算返工率。
- 不能据281份台账断言V2方案完成多少百分比。只能说原队列文件摘要覆盖281/281。

## 接续形成的可复算数据

`01-千问逐文件台账.csv`保存全部原始行、批次来源和复核标记；`00-资料快照.csv`记录当前docs路径与哈希；`03-时间候选记录.csv`分开估时与轮次。运行`python 汇总台账.py`重建，本报告数字直接来自CSV同源行。

快照可能随新增产物变化；方法目录单列，不纳入业务数量。哈希用于后续检查变更，不代表已全文阅读。

## 原分类分布（继承分类，尚未校准）

'''+table(categories,'千问原分类')+'''

上述类别合计281，仅说明千问如何归类文件，不等于项目事件分布。“首次开发”也可能同时包含修复；“方向调整”也可能只是未实施方案撤销。

## 原模块分布（尚未统一同义词）

'''+table(modules,'原模块名')+'''

## 时间证据现状

'''+table(Counter(t['类型'] for t in times),'候选记录类型')+f'''

281行中，{len(times)}行的“耗时或轮次”非未知，{281-len(times)}行未知。这是混合字段填写情况，不能称实际耗时覆盖率。当前摘要没有可直接纳入的实际投入工时；也不能据此断言源文档没有工时。构建用时、版本号和第几轮均不得折成人工投入。

## 当前docs的Markdown目录分布

'''+table(dirs,'目录（方法目录排除）')+'''

281份已做摘要不覆盖未做、作废、处理中、归档及测试反馈；其中可能保存被推翻方案和关键纠偏证据。此表是资料盘点，不能作为业务事件分母。

## 已发现的质量问题与下一步

1. 原口径靠“新增、优化、修复”等特征词并以优先级单选，容易把首次能力归为界面纠正，把方案替换归为代码重做。按V2把性质、原因、体验标签、实施状态分列。
2. 批次点评含“白做”“有效率20%”“全库唯一”等超出逐文件摘要证据的结论，暂不采纳；尤其不能把最终保留文稿比例称开发有效率。
3. 明确重复候选：135与136的去手工发薪项、84与86的学操作拆除；方案/执行/验收可能同一事件，但须源文核对后合并。
4. 复核重点链：建单取价提示432→433→434→445→452、AI页签259→260→261三版→262、Pushplus实施与撤回、摄像头扫码排障。先核对原文，不将关联编号数等同修改次数。
5. 再补未做/作废/处理中与测试反馈的资料，将方案取消与已实现撤回分开。之后输出事件台账和正式统计，不要求业务数据库或现场账号。

**本阶段完成进度核对、文件级结构化和初步计数。尚未完成全项目事件去重统计与原因结论，不能用本表宣称“提示问题最多”或“AI导致多少返工”。**
'''
(ROOT/'05-接续进度与初步统计_Codex.md').write_text(report,encoding='utf-8')
print(f'校验通过：{len(rows)}行，队列一致，源文件存在{sum(r["源文件存在"]=="是" for r in rows)}；时间候选{len(times)}；快照{len(inventory)}文件')
print(dict(categories))
