# -*- coding: utf-8 -*-
"""生成《打蛋器图纸》PDF — 二维正视示意图 + 尺寸标注 + 标题栏."""
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Ellipse, Rectangle, FancyArrowPatch
import matplotlib.font_manager as fm

# 中文字体
import sys
for f in ['Microsoft YaHei', 'SimHei', 'SimSun']:
    if any(f.lower() == x.name.lower() for x in fm.fontManager.ttflist):
        plt.rcParams['font.sans-serif'] = [f]; break
plt.rcParams['axes.unicode_minus'] = False

OUT = r"D:\workspace\pkub\裁切分析\MP42Txt\黑湖小工单_分析\打蛋器_图纸.pdf"

fig, ax = plt.subplots(figsize=(11.69, 8.27), dpi=150)  # A4 横向
ax.set_xlim(-110, 120); ax.set_ylim(-45, 320)
ax.set_aspect('equal'); ax.axis('off')

INK = 'black'; CTR = '#888888'; DIM = '#c00000'

# ---- 图框 ----
ax.add_patch(Rectangle((-108, -42), 224, 356, fill=False, edgecolor=INK, lw=1.2))
ax.add_patch(Rectangle((-104, -38), 216, 348, fill=False, edgecolor=INK, lw=2.2))

# ---- 中心线（点划线） ----
ax.plot([0, 0], [0, 292], color=CTR, lw=0.8, linestyle=(0, (8, 4, 1, 4)))

# ---- 打蛋器正视轮廓 ----
# 握柄
ax.add_patch(Rectangle((-13, 240), 26, 35, fill=False, edgecolor=INK, lw=2.0))
# 连接杆
ax.add_patch(Rectangle((-4, 130), 8, 110, fill=False, edgecolor=INK, lw=2.0))
# 打蛋头：左右两个对称线环（椭圆）
for cx in (-32, 32):
    ax.add_patch(Ellipse((cx, 72), 60, 116, fill=False, edgecolor=INK, lw=2.0))
# 打蛋头环在杆端的衔接短线
ax.plot([-30, 30], [130, 130], color=INK, lw=2.0)

def dim_h(x, y1, y2, label, ext=8):
    """竖直尺寸线（带箭头+文字）"""
    ax.annotate('', xy=(x, y2), xytext=(x, y1),
                arrowprops=dict(arrowstyle='<->', color=DIM, lw=1.1, shrinkA=0, shrinkB=0))
    ax.text(x, (y1 + y2) / 2, label, fontsize=12, color=DIM,
            ha='left', va='center', rotation=90,
            bbox=dict(fc='white', ec='none', pad=0.6))

def dim_w(y, x1, x2, label, ext=8):
    ax.annotate('', xy=(x1, y), xytext=(x2, y),
                arrowprops=dict(arrowstyle='<->', color=DIM, lw=1.1, shrinkA=0, shrinkB=0))
    ax.text((x1 + x2) / 2, y + 4, label, fontsize=12, color=DIM, ha='center')

# 尺寸线引线
for (yy, xx, d) in [(14, -40, 110), (240, 55, 15), (275, 55, 15), (130, 55, 15)]:
    pass

# 竖直尺寸：总高、打蛋头长、手柄长
dim_h(72, 14, 275, '260')          # 总高
dim_h(-84, 14, 130, '116')         # 打蛋头
dim_h(84, 240, 275, '35')          # 手柄
# 水平尺寸：打蛋头宽
dim_w(0, -62, 62, '110')
# 尺寸文字旋转修正的间距
ax.text(0, 305, '主视图', fontsize=13, ha='center', fontweight='bold')

# 局部引线（杆直径示意）
ax.plot([-12, -30], [180, 180], color=DIM, lw=0.8)
ax.text(-34, 184, 'φ8', fontsize=11, color=DIM, ha='center')

# ---- 标题栏（右下） ----
bx0, by0, bw, bh = 20, -38, 96, 28
ax.add_patch(Rectangle((bx0, by0), bw, bh, fill=False, edgecolor=INK, lw=1.0))
titles = ['打蛋器  DDQ-001', '材质：304 不锈钢', '比例 1:1    单位 mm', '版本 V1.0    2026-09-25']
for i, t in enumerate(titles):
    ax.text(bx0 + 2, by0 + bh - 3 - i * 6.4, t, fontsize=9.5, va='top', ha='left')

# 主标题（图框内顶部）
ax.text(0, 312, '打 蛋 器 图 纸', fontsize=16, ha='center', fontweight='bold')

fig.savefig(OUT)
print('saved:', OUT)
import os; print('exists:', os.path.exists(OUT), 'size:', os.path.getsize(OUT))
