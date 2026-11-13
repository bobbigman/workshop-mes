# -*- coding: utf-8 -*-
"""从事件定义 + 文件级证据生成 02-事项事件台账.csv 与 04-分类统计.md。
口径：V2（文件≠开发；事项=用户目标；事件=一次可辨认的决定/实现/修复/撤回）。
用法：python 汇总事件台账.py
"""
import csv
from pathlib import Path
from collections import Counter, defaultdict

BASE = Path(__file__).resolve().parent
import importlib.util
spec_ev = []
for mod in ('_events_1', '_events_2', '_events_3'):
    s = importlib.util.spec_from_file_location(mod, BASE / f'{mod}.py')
    m = importlib.util.module_from_spec(s); s.loader.exec_module(m)
    spec_ev += [v for k, v in vars(m).items() if k.startswith('EV')]

EVENTS = [e for grp in spec_ev for e in grp]

# 事项归并（事件级事项名 → 用户目标级事项）
s = importlib.util.spec_from_file_location('_xmap', BASE / '_xmap.py')
mx = importlib.util.module_from_spec(s); s.loader.exec_module(mx)
EVENTS = [(e[0], mx.XMAP.get(e[1], e[1]) ) + tuple(e[2:]) for e in EVENTS]

# 文件级证据
ev_file = {int(r['序号']): r for r in csv.DictReader((BASE / '01-文件级证据.csv').open(encoding='utf-8-sig'))}

BEST_VERIFY = ['已验证（原文称通过，范围见原文）', '部分验证（构建/单测通过，现场未验）', '未记录验证范围', '未验证/不适用']

IMPL_FIX_MAP = {'仅方案变化': '仅方案文档（未实施）'}

def derive_impl(rows):
    s = {r['实施判定'] for r in rows}
    if '已撤回' in s: return '已撤回（回滚）'
    if '已实现' in s: return '已实现'
    if '实施中/未完成' in s: return '实施中/未完成'
    if s <= {'仅方案变化', '仅方案（未授权开工）', '仅方案/被取代', '未实施/仅方案'} and s: return '仅方案文档（未实施）'
    if s == {'未标注'}: return '未标注（文档/清单类）'
    return '部分实现/混合'

def derive_verify(rows):
    got = {r['验证判定'] for r in rows}
    if len(got) > 1: return '来源验证状态不一，须分项核对'
    for lv in BEST_VERIFY:
        if lv in got: return lv
    return '未验证/不适用'

def dates(rows):
    ds = sorted(r['记录日'] for r in rows if r['记录日'])
    if not ds: return ''
    return ds[0] if ds[0] == ds[-1] else f'{ds[0]}～{ds[-1]}'

# 事项编号
order, xno = [], {}
for e in EVENTS:
    if e[1] not in xno:
        order.append(e[1]); xno[e[1]] = f'X{len(order):02d}'

rows_out = []
# V2 性质口径：首次开发/正常完善/缺陷修复/方向性重做/仅方案文档/环境交付/未知
# 旧事件里的『提示交互纠正』『需求理解偏差』不是性质（前者是提示体验标签、后者是原因），在此按 V2 归位。
NATURE_MAP = {'提示交互纠正': '未知', '仅方案变化': '仅方案文档'}
SPECIFIC = {
    '取消统一改作废': '缺陷修复', '撤回两跳提示': '缺陷修复', 'H5 工单页默认落执行中': '缺陷修复',
    '派工通知只 @': '缺陷修复', '跨厂找人': '缺陷修复', '新增报工弹窗帮助': '缺陷修复',
    '批次自定义字段改文本': '缺陷修复', 'MCP 口径与知识库检索加固': '缺陷修复', '纠正 FAQ': '缺陷修复',
    '把页签从工程师改为电脑部门': '方向性重做', '按业财打通三件事': '方向性重做', '按远景卖点版': '方向性重做',
    '工作台分层': '方向性重做', '彩色八宫格': '方向性重做', '改为原生 MCP 中间件': '方向性重做',
    'MCP 工具按 role': '首次开发', '班组长角色与复核双轨': '首次开发', 'PC 工单状态 tab': '正常完善',
    'AI 客服回答改价值话术': '正常完善', '物理删除 fid=4': '正常完善',
    '太阳神账套灌产品工单': '环境交付', '新服务器无 DB 直连': '环境交付', '新建昌盛雅畅': '环境交付',
    '按人均 7000—8000': '环境交付', '清理 HY 演示旧工单': '环境交付', 'MP-UN-002': '环境交付',
    '厨具五金厂演示账套': '环境交付', '远程测试账套上传': '环境交付', '打包脚本补两条': '环境交付',
    '做出可复用的增量部署包': '环境交付', '清理已跟踪的 MCP 令牌': '环境交付', '修复 start 端口占用': '环境交付',
}
for e in EVENTS:
    (nums, xiang, title, mod, tag, nature, cause, evid, hint, impl_fix, ver_fix, link, quote) = e
    n2 = NATURE_MAP.get(nature, nature)
    for k, v in SPECIFIC.items():
        if k in title:
            n2 = v
            break
    real = [n for n in nums if n > 0]
    fr = [ev_file[n] for n in real if n in ev_file]
    impl = derive_impl(fr) if not impl_fix else IMPL_FIX_MAP.get(impl_fix, impl_fix)
    if impl in ('未实施/仅方案', '仅方案文档'):
        impl = '仅方案文档（未实施）'
    verify = ver_fix or derive_verify(fr)
    grade = 'A 全文阅读' if any(r['复核等级'].startswith('A') for r in fr) else ('B 状态行+分类依据原句' if any(r['复核等级'].startswith('B') for r in fr) else 'C 摘要')
    nature_candidate = n2
    review_status = '候选分类，尚未独立全文复核'
    if nature == '提示交互纠正':
        n2 = '未知'
        review_status = '提示体验已标记；正常完善或纠错待核实'
    if any(n < 0 for n in nums):
        grade = 'C 外部补充线索，来源全文未核实'
        review_status = '补充线索，不纳入已做主样本'
    rows_out.append({
        '事项ID': xno[xiang], '事项': xiang, '事件标题': title,
        '主模块': mod, '辅助标签': tag, '性质': n2, '原因': cause,
        '实施状态': impl, '验证状态': verify, '证据等级': grade,
        '原性质候选': nature_candidate, '分类复核状态': review_status,
        '原因依据类型': evid, '样本范围': '已做主样本' if real else '外部补充线索',
        '稳定定位键': ','.join(map(str, nums)) + ':' + title,
        '提示体验标签': hint, '记录日期': dates(fr),
        '关联/链': link, '证据摘录（来源章节或原句）': quote,
        '覆盖序号': '、'.join(str(n) for n in nums), '覆盖文件数': len(real),
    })

