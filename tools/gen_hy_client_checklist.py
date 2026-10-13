# -*- coding: utf-8 -*-
"""生成《华煜伞业-车间管理系统上线前-需客户提供与注意清单.docx》"""
import sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from docx import Document
from docx.shared import Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml.ns import qn

def _f(run, size=10.5, bold=False, color=None):
    run.font.name='微软雅黑'; run._element.rPr.rFonts.set(qn('w:eastAsia'),'微软雅黑')
    run.font.size=Pt(size); run.font.bold=bold
    if color: run.font.color.rgb=RGBColor(*color)

def title(doc,t):
    p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER; _f(p.add_run(t),16,True)
def sub(doc,t):
    p=doc.add_paragraph(); _f(p.add_run(t),12,True,(0x1f,0x4e,0x79))
    p.paragraph_format.space_before=Pt(8)
def line(doc,t,size=10.5,bold=False,color=None):
    p=doc.add_paragraph(); _f(p.add_run(t),size,bold,color)
def item(doc,t):
    p=doc.add_paragraph(style='List Bullet'); _f(p.add_run(t),10.5)

doc=Document()
title(doc,'华煜伞业 · 车间管理系统上线前——贵司需提供 / 配合事项清单')
line(doc,'（本清单供贵司提前准备，正式上线前我们派单核对、协助录入）',9,color=(0x66,0x66,0x66))
doc.add_paragraph()

sub(doc,'一、请贵司准备并提供以下基础资料（我们录入系统用）')
item(doc,'产品清单：每款产品的【编号 + 名称 + 规格型号 + 单位】（编号要唯一，相当于产品身份证）')
item(doc,'产品图纸 / 图片：有的话给我们，挂到系统里供工人查看')
item(doc,'每款产品的工艺流程：从第一道到最后一道的【工序顺序】（例如 裁布→包边→…→包装）')
item(doc,'工序名称及对应部门：每道工序由哪个班组负责（报工权限就按部门来）')
item(doc,'部门 / 班组及人员名单：部门名称、工人和班组长的名单（用于建账号、分配权限）')
item(doc,'计件工价表：每道工序的【单价】；如按不良品扣款，写明扣款规则')
item(doc,'常见不良品项：生产中最常见的质量问题类别（如 尺寸不符、印花不良、车缝跳线等），用于报工时填写')

sub(doc,'二、现场硬件与网络（报工要能用）')
item(doc,'PDA 或手机：车间工人用于扫码报工的设备（PDA 扫码头优先，手机需能装网页报工）')
item(doc,'网络：车间报工点位需覆盖 WiFi / 局域网，能访问系统')
item(doc,'看板 / 大屏（可选）：用于车间生产看板展示的大屏或电视，及摆放位置')
item(doc,'条码 / 二维码：工单、产品的二维码打印方案（我们提供格式）')

sub(doc,'三、账号与权限（我们配置，请指定对接人）')
item(doc,'指定一名管理员：负责建账号、录数据、日常维护')
item(doc,'三类角色分工：工人＝报工、班组长＝复核、管理员＝管理')
item(doc,'报工权限按部门划分：只有该工序对应部门的人才能给这道工序报工')

sub(doc,'四、上线前请贵司确认并拍板的事项')
item(doc,'工价表：每道工序单价，请老板 / 管理签字确认（计件工资以此为准）')
item(doc,'不良品扣款规则：哪些不良要扣款、扣多少，确认后写入系统')
item(doc,'工序与部门对应关系：确认每道工序归哪个班组')

sub(doc,'五、日常使用中请注意（红线，建议培训时重点强调）')
item(doc,'产品编号一经确定不要改、不要重复（改了会乱账）')
item(doc,'报工数据要真实：良品数、不良数如实填写，不要虚报')
item(doc,'班组长必须复核：工人报的工，班组长核对通过后工资才生效；被驳回的不计钱')
item(doc,'删除有顺序：工单 → 产品 → 工艺 → 工序 → 不良品项 → 单位 → 部门；有下级数据的不能直接删')
item(doc,'工资可追溯可重算：每一笔钱都能查到来源，发现不对可以重算，不要手工乱改')

sub(doc,'六、我们能提供的支持')
item(doc,'协助建账套、录入基础资料、开通账号')
item(doc,'现场或线上培训：工人报工、班组长复核、看板查看')
item(doc,'提供操作手册；上线后跟进使用情况')

doc.add_paragraph()
line(doc,'如有疑问或资料准备不齐，请与我们对接人联系，我们逐项协助。',10,color=(0x1f,0x4e,0x79))

out=r'D:\workspace\pkub\bee\docs\AI模拟数据\华煜伞业-系统上线前-需客户提供与注意清单.docx'
doc.save(out)
print('saved:',out)
