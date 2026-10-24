"""把 skin-lab 的四页截图合成到模拟的 FF14 画面上, 亮/暗各一张。

原因: 玻璃和浮字都是半透明的, 单独摆在透明底上根本看不出效果 ——
只有压在真实画面上才知道"提亮会不会糊"、"白字会不会看不见"。
用法: python compose_sheet.py <prefix> <outdir>
"""
import os
import sys
import numpy as np
from PIL import Image, ImageFilter

SHOTS = r"C:\Users\<你>\Binput\tools\shots"

PAGES = ["front", "wheel", "en26", "back"]


def grad(h, w, top, bot):
    """垂直线性渐变。"""
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    t = np.repeat(t, w, axis=1)
    out = np.zeros((h, w, 3), np.float32)
    for i in range(3):
        out[..., i] = top[i] * (1 - t) + bot[i] * t
    return out


def blob(arr, cx, cy, r, color, strength):
    """加一团柔光, 模拟场景灯光/太阳。"""
    h, w, _ = arr.shape
    ys = np.arange(h, dtype=np.float32)[:, None]
    xs = np.arange(w, dtype=np.float32)[None, :]
    d2 = ((xs - cx) ** 2 + (ys - cy) ** 2) / (r * r)
    g = np.exp(-d2).astype(np.float32)
    for i in range(3):
        arr[..., i] += (color[i] - arr[..., i]) * g * strength
    return arr


def backdrop(w, h, dark):
    """合成一张"像游戏画面"的背景: 暗=地下城, 亮=晴天野外。"""
    if dark:
        a = grad(h, w, (10, 14, 24), (30, 40, 62))
        a = blob(a, w * 0.30, h * 0.22, min(w, h) * 0.45, (90, 120, 180), 0.30)
        a = blob(a, w * 0.80, h * 0.70, min(w, h) * 0.38, (120, 80, 60), 0.18)
    else:
        a = grad(h, w, (120, 178, 226), (206, 220, 232))
        # 下半部地面: 草绿到土黄
        a[int(h * 0.52):] = grad(h - int(h * 0.52), w, (108, 148, 78), (150, 140, 86))
        a = blob(a, w * 0.72, h * 0.18, min(w, h) * 0.30, (255, 248, 220), 0.45)
    # 暗角, 让中间的输入法面板更聚焦
    ys = np.linspace(-1, 1, h, dtype=np.float32)[:, None]
    xs = np.linspace(-1, 1, w, dtype=np.float32)[None, :]
    vig = 1.0 - 0.30 * np.clip((xs * xs + ys * ys) / 1.6, 0, 1)
    a *= vig[..., None]
    # 轻微噪点, 免得背景太干净像色卡
    rng = np.random.default_rng(7)
    a += rng.normal(0, 2.2, a.shape).astype(np.float32)
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB")
    return img.filter(ImageFilter.GaussianBlur(0.6)).convert("RGBA")


def build(prefix, dark):
    ims = []
    for p in PAGES:
        f = os.path.join(SHOTS, f"{prefix}_{p}.png")
        if not os.path.exists(f):
            print("缺:", f)
            continue
        ims.append(Image.open(f).convert("RGBA"))
    if not ims:
        return None
    # 统一高度缩放 + 每页左右留 34px 间隙
    H = 420
    GAP = 34
    scaled = []
    for im in ims:
        h = im.size[1]
        nw = max(1, int(im.size[0] * H / h))
        scaled.append(im.resize((nw, H), Image.LANCZOS))
    W = sum(i.size[0] for i in scaled) + GAP * (len(scaled) + 1)
    sheet = Image.new("RGBA", (W, H + GAP * 2), (0, 0, 0, 0))
    x = GAP
    for im in scaled:
        sheet.alpha_composite(im, (x, GAP))
        x += im.size[0] + GAP
    bg = backdrop(W, H + GAP * 2, dark)
    bg.alpha_composite(sheet)
    return bg


if __name__ == "__main__":
    pref = sys.argv[1] if len(sys.argv) > 1 else "glass"
    out = sys.argv[2] if len(sys.argv) > 2 else SHOTS
    os.makedirs(out, exist_ok=True)
    for d, name in ((True, "dark"), (False, "light")):
        im = build(pref, d)
        if im is None:
            continue
        p = os.path.join(out, f"sheet_{pref}_{name}.png")
        im.save(p)
        print(p, im.size)
