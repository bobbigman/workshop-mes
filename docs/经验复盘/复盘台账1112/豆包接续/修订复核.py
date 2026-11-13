"""2026-11-13 Codex：修正已证实错误，保留未复核范围；不伪造全量完成。"""
from pathlib import Path
import csv, collections, runpy

B=Path(__file__).resolve().parent

def replace_line(text, key, new):
    lines=text.splitlines()
    hits=[i for i,l in enumerate(lines) if key in l]
    assert len(hits)==1,(key,hits)
    lines[hits[0]]=new
    return '\n'.join(lines)+'\n'

p=B/'_events_3.py'
s=p.read_text(encoding='utf-8')
s=replace_line(s,'"SupportDocs 中文检索",', ' ((229,), "SupportDocs 中文检索", "修正验收脚本中文请求编码造成的假检索失败", "部署运维", "验收脚本", "环境交付", "外部环境", "原文明确", "无", "已实现", "已验证（原文称通过，范围见原文）", "55、54", "§1及§8：中文检索本身正常；未改SupportDocumentService；验收脚本改curl+UTF-8，19/19及ALL PASS"),')
s=replace_line(s,'"FAQ 口径纠正",', ' ((277,), "FAQ 口径纠正", "将FAQ误写未上线纠正为代报工和多人分摊已上线", "知识库", "文档纠错", "仅方案文档", "文档版本不同步", "原文明确", "内容不准", "文档修正要求，实施未核实", "未验证/不适用", "95、94", "§错在哪：旧稿把规划中、未上线写成现状，实际已上线；只改faq和手册两处，验收未勾；不计业务代码修复"),')
s=replace_line(s,'"学操作功能",', ' ((269, 271), "学操作功能", "移除学操作功能（菜单/路由/后端/3表/种子全拆）", "帮助说明", "—", "方向性重做", "未知", "原文明确", "无", "已实现", "部分验证，生产删表及页面验收未确认", "78、82、83、84、86", "84：用户确认学操作不是想要的东西；86仅编排84拆除与85新增，不另计一次开发。是否误解或产品改变缺当时决定证据，原因留未知"),')
s=replace_line(s,'"新手入门",', ' ((270, 271, 272), "新手入门", "新增新手入门页面（方案、编排与预览交付合并）", "帮助说明", "移动端", "首次开发", "—", "交叉核实", "无", "已实现（页面）；首次登录提示未确认", "页面/首次登录验收未确认", "85、86、87", "86合并84/85执行；87明确只做85页面+路由+菜单子集，不重复计首次开发。首次登录提示实施不明，保留缺口"),')
s='\n'.join(l for l in s.splitlines() if '((271,),' not in l and '((272,),' not in l)+'\n'
p.write_text(s,encoding='utf-8')

p=B/'_build_file_evidence.py'; s=p.read_text(encoding='utf-8')
s=s.replace("FULL_READ = {", "FULL_READ = {\n    '55-执行指令-修复SupportDocs中文检索bug.md',\n    '95-豆包纠正指令-FAQ代报工与多人分摊已上线.md',\n    '87-开工卡-新手入门预览版.md',", 1) if '95-豆包纠正指令-FAQ代报工与多人分摊已上线.md' not in s else s
s=s.replace("elif re.search(r'(验收通过|运行时验收|真机通过|全部完成|通过且|及验收|验收归档)', t):", "elif re.search(r'(验收通过|运行时验收|真机通过|通过且|及验收|验收归档)', t):")
# 现场未验并不证明已做构建/单测，避免凭未验关键字补验证。
s=s.replace("verify = '部分验证（构建/单测通过，现场未验）'", "verify = ('部分验证（构建/单测通过，现场未验）' if re.search(r'(构建|build|单测|测试).{0,12}(通过|完成|过)', t) else '现场/页面未验；其它验证须查正文')")
p.write_text(s,encoding='utf-8')

