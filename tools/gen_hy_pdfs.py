# -*- coding: utf-8 -*-
"""HY伞业（华煜伞业）晴雨伞 图纸+作业指导书 生成器
- 图纸 PDF（matplotlib）
- 作业指导书 DOCX（python-docx，再用 Word COM 转 PDF）
输出到 docs/AI模拟数据/PDF输出/
"""
import os, json
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Arc, Rectangle, FancyArrowPatch
import matplotlib.font_manager as fm

OUT = r"D:\workspace\pkub\bee\docs\AI模拟数据\PDF输出"
os.makedirs(OUT, exist_ok=True)

# 中文默认字体
for f in ['Microsoft YaHei', 'SimHei', 'SimSun']:
    if any(f.lower() == x.name.lower() for x in fm.fontManager.ttflist):
        plt.rcParams['font.sans-serif'] = [f]; break
plt.rcParams['axes.unicode_minus'] = False

# ============ 1. 图纸 PDF（matplotlib 画三折晴雨伞正视示意图） ============
def make_drawing_pdf(path):
    fig, ax = plt.subplots(figsize=(11.69, 8.27), dpi=150)  # A4 横向
    ax.set_xlim(-120, 120); ax.set_ylim(-30, 340)
    ax.set_aspect('equal'); ax.axis('off')
    INK='black'; CTR='#777'; DIM='#c00000'
    # 图框
    ax.add_patch(Rectangle((-112,-26),224,360,fill=False,edgecolor=INK,lw=1.2))
    ax.add_patch(Rectangle((-107,-21),214,352,fill=False,edgecolor=INK,lw=2.4))
    # 中心线
    ax.plot([0,0],[-5,300],color=CTR,lw=0.8,linestyle=(0,(8,4,1,4)))
    # 伞面（展开伞：外弧 + 内弧）
    import numpy as np
    th = np.linspace(15,165,200)*np.pi/180
    R = 55
    ax.plot(3+R*np.cos(th), 190+R*np.sin(th), color=INK, lw=2.0)   # 外缘
    ax.plot(3+0.86*R*np.cos(th), 190+0.86*R*np.sin(th), color=INK, lw=1.2)  # 内缘(伞面下缘示意)
    # 伞骨（8 根放射线）
    for ang in np.linspace(25,155,8):
        a=ang*np.pi/180
        ax.plot([3, 3+0.97*R*np.cos(a)],[190, 190+0.97*R*np.sin(a)], color=CTR, lw=1.0)
    # 伞顶珠
    ax.add_patch(plt.Circle((3,245),2.2,fill=False,edgecolor=INK,lw=1.4))
    # 中棒
    ax.plot([0,0],[190,10],color=INK,lw=2.6)
    # 伞柄（弯柄）
    ax.plot([0,-14],[10,10],color=INK,lw=2.6)
    ax.add_patch(Arc((-14,10),22,22,theta1=90,theta2=270,color=INK,lw=2.6))
    # 尺寸标注（伞面半径）
    ax.annotate('',xy=(3+55,190),xytext=(3,190),arrowprops=dict(arrowstyle='<->',color=DIM,lw=1.2))
    ax.text(30,176,'R550 (伞面半径 mm)',fontsize=9,color=DIM)
    ax.annotate('',xy=(3,190),xytext=(3,245),arrowprops=dict(arrowstyle='<->',color=DIM,lw=1.2))
    ax.text(8,214,'伞顶高度',fontsize=9,color=DIM)
    ax.text(0,-17,'伞柄长 120mm',fontsize=9,color=DIM)
    # 标注
    ax.text(0,315,'三折晴雨伞 正视示意图',fontsize=15,ha='center',weight='bold')
    ax.text(0,300,'规格：55cm 八骨 · 玻纤骨+铁中棒 · 210T碰击布+黑胶涂层',fontsize=10,ha='center',color='#333')
    ax.text(0,286,'伞面展开半径 R550 · 收起长约 300mm · 伞柄长 120mm',fontsize=9.5,ha='center',color='#555')
    # 标题栏（右下）
    ax.add_patch(Rectangle((-105,-26),210,22,fill=True,edgecolor=INK,lw=1,facecolor='#f2f2f2'))
    ax.text(-100,-19,'HY-001',fontsize=9); ax.text(-60,-19,'三折晴雨伞（黑胶防紫外线）',fontsize=9)
    ax.text(60,-19,'比例 1:10  版本 V1.0',fontsize=9)
    fig.savefig(path, format='pdf')
    plt.close(fig)
    print("图纸PDF:", path)

