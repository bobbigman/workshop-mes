# -*- coding: utf-8 -*-
"""
生成朋友圈九宫格图片包（可复用脚本）
- 把 wechat-card.jpg 中的二维码裁出，合成「扫码加老胡」卡片到封面
- 输出 moments/1.jpg ~ 9.jpg（文件名即发圈选图顺序）
- 顺带输出 moments/文案.txt
用法: python make_moments.py
"""
import os
from PIL import Image, ImageDraw, ImageFont, ImageOps

BASE = os.path.dirname(os.path.abspath(__file__))
POSTERS = os.path.join(BASE, "posters")
OUT = os.path.join(BASE, "moments")
QR_SRC = os.path.join(BASE, "wechat-card.jpg")

FILES = [
    "00-cover.jpg", "01-modules.jpg", "02-flow.jpg", "03-kit.jpg",
    "04-exception.jpg", "05-review.jpg", "06-progress.jpg",
    "07-schedule.jpg", "08-wage.jpg",
]
TITLES = [
    "封面", "八种能力", "助手怎么跑", "01 齐套欠料", "02 异常催办",
    "03 报工复核", "04 进度追溯", "05 排产建议", "06 计件对账",
]

COPY = """分享一套落地制造业企业的Agent架构（车间小工单 × 金蝶版）。

系统解决「看得见」；岗位助手解决中间「谁去干」这一段——
齐套欠料、异常催办、报工复核、进度追溯、排产建议、计件对账。

金蝶管订单物料库存，小工单管报工派工工资；MCP 取数。
低风险出清单催办；高风险金蝶最多存草稿，提交审核仍由人点。

资料要齐。想看演示扫二维码加我：金蝶云开发 老胡"""


def load_font(size, bold=False):
    name = "msyhbd.ttc" if bold else "msyh.ttc"
    paths = [
        os.path.join(os.environ.get("WINDIR", "C:\\Windows"), "Fonts", name),
        os.path.join(os.environ.get("WINDIR", "C:\\Windows"), "Fonts",
                     "msyhbd.ttc" if bold else "msyh.ttc"),
    ]
    for p in paths:
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def crop_qr():
    """从微信二维码名片中裁出二维码主体（正方形，留白边）"""
    im = Image.open(QR_SRC).convert("RGB")
    w, h = im.size  # 907x1242
    # 二维码大约位于 x 17%~84%, y 27%~79%（按名片版式估算）
    box = (int(w * 0.16), int(h * 0.26), int(w * 0.85), int(h * 0.80))
    qr = im.crop(box)
    side = min(qr.size)
    qr = ImageOps.fit(qr, (side, side), centering=(0.5, 0.5))
    return qr


def rounded(draw, xy, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)


def make_cover(cover_path, qr):
    cover = Image.open(cover_path).convert("RGB")
    W, H = cover.size  # 864x1152
    d = ImageDraw.Draw(cover)

    # ---- 二维码卡片：居中放在封面中部空白区 ----
    card_w, card_h = 640, 250
    cx, cy = (W - card_w) // 2, int(H * 0.415)  # 标题与胶囊之间
    # 阴影
    shadow = Image.new("RGBA", (card_w + 40, card_h + 40), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle([20, 24, card_w + 20, card_h + 24], radius=28, fill=(17, 19, 24, 60))
    cover.paste(Image.alpha_composite(
        Image.new("RGBA", shadow.size, (0, 0, 0, 0)), shadow
    ).crop((0, 0, shadow.size[0], shadow.size[1])), (cx - 20, cy - 20), shadow)
    d = ImageDraw.Draw(cover)
    # 卡片本体
    rounded(d, [cx, cy, cx + card_w, cy + card_h], radius=28,
            fill=(255, 255, 255), outline=(228, 225, 245), width=3)

    # 二维码（右侧）
    qr_side = 190
    qx = cx + card_w - qr_side - 30
    qy = cy + (card_h - qr_side) // 2
    qr_r = qr.resize((qr_side, qr_side), Image.LANCZOS)
    cover.paste(qr_r, (qx, qy))
    d.rounded_rectangle([qx - 2, qy - 2, qx + qr_side + 1, qy + qr_side + 1],
                        radius=8, outline=(230, 230, 235), width=2)
    d = ImageDraw.Draw(cover)

    # 左侧文字
    tx = cx + 44
    f1 = load_font(46, bold=True)
    f2 = load_font(26)
    d.text((tx, cy + 52), "扫码加老胡", font=f1, fill=(17, 19, 24))
    d.text((tx, cy + 122), "金蝶云开发 · 演示与落地交流", font=f2, fill=(120, 127, 140))
    d.text((tx, cy + 162), "有问题直接找我", font=f2, fill=(124, 58, 237))

    return cover


def main():
    os.makedirs(OUT, exist_ok=True)
    qr = crop_qr()

    for idx, (fname, title) in enumerate(zip(FILES, TITLES), start=1):
        src = os.path.join(POSTERS, fname)
        if idx == 1:
            im = make_cover(src, qr)
        else:
            im = Image.open(src).convert("RGB")
        out_path = os.path.join(OUT, f"{idx}.jpg")
        im.save(out_path, "JPEG", quality=92)
        print(f"[ok] {idx}.jpg <- {fname} ({title})")

    with open(os.path.join(OUT, "文案.txt"), "w", encoding="utf-8") as f:
        f.write(COPY + "\n")
    print("[ok] 文案.txt")
    print("完成，输出目录:", OUT)


if __name__ == "__main__":
    main()
