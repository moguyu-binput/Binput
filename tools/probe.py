"""数值探针: 图像无法直接看时, 用 alpha/亮度直方图判断玻璃/描边是否画上去了。"""
import sys, numpy as np
from PIL import Image

p = sys.argv[1]
im = Image.open(p).convert("RGBA")
a = np.asarray(im).astype(np.int16)
al = a[..., 3]
print(f"{p}  {im.size[0]}x{im.size[1]}")
print("  不透明像素占比: %.1f%%" % (100.0 * (al > 8).sum() / al.size))
# alpha 直方图(只看有内容的)
nz = al[al > 8]
if nz.size:
    bins = [0,20,40,60,90,120,160,200,240,256]
    h,_ = np.histogram(nz, bins=bins)
    print("  alpha 分布: " + "  ".join(f"{bins[i]}-{bins[i+1]}:{h[i]}" for i in range(len(h))))
# 找一块"疑似玻璃键": 取图像中部 60% 区域内 alpha 的中位数
h,w = al.shape
cy0,cy1 = int(h*0.25), int(h*0.75)
cx0,cx1 = int(w*0.25), int(w*0.75)
sub = al[cy0:cy1, cx0:cx1]
print("  中部区域 alpha: 中位 %d / 均值 %.1f / 最大 %d" % (np.median(sub), sub.mean(), sub.max()))