# ============ 2. 作业指导书 DOCX（python-docx） ============
from docx import Document
from docx.shared import Pt, Cm, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_LINE_SPACING
from docx.oxml.ns import qn

def _set_font(run, size=10.5, bold=False, color=None):
    run.font.name = '微软雅黑'
    run._element.rPr.rFonts.set(qn('w:eastAsia'), '微软雅黑')
    run.font.size = Pt(size); run.font.bold = bold
    if color: run.font.color.rgb = RGBColor(*color)

def _title(doc, text):
    p = doc.add_paragraph(); p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    _set_font(p.add_run(text), 18, True, (0,0,0)); return p

def _sub(doc, text):
    p = doc.add_paragraph(); _set_font(p.add_run(text), 12, True, (0x1f,0x4e,0x79))
    p.paragraph_format.space_before = Pt(6); return p

def _line(doc, text, size=10.5, bold=False):
    p = doc.add_paragraph(); _set_font(p.add_run(text), size, bold); return p

def _kv(doc, k, v):
    p = doc.add_paragraph()
    _set_font(p.add_run(k), 10.5, True); _set_font(p.add_run(v), 10.5)
    return p

def _list(doc, items):
    for it in items:
        p = doc.add_paragraph(style='List Bullet'); _set_font(p.add_run(it), 10.5)

def product_guide_docx(path):
    doc = Document()
    _title(doc, '三折晴雨伞 作业指导书')
    _line(doc, 'HY-001 · 三折晴雨伞（黑胶防紫外线）', 11, True)
    _line(doc, '版本 V1.0 · 华煜伞业', 9)
    _sub(doc, '一、产品信息')
    _kv(doc, '产品编号：', 'HY-001')
    _kv(doc, '产品名称：', '三折晴雨伞（黑胶防紫外线）')
    _kv(doc, '规格：', '55cm 八骨 · 玻纤骨+铁中棒 · 210T碰击布+黑胶涂层')
    _kv(doc, '单位：', '把')
    _kv(doc, '工艺路线：', 'RT-HY 华煜晴雨伞标准线（13 道工序）')
    _sub(doc, '二、作业流程（13 道工序）')
    _list(doc, [
        '1. 裁布 → 2. 包边 → 3. 面料检验 → 4. 三角片裁切 → 5. 丝网印刷 → 6. 车缝',
        '7. 固定绑带 → 8. 上伞帽 → 9. 装伞头珠 → 10. 缝制 → 11. 质检 → 12. 装伞柄 → 13. 包装'])
    _sub(doc, '三、关键质量要点')
    _list(doc, [
        '伞面尺寸符版、无跳线脱线；丝网印刷 Logo 定位准、色牢度达标；',
        '开合顺畅不卡涩，伞骨无变形断裂；伞帽、伞珠、伞柄装配牢固；',
        '每把经质检开合测试合格后包装，附件（伞套/说明书）齐全。'])
    _sub(doc, '四、常见不良品项')
    _line(doc, '尺寸不符 / 布面疵点 / 色差 / 跳线脱线 / 印花不良 / 伞骨变形断裂 / 开合卡涩 / 伞面破损 / 包装破损', 10)
    _sub(doc, '五、安全提示')
    _list(doc, ['裁切、车缝设备操作注意人身安全，戴好防护；', '印刷、胶水工序注意通风与消防。'])
    doc.save(path); print("产品作业指导书DOCX:", path)

