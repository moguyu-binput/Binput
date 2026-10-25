"""量 1.0.8 按下缩小: 从每行/每列的"非窗口底"跨度量出键的 bbox。
窗口底是 #141420 一类的深色, 键(不管选中还是没选中)都比它亮, 所以按亮度阈值切。"""
import struct, zlib, sys


def read_png(p):
    d = open(p, 'rb').read()
    pos, idat, w, h, ct = 8, b'', 0, 0, 0
    while pos < len(d):
        ln = struct.unpack('>I', d[pos:pos+4])[0]
        typ = d[pos+4:pos+8]
        if typ == b'IHDR':
            w, h, _bd, ct = struct.unpack('>IIBB', d[pos+8:pos+18])
        elif typ == b'IDAT':
            idat += d[pos+8:pos+8+ln]
        pos += 12 + ln
    raw = zlib.decompress(idat)
    nch = {0: 1, 2: 3, 4: 2, 6: 4}[ct]
    stride = w * nch
    out = bytearray(h * stride)
    prev = bytearray(stride)
    i = 0
    for y in range(h):
        f = raw[i]; i += 1
        line = bytearray(raw[i:i+stride]); i += stride
        if f:
            for x in range(stride):
                a = line[x-nch] if x >= nch else 0
                b = prev[x]
                c = prev[x-nch] if x >= nch else 0
                if f == 1: line[x] = (line[x] + a) & 255
                elif f == 2: line[x] = (line[x] + b) & 255
                elif f == 3: line[x] = (line[x] + (a+b)//2) & 255
                else:
                    pa, pb, pc = abs(b-c), abs(a-c), abs(a+b-2*c)
                    pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                    line[x] = (line[x] + pr) & 255
        out[y*stride:(y+1)*stride] = line
        prev = line
    return w, h, nch, out


def lum(w, nch, buf, x, y):
    o = (y*w + x)*nch
    return 0.299*buf[o] + 0.587*buf[o+1] + 0.114*buf[o+2]


def span_h(w, h, nch, buf, y, x0, x1, thr):
    lo = None
    for x in range(x0, x1+1):
        if lum(w, nch, buf, x, y) > thr:
            if lo is None: lo = x
            hi = x
    return (lo, hi) if lo is not None else None


def span_v(w, h, nch, buf, x, y0, y1, thr):
    lo = None
    for y in range(y0, y1+1):
        if lum(w, nch, buf, x, y) > thr:
            if lo is None: lo = y
            hi = y
    return (lo, hi) if lo is not None else None


def measure(path, boxes, thr=30):
    w, h, nch, buf = read_png(path)
    print(f"--- {path}  ({w}x{h}) ---")
    for cx, cy, x0, x1, y0, y1, label in boxes:
        # 键所在的那一格内扫, 避开上下相邻键
        hs = span_h(w, h, nch, buf, cy - 25, x0, x1, thr)
        vs = span_v(w, h, nch, buf, cx - 25, y0, y1, thr)
        if not hs or not vs:
            print(f"  {label}: 没扫到")
            continue
        print(f"  {label}: {hs[1]-hs[0]+1} x {vs[1]-vs[0]+1}")


if __name__ == "__main__":
    measure("shots/v108_press9.png", [
        (196, 487, 137, 255, 440, 535, "TUV (按下)"),
        (78,  487,  20, 135, 440, 535, "PQRS(没按)"),
        (313, 487, 258, 375, 440, 535, "WXYZ(没按)"),
    ])


def measure_sel(path, cy, x0, x1, y0, y1, label):
    """选中键是蓝底, 用蓝通道切最准(圆角抗锯齿不会把边界拉出去太多)。"""
    w, h, nch, buf = read_png(path)
    def isblue(x, y):
        o = (y*w + x)*nch
        r, g, b = buf[o], buf[o+1], buf[o+2]
        return b > 70 and b > r + 25
    lo = hi = None
    for x in range(x0, x1+1):
        if isblue(x, cy):
            if lo is None: lo = x
            hi = x
    vo = vh = None
    for y in range(y0, y1+1):
        if isblue((lo+hi)//2 if lo is not None else x0, y):
            if vo is None: vo = y
            vh = y
    print(f"  {label}: 蓝底 {hi-lo+1 if lo else 0} x {vh-vo+1 if vo else 0}")
    return (hi-lo+1 if lo else 0, vh-vo+1 if vo else 0)


print()
a = measure_sel("shots/v108_press9.png", 487, 137, 255, 435, 540, "动态开+按住 (应=常规*0.88)")
b = measure_sel("shots/v108_press9_nofx.png", 487, 137, 255, 435, 540, "动态关+按住 (应=常规*1.0, 只压暗)")
if a[0] and b[0]:
    print(f"  -> 按下/常规 宽比 = {a[0]/b[0]:.3f}  高比 = {a[1]/b[1]:.3f}   (代码里 PRESS_SCALE=0.88)")