p=B/'汇总事件台账.py'; s=p.read_text(encoding='utf-8')
s=s.replace("'提示交互纠正': '正常完善'", "'提示交互纠正': '未知'")
s=s.replace("    rows_out.append({", "    nature_candidate = n2\n    review_status = '候选分类，尚未独立全文复核'\n    if nature == '提示交互纠正':\n        n2 = '未知'\n        review_status = '提示体验已标记；正常完善或纠错待核实'\n    if any(n < 0 for n in nums):\n        grade = 'C 外部补充线索，来源全文未核实'\n        review_status = '补充线索，不纳入已做主样本'\n    rows_out.append({") if "nature_candidate = n2" not in s else s
s=s.replace("'实施状态': impl, '验证状态': verify, '证据等级': grade,", "'实施状态': impl, '验证状态': verify, '证据等级': grade,\n        '原性质候选': nature_candidate, '分类复核状态': review_status,\n        '原因依据类型': evid, '样本范围': '已做主样本' if real else '外部补充线索',\n        '稳定定位键': ','.join(map(str, nums)) + ':' + title,") if "稳定定位键" not in s else s
# 汇总语义不再由best-status将部分来源的通过升级到整个事件通过。
s=s.replace("    for lv in BEST_VERIFY:", "    if len(got) > 1: return '来源验证状态不一，须分项核对'\n    for lv in BEST_VERIFY:") if "if len(got) > 1:" not in s else s
p.write_text(s,encoding='utf-8')

runpy.run_path(str(B/'_build_file_evidence.py'))
runpy.run_path(str(B/'汇总事件台账.py'))
R=list(csv.DictReader((B/'02-事项事件台账.csv').open(encoding='utf-8-sig')))
F=list(csv.DictReader((B/'01-文件级证据.csv').open(encoding='utf-8-sig')))
T=list(csv.DictReader((B/'03-时间证据.csv').open(encoding='utf-8-sig')))
for t in T:
    if t['类型']=='实测投入':
        t['类型']='用户反馈约数（回顾记录）'
        t['是否可累加为工时']='不作为精确工时合计'
        t['说明']='原文明确为用户反馈约数，无原始起止计时；不外推整体'
    if t['类型']=='覆盖说明':
        t['数值']='2条用户反馈约数；尚未建立可核实的事件时间关联分母'
        t['说明']='不得用2/270或2/273当精确计时覆盖率'
with (B/'03-时间证据.csv').open('w',encoding='utf-8-sig',newline='') as f:
    w=csv.DictWriter(f,fieldnames=list(T[0]));w.writeheader();w.writerows(T)

def table(counter):
    return '\n'.join(['| 类别 | 条数 |','|---|---:|']+[f'| {k} | {v} |' for k,v in counter.most_common()])