OP_GUIDES = [
    ('CB','裁布','领取伞面布料 → 核对色卡/材质 → 按版型裁剪伞面大布 → 标注三角片区 → 转下一工序','自动裁布机 / 电剪','尺寸符版、无毛边、布面无疵点','尺寸不符、布面疵点'),
    ('BB','包边','裁剪后布边锁边 → 检查包边平直无散边 → 转下一工序','锁边机 / 包边机','包边平直、无跳线、布边不散','跳线脱线'),
    ('MLJY','面料检验','来料验布 → 核对颜色/克重 → 目检疵点色差 → 合格放行','验布机 / 目检台','无色差、无疵点、克重达标','布面疵点、色差'),
    ('SJP','三角片裁切','按模板裁切伞面三角片 → 点数核片数 → 转车缝','裁片机 / 模板冲裁','三角片尺寸符版、片数齐全','尺寸不符'),
    ('SYYS','丝网印刷','定位放板 → 丝印 Logo → 烘道固化 → 目检印品','丝网印刷台 / 烘道','Logo 定位准、无漏印、色牢度达标、无沾污','印花不良、布面疵点'),
    ('CF','车缝','将三角片依序拼缝 → 检查针距/线迹 → 转绑带','平缝机','针距均匀、无线歪斜、无跳线脱线','跳线脱线'),
    ('GDBD','固定绑带','缝制伞面绑带 → 检查绑带位置牢固 → 转装配','平缝机','绑带位置正、缝线牢固','绑带歪斜'),
    ('SYM','上伞帽','安装伞顶帽 → 压紧/锁固 → 检查不松动','压帽机 / 手工','伞帽牢固、不松动、不脱落','伞帽松动'),
    ('ZSYZ','装伞头珠','在伞骨末端装伞头珠 → 铆接/固定 → 检查','铆接工具 / 手工','伞珠牢固、不错装','伞珠脱落'),
    ('FZ','缝制','伞面与伞骨固定缝线 → 加固关键受力点 → 检查伞面平顺','平缝机 / 专用机','缝线牢固、伞面无皱褶、无线歪斜','跳线脱线、伞面皱褶'),
    ('ZJ','质检','开合测试 → 检查骨位/伞面/功能 → 判定合格或退回','开合测试台','开合顺畅、伞骨不变形、伞面无破损、功能正常','开合卡涩、伞骨变形断裂、伞面破损'),
    ('ZSB','装伞柄','安装伞柄/手柄 → 打紧/锁固 → 检查安装到位','螺丝刀 / 打螺丝机','伞柄牢固、安装到位、不松动','伞柄松动'),
    ('BZ','包装','折叠装伞套 → 放说明书 → 封箱 → 贴标','包装台 / 封箱机','包装完好、附件齐全、封箱牢固','包装破损、附件缺失'),
]

def op_guide_docx(path, code, name, steps, equip, quality, defect):
    doc = Document()
    _title(doc, f'{name} 工序作业指导书')
    _line(doc, f'工序：{code} {name} · 华煜伞业 晴雨伞标准线 · 版本 V1.0', 9.5)
    _sub(doc, '一、工序概述')
    _line(doc, f'本工序为三折晴雨伞「{name}」作业环节，编号 {code}。')
    _sub(doc, '二、作业步骤')
    _list(doc, steps.split(' → '))
    _sub(doc, '三、设备/工具')
    _line(doc, equip)
    _sub(doc, '四、质量要求')
    _line(doc, quality)
    _sub(doc, '五、常见不良品项')
    _line(doc, defect)
    _sub(doc, '六、安全提示')
    _list(doc, ['按设备操作规程作业，注意防护；', '发现异常立即上报班组长，不停机带病作业。'])
    doc.save(path); print("工序指导书DOCX:", code, path)

make_drawing_pdf(os.path.join(OUT, '三折晴雨伞_图纸.pdf'))
product_guide_docx(os.path.join(OUT, '三折晴雨伞_作业指导书.docx'))
for code,name,steps,equip,quality,defect in OP_GUIDES:
    op_guide_docx(os.path.join(OUT, f'{name}工序作业指导书.docx'), code, name, steps, equip, quality, defect)
print("DOCX 与图纸生成完成")