# 同一事项内按记录日期排序，再统一发事件ID
rows_out.sort(key=lambda r: (r['事项ID'], r['记录日期'] or '9999'))
for i, r in enumerate(rows_out, 1):
    r['事件ID'] = f'EVT{i:03d}'
rows_out = [{k: r[k] for k in ['事件ID'] + [c for c in r if c != '事件ID']} for r in rows_out]

cols = list(rows_out[0])
with (BASE / '02-事项事件台账.csv').open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.DictWriter(f, fieldnames=cols); w.writeheader(); w.writerows(rows_out)

# 覆盖校验
covered = set()
for r in rows_out:
    for n in r['覆盖序号'].replace('-', '').split('、'):
        if n.strip().isdigit() and int(n) > 0: covered.add(int(n))
miss = sorted(set(range(1, 282)) - covered)
print(f'事件 {len(rows_out)} 条，事项 {len(order)} 个；未覆盖序号 {len(miss)}：{miss}')

# —— 分类统计 ——
def table(title, counter, total, note=''):
    lines = [f'### {title}', '', '| 类别 | 事件数 | 占事件总数 |', '|---|---:|---:|']
    for k, v in counter.most_common():
        lines.append(f'| {k} | {v} | {v/total*100:.1f}% |')
    lines += ['', note, '']
    return '\n'.join(lines)

N = len(rows_out)
X = len(order)
nature = Counter(r['性质'] for r in rows_out)
cause = Counter(r['原因'] for r in rows_out)
impl = Counter(r['实施状态'] for r in rows_out)
ver = Counter(r['验证状态'] for r in rows_out)
mod = Counter(r['主模块'] for r in rows_out)
grade = Counter(r['证据等级'] for r in rows_out)
hints = Counter()
for r in rows_out:
    if r['提示体验标签'] and r['提示体验标签'] != '无':
        for h in r['提示体验标签'].split('、'):
            hints[h] += 1
hint_events = sum(1 for r in rows_out if r['提示体验标签'] not in ('', '无'))

# 事项级：是否有≥2 个事件（多轮链）
by_x = defaultdict(list)
for r in rows_out: by_x[r['事项ID']].append(r)
multi = [k for k, v in by_x.items() if len(v) >= 2]

md = [f'''# 04 分类统计（由 02-事项事件台账.csv 直接生成）

生成脚本：`汇总事件台账.py`；输入：`02-事项事件台账.csv`（事件定义见 `_events_1/2/3.py`，文件级证据见 `01-文件级证据.csv`）。

## 口径与分母（先看这一段）

- 统计单元是**事件**，不是文件。分母 N = {N}（已辨认事件总数）。
- 事项 {X} 个；281 份已做指令中，覆盖到台账的 {len(covered)} 个序号（未覆盖 {len(miss)} 个：{('、'.join(map(str, miss)) if miss else '无')}）。
  同一序号可出现在多个事件里（如 48 同时属批量重算与栏位改造），这是刻意标注的交叉，不重复计入文件总数。
- 性质、原因、提示体验、实施、验证**分列统计**，不用旧口径的六类单选优先级。
- 原因写『未知』的不做推测；『—』表示该事件本身没有可归因的偏差（正常新建或正常完善）。
- 证据等级：A={grade.get('A 全文阅读',0)} 条事件（其文件已全文阅读）、B={grade.get('B 状态行+分类依据原句',0)} 条（读状态行与原分类依据句）、C={grade.get('C 摘要',0)} 条。**仅 {grade.get('A 全文阅读',0)} 条事件达到全文级复核**，其余未逐句复核，结论按 B 级证据强度使用。

## 一、事件性质分布（N={N}）

''']
md.append(table('性质', nature, N, '说明：仅方案文档=有方案/清单但未开工或已取消，不算代码返工；方向性重做=既有实现被整体推翻或替换（含回滚、连根拔、多次改版后定稿）；环境交付=数据/打包/部署类工作，不计产品开发。'))
md.append(table('原因', cause, N, '说明：原因与责任分开。『需求或产品决定变化』含客户/通道/卖点等外部条件改变；『需求理解偏差』指方案或实现偏离已定口径；『实现缺陷』指代码与既定契约不一致。'))
md.append(table('实施状态', impl, N, '说明：由文件状态行判定；未标注的多为文档/清单/测试类。'))
md.append(table('验证状态', ver, N, '说明：部分验证=构建或单测通过、现场/真机/点选未验；未记录验证范围=原文未见验证结论。'))

