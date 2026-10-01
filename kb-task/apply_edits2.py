# -*- coding: utf-8 -*-
"""阶段 B 收尾 + C 前置：修过时口径，更新 sync-manual.mjs 章节映射与版本号。"""
import sys

ROOT = r"D:\workspace\pkub\裁切分析\MP42Txt\黑湖小工单_分析"


def load(path):
    with open(path, "rb") as f:
        raw = f.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    crlf = "\r\n" in text
    if crlf:
        text = text.replace("\r\n", "\n")
    return text, bom, crlf


def save(path, text, bom, crlf):
    out = text.replace("\n", "\r\n") if crlf else text
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    with open(path, "wb") as f:
        f.write(data)


def apply(path, edits):
    text, bom, crlf = load(path)
    for i, (old, new) in enumerate(edits):
        n = text.count(old)
        if n != 1:
            print(f"[FAIL] {path} edit#{i + 1} 出现 {n} 次（应为 1）")
            sys.exit(1)
        text = text.replace(old, new)
    save(path, text, bom, crlf)
    print(f"[OK] {path}：{len(edits)} 处")


# ---------- docs/manual/遇到问题.md：完成数旧口径（看板求和）改为已统一口径 ----------
pf = ROOT + r"\docs\manual\遇到问题.md"
apply(pf, [
    ("手册版本 1.2，更新于 2026-09-20。先核对工单、工序、时间和记录状态，结果不确定不要重复提交。",
     "手册版本 1.3，更新于 2026-09-24。先核对工单、工序、时间和记录状态，结果不确定不要重复提交。"),
    ("当前看板把各道有效良品相加，这个例子是 180，代表报工量合计。核实整单进度看工单详情。比较数字先确认页面和统计方式。",
     "看板与工单详情、列表用同一个最少值口径：这张单显示 80，不会显示 180。「工序达成进度」取各正计划工序（良品/计划）的最小值（0～100%）。生产报表只计已通过。比较数字先确认页面和统计口径。"),
])

# ---------- docs/manual/第一次使用.md：多账套看另一家厂的措辞 ----------
ps = ROOT + r"\docs\manual\第一次使用.md"
apply(ps, [
    ("登录后看到的是本厂数据；要看另一家工厂的资料，需使用获授权的对应系统和账号。",
     "登录后看到的是本厂数据；不同账套（工厂）的数据互相看不到。要看另一家工厂的资料，在登录页【账套 / 工厂】下拉选择该厂，并用该厂获授权的账号登录。"),
])

# ---------- frontend/scripts/sync-manual.mjs：章节映射与版本 ----------
pm = ROOT + r"\frontend\scripts\sync-manual.mjs"
apply(pm, [
    # 1) 基础资料：补批量导入
    ("const basic = ['pc/建账号、改密码', 'pc/建部门、把人加入部门', 'pc/配单位、不良品项和工序', 'pc/配工艺路线和产品']",
     "const basic = ['pc/建账号、改密码', 'pc/建部门、把人加入部门', 'pc/配单位、不良品项和工序', 'pc/批量导入', 'pc/配工艺路线和产品']"),
    # 2) 工单：修复失效旧标题，补派工/我的任务/执行监控落位
    ("const orders = ['pc/下工单', 'pc/打印二维码流转卡', 'pc/改工单、结束、撤回和取消', 'pc/查进度、查报表、看看板']",
     "const orders = ['pc/下工单', 'pc/派工：把工序派给工人', 'mobile/我的任务（被派工了看这里）', 'pc/打印二维码流转卡', 'pc/改工单、结束、撤回和取消', 'pc/查进度、查报表、看看板与执行监控']"),
    # 3) 报工与复核：补异常上报两端章节
    ("const reporting = ['pc/电脑报工', 'pc/复核：通过或退回报工', 'pc/报错数量怎么改', 'pc/一键补报未完工序', 'mobile/用手机摄像头扫码', 'mobile/用 PDA 扫码枪，或手动输入', 'mobile/选工序、填本次数量', 'mobile/提交后怎么确认成功']",
     "const reporting = ['pc/电脑报工', 'pc/复核：通过或退回报工', 'pc/报错数量怎么改', 'pc/一键补报未完工序', 'pc/处理现场异常上报', 'mobile/用手机摄像头扫码', 'mobile/用 PDA 扫码枪，或手动输入', 'mobile/选工序、填本次数量', 'mobile/提交后怎么确认成功', 'mobile/现场异常怎么上报']"),
    # 4) 数据口径：补工资算法口径（引用 pc/工价和工资）
    ("const quantity = ['faq/报工后生产报表为什么没增加', 'faq/完成数为什么是 80，不是 180', 'faq/工单找不到、结束后又变执行中', 'faq/超出计划数、剩余可报为零']",
     "const quantity = ['faq/报工后生产报表为什么没增加', 'faq/完成数为什么是 80，不是 180', 'faq/工单找不到、结束后又变执行中', 'faq/超出计划数、剩余可报为零', 'pc/工价和工资报表']"),
    # 5) 新篇：工资与预警
    ("const permission = [",
     "const salary = ['pc/工价和工资报表', 'pc/微信预警（企业微信推送，默认关闭）']\nconst permission = ["),
    # 6) 01-操作入门：补登录与选厂
    ("['SupportDocs/01-操作入门.md', '操作入门', ['start/先看懂每天要做什么', 'start/登录前准备什么', 'start/第一次配置的顺序', ...basic.slice(0, 2)]]",
     "['SupportDocs/01-操作入门.md', '操作入门', ['start/先看懂每天要做什么', 'start/登录前准备什么', 'pc/登录与选厂（多账套）', 'start/第一次配置的顺序', ...basic.slice(0, 2)]]"),
    # 7) outputs 增加 07 篇
    ("  ['SupportDocs/06-数据口径.md', '数据口径', quantity],",
     "  ['SupportDocs/06-数据口径.md', '数据口径', quantity],\n  ['SupportDocs/07-工资与预警.md', '工资与预警', salary],"),
    # 8) 版本号随生成更新
    ("version: 1.4\\nsystemVersion: 2026-09-22\\nupdatedAt: 2026-09-22",
     "version: 1.5\\nsystemVersion: 2026-09-24\\nupdatedAt: 2026-09-24"),
])

print("B 收尾 + sync 映射更新完成")
