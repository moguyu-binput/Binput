#!/usr/bin/env python3
"""把任意图片转成 Windows 图标文件 Binput.ico(exe 图标 + 托盘角标共用这一个)。

用法:
    python tools/make_icon.py <图片路径>

输出:
    Binput.ico   仓库根目录, 多尺寸(16~256)图标

为什么需要多个尺寸:
    托盘区在不同 DPI / 不同 Windows 版本下会挑不同尺寸来画,
    只给一个 256 的话, 缩到 16 时 Windows 自己缩放会糊成一团。
    这里预先用 LANCZOS 重采样好每个尺寸, 小尺寸也清楚。

注意:
    - 会裁成正方形(居中裁剪), 非正方形图片不会被拉变形
    - 保留透明通道; 原图没有 alpha 的话就保持原样(不自动抠白底)
    - **源图最好是已抠掉背景的透明 PNG**(白底图不会自动变透明,
      混色边还会被 LANCZOS 振铃放大成灰点 —— kill_ringing 只能清掉一部分)
"""

import sys
import os
from PIL import Image, ImageChops

# Windows 图标常用尺寸。托盘实际主要用 16/20/24/32,
# 大尺寸是给资源管理器"大图标"视图和任务栏用的。
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "Binput.ico")


def kill_ringing(f):
    """清掉 LANCZOS 缩小时的振铃碎点。

    半透明边缘外侧会出现"低透明度 + 灰黑色"的像素(白底下看是一个个灰点,
    232x46 这类主色都能被它带出杂色)。判据用**饱和度**: 彩色图标的半透明
    边缘必然带色, 纯灰的只可能是振铃。清完不影响边缘本身的抗锯齿。
    """
    sat = f.convert("RGB").convert("HSV").split()[1]
    keep = sat.point(lambda v: min(255, v * 255 // 48))
    f.putalpha(ImageChops.multiply(f.split()[3], keep))
    return f


def boost_small(f):
    """小尺寸光学校正(只对 <=32 的档做)。

    细线图标(线宽 ~5%)缩到 16/32 时线宽不到 1px, 每个像素都只是部分覆盖,
    整幅发灰(实测 32 档平均 alpha 107、16 档 60)—— 托盘和 Alt+Tab 恰恰
    最常用这两档。做两步: ① alpha 3x3 膨胀(线补回 ~1px 半) ② gamma 0.75
    提透明度。不改形状走向、不改颜色; >=48 的档一律不碰, 保持原图。
    """
    from PIL import ImageFilter
    a = f.split()[3].filter(ImageFilter.MaxFilter(3))
    lut = [min(255, int(round(255.0 * (v / 255.0) ** 0.75))) for v in range(256)]
    f.putalpha(a.point(lut))
    return f


def to_square(img: Image.Image) -> Image.Image:
    """居中裁成正方形, 透明处用原图的透明通道; 不拉伸。"""
    w, h = img.size
    if w == h:
        return img
    side = min(w, h)
    left = (w - side) // 2
    top = (h - side) // 2
    return img.crop((left, top, left + side, top + side))


def write_ico(frames, path):
    """手写 ICO 二进制。

    为什么不用 Pillow 的 save(format="ICO"): 实测 Pillow 12.3 下传 sizes= +
    append_images= 只写出 1 帧(16x16), 其余尺寸全丢。后果是托盘在高 DPI 下
    拿 16 去放大、资源管理器大图标视图更是糊成一团 —— 而日志上完全看不出来。

    每帧用 32bpp BMP(而不是 PNG): PNG 帧要 Vista+ 且部分场景兼容性一般,
    BMP 帧是 ICO 最传统的形式, GDI+ 一定认。代价是文件大(256 那帧约 256KB),
    但图标是嵌进 exe 的资源, 几百 KB 无所谓。
    """
    import struct

    n = len(frames)
    header = struct.pack("<HHH", 0, 1, n)   # reserved, type=1(icon), count

    entries, blobs = [], []
    offset = 6 + 16 * n

    for f in frames:
        w = h = f.width
        rgba = f.convert("RGBA")
        px = rgba.load()

        # XOR 位图: BGRA, 自下而上
        rows = []
        for y in range(h - 1, -1, -1):
            row = bytearray()
            for x in range(w):
                r, g, b, a = px[x, y]
                row += bytes((b, g, r, a))
            rows.append(bytes(row))
        xor = b"".join(rows)

        # AND 掩码: 32bpp 时透明已由 alpha 表达, 掩码全 0, 但每行必须 4 字节对齐
        and_row = ((w + 31) // 32) * 4
        andmask = b"\x00" * (and_row * h)

        # BITMAPINFOHEADER: 高度写 h*2, 因为 XOR 和 AND 是叠着放的两块
        bih = struct.pack("<IiiHHIIiiII",
                          40, w, h * 2, 1, 32, 0,
                          len(xor) + len(andmask), 0, 0, 0, 0)
        blob = bih + xor + andmask

        # 目录项里 256 要写成 0(这个字段只有 1 字节)
        entries.append(struct.pack("<BBBBHHII",
                                   w % 256, h % 256, 0, 0, 1, 32,
                                   len(blob), offset))
        offset += len(blob)
        blobs.append(blob)

    with open(path, "wb") as fp:
        fp.write(header)
        for e in entries:
            fp.write(e)
        for b in blobs:
            fp.write(b)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    src = sys.argv[1]
    if not os.path.exists(src):
        print("找不到文件:", src)
        sys.exit(1)

    img = Image.open(src)
    print("原图: %s  %dx%d  模式=%s" % (os.path.basename(src), img.width, img.height, img.mode))

    # 统一到 RGBA, 保证透明通道存在
    img = img.convert("RGBA")
    img = to_square(img)

    max_side = img.width

    # 判断是不是像素风(颜色数很少 + 硬边缘)。像素风放大要用 NEAREST 保住锐利边缘,
    # 平滑抗锯齿的图用 NEAREST 会出现锯齿, 得用 LANCZOS。
    colors = len(img.getcolors(maxcolors=1 << 20) or [])
    pixel_art = colors <= 64
    up = Image.NEAREST if pixel_art else Image.LANCZOS

    sizes = [s for s in SIZES if s <= max_side]
    upscaled = [s for s in SIZES if s > max_side]

    if not sizes:
        sizes = [16]
        print("警告: 原图只有 %dpx, 太小了, 会强行放大" % max_side)

    if max_side < 64:
        print("警告: 原图仅 %dpx。托盘用的 16/20/24/32 是缩小, 没问题;" % max_side)
        print("       但资源管理器\"大图标\"视图要 64~256, 只能放大, 会偏软。")
    print("识别为: %s (唯一颜色 %d 种)" % ("像素风" if pixel_art else "平滑抗锯齿图", colors))

    frames = []
    for s in sizes:
        f = kill_ringing(img.resize((s, s), Image.LANCZOS))
        if s <= 32:
            f = boost_small(f)
        frames.append(f)
    # 超出原图的大尺寸仍然生成: 没有的话 Windows 会自己拿 32 去拉伸, 比我们给更糊
    for s in upscaled:
        f = kill_ringing(img.resize((s, s), up))
        if s <= 32:
            f = boost_small(f)
        frames.append(f)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    write_ico(frames, OUT)

    print("已生成: %s" % OUT)
    print("包含尺寸: %s" % ", ".join("%dx%d" % (f.width, f.height) for f in frames))
    print("大小: %.1f KB" % (os.path.getsize(OUT) / 1024))

    # 别信 Pillow 的返回值, 自己读回来数一遍帧
    import struct
    with open(OUT, "rb") as f:
        head = f.read(6)
        n = struct.unpack("<H", head[4:6])[0]
    print("回读校验: 实际写入 %d 帧" % n)
    if n != len(frames):
        print("!! 帧数不符, 期望 %d —— 图标是坏的" % len(frames))


if __name__ == "__main__":
    main()