md.append(f'''### 提示体验专项（不预设它最多）

出现提示体验标签的事件 {hint_events} 条，占全部事件 {hint_events/N*100:.1f}%。标签可多选，故下列频次之和大于事件数。

| 提示体验标签 | 事件频次 |
|---|---:|''')
for k, v in hints.most_common():
    md.append(f'| {k} | {v} |')
md.append('')
md.append('''读法：这是**标签频次**，不是"问题占比"。多数标签事件的性质是提示交互纠正（改文案/改层次/改位置），
不等于代码返工；真实阻断（报工不能提交、复核页打不开、扫码进不去）另见 05 重点链。''')
md.append('')

md.append(table('主模块（事件归属，非文件数）', mod, N, '说明：模块名只用于归类，不作事项名；事项名见 02 台账『事项』列。'))

md.append(f'''### 多轮事项（同一事项 ≥2 个事件）

共 {len(multi)} 个事项存在多事件（同一目标下的方案→实施→推翻/回滚/再定稿）：

| 事项ID | 事项 | 事件数 | 事件标题 |
|---|---|---:|---|
''')
for k in multi:
    v = by_x[k]
    md.append(f'| {k} | {v[0]["事事"] if False else v[0]["事项"]} | {len(v)} | ' + '；'.join(x['事件标题'] for x in v) + ' |')
md.append('')
md.append('''说明：多事件不等于返工。方案稿→实施→验收属正常推进；只有『既有实现被推翻/回滚/连根拔』才计入方向性重做。''')

# 补充计数
multi_file = sum(1 for r in rows_out if int(r['覆盖文件数']) > 1)
blocking = [r for r in rows_out if r['性质'] == '缺陷修复' and any(
    k in r['证据摘录（来源章节或原句）'] for k in ('必现', '打不开', '无法', '进不去', '闪退', '冲突', '漏洞', '串数据', '崩溃', '空白'))]
md.append(f'''
## 二、补充计数（同样以事件为分母）

| 项目 | 数值 | 说明 |
|---|---:|---|
| 已做指令文件数 | 281 | 千问逐文件台账原始行数 |
| 已辨认事件数 N | {N} | 本次台账 |
| 事项数 | {X} | 用户目标级 |
| 平均事件/事项 | {N/X:.1f} | — |
| 覆盖 >1 份文件的事件 | {multi_file} | 同一次修改的方案/执行/验收合并 |
| 存在 ≥2 事件的 事项 | {len(multi)} | 含方案→实施→回滚/再定稿的链 |
| 缺陷修复事件 | {nature.get('缺陷修复',0)} | 其中证据含『必现/打不开/无法/闪退/冲突/漏洞』等阻断症状的 {len(blocking)} 条 |
| 方向性重做事件 | {nature.get('方向性重做',0)} | 既有实现被推翻/回滚/连根拔/多次改版后定稿 |
| 仅方案文档（未开工或取消） | {nature.get('仅方案文档',0)} | 不计代码返工 |
| 环境交付（数据/打包/部署） | {nature.get('环境交付',0)} | 不计产品开发事件 |
| 有用户反馈实测耗时的事件 | 2 | 129 企微推送≈38 分钟、MP-UN-002≈6 分钟 |
| 有计划估时记录的事件 | 6 | 人天/小时，非实测 |
| 有修改轮次记录的事件 | 13 | 轮次不折工时 |

> 文件数 281 与事件数 {N} 接近，是因为千问台账本身按"一份文件一次执行"记账，且多数已做指令确为单文件单执行；
> 本次只做了有限合并（覆盖 >1 文件的事件 {multi_file} 条）与有限拆分（同一文件拆多事件，见 02 台账『覆盖序号』重复出现的行）。
> **不能据此推断"文件数≈修改次数"**：{len(multi)} 个事项含多事件，说明同一目标的修改分散在多份文件里。
''')


(BASE / '04-分类统计.md').write_text('\n'.join(md), encoding='utf-8')
print('已输出 02-事项事件台账.csv / 04-分类统计.md')
print('性质', nature.most_common())
print('原因', cause.most_common())
print('提示标签事件数', hint_events, hints.most_common())
