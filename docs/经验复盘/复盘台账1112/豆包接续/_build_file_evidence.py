"""从281份已做指令正文提取：记录日、状态原文、实施判定、验证判定、分类依据。不改原文件。"""
from pathlib import Path
import re, csv

DOCS = Path(r'D:\workspace\pkub\bee\docs')
BASE = Path(__file__).resolve().parent
plan = {int(r['序号']): r for r in csv.DictReader((BASE.parent / '01-千问逐文件台账.csv').open(encoding='utf-8-sig'))}

# 本次已全文阅读（A级）的文件名
FULL_READ = {
    '55-执行指令-修复SupportDocs中文检索bug.md',
    '95-豆包纠正指令-FAQ代报工与多人分摊已上线.md',
    '87-开工卡-新手入门预览版.md',
    '432-优化指令-建单取价提示文案可读性.md', '433-优化指令-建单工艺属性取价提示不误导.md',
    '434-优化指令-建单工艺必填星号与字段级感叹.md', '435-开发指令-工单保存待审与审核锁价.md',
    '445-优化指令-取价缺字段旁感叹号对齐ErrorProvider.md', '452-优化指令-工单报工基础资料缺价与单价提示一体化整改.md',
    '261-优化指令-AI助手电脑部门页签重写-最终版.md', '133-执行指令-回滚Pushplus推送（推送整体搁置）.md',
    '135-优化指令-工资单栏位拿掉全部手工发薪项.md', '136-总开工单-工资单批量重算与栏位全拿.md',
    '86-开工单-先拆学操作再装新手入门.md', '11-排查指令-手机摄像头扫码异常-Cursor接手.md',
    '425-优化指令-工单暂存与提交锁价.md', '07-优化指令-工单修改.md',
}

def judge_status(status_text):
    t = status_text
    if re.search(r'(回滚完成|已回滚|性质：正式代码回滚|全量回滚)', t):
        impl = '已撤回'
    elif re.search(r'(方案稿|待拍板|分析对标稿|非授权开工)', t):
        impl = '仅方案（未授权开工）'
    elif re.search(r'(待排查修复|排查中)', t):
        impl = '实施中/未完成'
    elif re.search(r'(待做|未开始|文档阶段，待执行|待开发)', t):
        impl = '未实施/仅方案'
    elif re.search(r'(已并入|被.*取代)', t):
        impl = '仅方案/被取代'
    elif re.search(r'(已做|已实现|已完成|已落地|已执行|已修复|已上线|已实施|已排查|已归档|代码已交付|代码已落地)', t):
        impl = '已实现'
    elif not t.strip():
        impl = '未标注'
    else:
        impl = '待判'
    if re.search(r'(现场|真机|点选|点验|勾选|浏览器|联调|未部署|未验|待验|走查).{0,30}(未|待|空白|另记)', t) \
       or re.search(r'(未现场|未真机|未部署|勾选.{0,6}空白|待现场|现场.{0,4}待|待验收|未验收)', t):
        verify = ('部分验证（构建/单测通过，现场未验）' if re.search(r'(构建|build|单测|测试).{0,12}(通过|完成|过)', t) else '现场/页面未验；其它验证须查正文')
    elif re.search(r'(验收通过|运行时验收|真机通过|通过且|及验收|验收归档)', t):
        verify = '已验证（原文称通过，范围见原文）'
    elif impl == '已实现':
        verify = '未记录验证范围'
    else:
        verify = '未验证/不适用'
    return impl, verify

rows = []
for idx in range(1, 282):
    name = plan[idx]['文件名']
    p = DOCS / '已做指令' / name
    txt = p.read_text(encoding='utf-8-sig', errors='ignore') if p.exists() else ''
    head = '\n'.join(txt.splitlines()[:18])
    status_line = next((l.strip() for l in txt.splitlines()[:18] if re.search(r'状态[：:]', l)), '')
    if not status_line:
        status_line = next((l.strip() for l in txt.splitlines()[:18] if re.search(r'(完成日期|已落地|状态：)', l)), '')
    dates = re.findall(r'20\d{2}-\d{1,2}-\d{1,2}', head)
    impl, verify = judge_status(status_line)
    # 人工核对后的定点修正（状态行表述特殊，避免规则漏判）
    override = {
        18: ('已实现', '部分验证（构建/单测通过，现场未验）'),
        56: ('已实现', '部分验证（构建/单测通过，现场未验）'),
        173: ('已实现', '已验证（原文称通过，范围见原文）'),
        187: ('已实现', '部分验证（构建/单测通过，现场未验）'),
        230: ('已实现', '已验证（原文称通过，范围见原文）'),
        262: ('未标注', '未验证/不适用'),
    }
    if idx in override:
        impl, verify = override[idx]
    grade = 'A 全文阅读' if name in FULL_READ else ('B 状态行+分类依据原句' if plan[idx]['分类依据'] and not plan[idx]['分类依据'].startswith('正文无依据') else 'C 仅摘要/待复核')
    rows.append({
        '序号': idx, '文件名': name, '记录日': dates[0] if dates else '',
        '状态原文': status_line[:180], '实施判定': impl, '验证判定': verify,
        '千问原分类': plan[idx]['原分类'], '分类依据原句': plan[idx]['分类依据'][:120],
        '复核等级': grade,
    })
out = BASE / '01-文件级证据.csv'
with out.open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0]))
    w.writeheader(); w.writerows(rows)
from collections import Counter
print(len(rows), Counter(r['实施判定'] for r in rows), Counter(r['验证判定'] for r in rows), Counter(r['复核等级'] for r in rows))