main=[r for r in R if r['样本范围']=='已做主样本']
extra=[r for r in R if r['样本范围']!='已做主样本']
h=[r for r in main if r['提示体验标签'] not in ('','无')]
reason=collections.Counter(r['原因'] for r in main if r['性质'] in ('缺陷修复','方向性重做'))
summary=f'''# 04 分类统计（Codex修订，2026-11-13）

状态：**有限复核的候选事件统计，非全项目定论**。

## 范围与分母

台账共{len(R)}条候选事件、{len(set(r['事项ID'] for r in R))}个归并事项。其中已做指令主样本{len(main)}条、外部补充线索{len(extra)}条，二者分开。
281个原文件序号均有映射，这只代表摘要映射覆盖，不代表281份全文已复核，也不代表事件归并无误。
EVT编号在重算后可能变化，核对时同时使用稳定定位键及来源序号。

## 已做主样本性质（包含未实施、未知；不是返工率）

{table(collections.Counter(r['性质'] for r in main))}

提示体验标签不能自动决定正常完善或缺陷修复。旧脚本将全部提示纠正映射正常完善的规则已取消；无当时要求证据的保留未知。

## 提示专项

主样本带提示标签{len(h)}/{len(main)}条，只是候选标签覆盖；标签可能包含新功能说明，尚非确认体验缺陷比例。
{table(collections.Counter(tag for r in h for tag in r['提示体验标签'].split('、')))}

不能把标签频次与首次开发、正常完善等性质比较后断言“提示问题不突出”。真实阻断必须逐条查操作与结果，已取消关键词命中当真实阻断数量的统计。

## 原因候选（仅主样本修复/重做，保留未知）

{table(reason)}

这些是仍待复核的原因分类，不是责任判定。原文件全文阅读等级与原因证据等级分开；全文读过也不自动证明原因成立。

## 文件验证记录（不合并成单一验收等级）

{table(collections.Counter(r['验证判定'] for r in F))}

状态行扫描只能定位线索，不足以断言全文没记验证。未验现场也不自动证明构建或单测已过。

## 时间记录类型

{table(collections.Counter(t['类型'] for t in T))}

实际仅有2条用户反馈约数（约38分钟、约6分钟，来自同一复盘），不是原始计时。轮次记录行数从CSV生成，不再硬编码13条；时间未建立逐事件完整关联，不计算精确工时覆盖率或提效百分比。

## 重现方式

运行 `python 修订复核.py`。事件定义→文件证据→事件台账→本统计及06同时重建。`汇总事件台账.py`是内部步骤，请勿单独运行后将其旧模板报告当最终输出。
原语义分类除已列修正外多数沿用豆包候选；尚须全文校准与补读作废、未做、测试反馈后，才能报告总体排序。
'''
(B/'04-分类统计.md').write_text(summary,encoding='utf-8')
(B/'06-原因分析与改进建议.md').write_text(f'''# 06 原因分析与改进建议（Codex修订）

2026-11-13。当前是已覆盖资料的初稿，不能作为全项目最终归因。

## 已核实事实

- 已做队列281文件有映射；当前候选事件{len(main)}条，另有{len(extra)}条外部补充线索。文件覆盖不等于全文复核。
- 中文检索55：原文§1/§8明确服务正常，客户端验收脚本中文编码导致假失败；未改检索源码。已从实现缺陷改为环境/验收处理。
- FAQ95：旧稿将已上线误写成未上线，修正的是文档事实；原台账把方向写反，已纠正，不能当业务代码缺陷。
- 新手入门85/86/87：86是执行编排、87明确是85的页面子集；已合并页面首次交付，86的拆除部分关联84。首次登录提示实施仍未知。
- 提示纠正不再自动归正常完善。当前统计见04；未知不记作无问题，也不当作已确认返工。
- 时间仅有2条用户回顾约数；没有支撑最贵、最耗时、吃掉时间的链等排序的数据。

## 撤回或降级的旧结论

撤回“提示问题不是最多”“多数提示是正常完善”“18条重做原因全部是产品决定变化”“验证没有承载体”“最贵文案链”“学操作每版均按当时要求做对”等结论。
原因：维度不一致、分类预置、报告与台账不符，或缺少当时要求/时间证据。18条旧重做记录实际有12条产品变化、6条理解偏差；修订后须重新读台账，不沿用旧计数。

## 暂可采用的建议（不是已证明能省多少时间）

1. 新概念先用操作实例或原型核对目标；学操作拆除说明方向核对有价值，但尚不能判谁导致偏差。
2. 提示核对用户动作、出现时点、实际是否可继续和处理位置；取价提示链中确有指引与展示问题，也含锁价规则演进，分开看。
3. 多份方案/执行/验收采用同一事项和事件映射，记录替代关系；不要求为所有小改动另建审批或签字步骤。
4. 外部通道做满足目的的最小验证；不承诺固定30分钟，不把所有功能强制依赖现场验收。
5. 验证按适用范围记构建、接口、页面、真机等实际证据，允许不适用；状态行没有记录时继续查正文。
6. 后续需要时间比较时记录口径、实际投入与等待；起止时间差不是净工时。不设未经验证的80%等达标线。

## 尚未完成

全文校准大多数事件；补读作废/未做/处理中/测试反馈；复核其余归并、拆分和原因；建立时间→事件关联及抽检误差记录。完成前不报全项目返工率、主要原因排名或提效百分比。
''',encoding='utf-8')
(B/'_sync06.py').write_text('"""已停用旧版字符串替换同步，避免将旧数字写回。"""\nimport runpy\nfrom pathlib import Path\nrunpy.run_path(str(Path(__file__).with_name("修订复核.py")))\n',encoding='utf-8')
print('Codex修订输出完成',len(R),len(main),len(extra))
