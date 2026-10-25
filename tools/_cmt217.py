# -*- coding: utf-8 -*-
"""2.1.7: 把注释里还写在"每类 40 个 / 网格 5 列 8 行"的旧说法改成 24 个 / 5×5。

符号网格从 5×8 压成 5×5(正中间留空), 代码早改完了, 注释还停在旧尺寸 ——
留着会误导后面看代码的人。两个文件都是 CRLF, 用 newline="" 原样读写。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Users\<你>\Binput"

JOBS = [
    ("InputCore.cs", [
        ("    /// 每类都控制在 40 个以内 —— 网格是 5 列 × 8 行, 装不下更多。",
         "    /// 每类固定 24 个 —— 网格是 5 列 × 5 行, 正中间那格留空(摇杆松手的落点)。"),
        ("    /// <summary>一页几格。符号面板 40 个一页装得下, 只有拼音网格会分页。</summary>",
         "    /// <summary>一页几格。符号面板 24 个一页装得下, 只有拼音网格会分页。</summary>"),
    ]),
    ("OverlayWindow.xaml.cs", [
        ("            // 不设固定高度: 40 格排 5 列 8 行, 由 UniformGrid 均分 360×360",
         "            // 不设固定高度: 25 格排 5 列 5 行, 由 UniformGrid 均分 360×360"),
        ("            // **行数也必须钉死**(拼音 5 行 / 符号 8 行): 以前不设行数, 40 格排 3 列",
         "            // **行数也必须钉死**(拼音 / 符号一律 5 行): 以前不设行数, 40 格排 3 列"),
    ]),
]


def main():
    for name, pairs in JOBS:
        p = os.path.join(ROOT, name)
        s = io.open(p, encoding="utf-8", newline="").read()
        assert "\r\n" in s, "%s 不是 CRLF" % name
        assert s.count("\r\r\n") == 0 and s.count("\n") - s.count("\r\n") == 0, \
            "%s 换行被搅了" % name
        for old, new in pairs:
            if old not in s:
                raise SystemExit("!! %s 匹配不上: %r" % (name, old))
            s = s.replace(old, new, 1)
        io.open(p, "w", encoding="utf-8", newline="").write(s)
        print("%-24s 改了 %d 处" % (name, len(pairs)))

    # 复查: 全库不该再有"40 个"这种旧量法(第 423~425 行那段 2.1.7 由来是例外,
    # 它讲的就是"从 40 精简到 24"这件事本身)
    bad = 0
    for name, _ in JOBS:
        s = io.open(os.path.join(ROOT, name), encoding="utf-8", newline="").read()
        for i, ln in enumerate(s.split("\r\n"), 1):
            if "40 个" in ln and "精简到 24" not in ln:
                print("  残留 %s:%d  %s" % (name, i, ln.strip()))
                bad += 1
    print("残留检查: %d 处" % bad)


if __name__ == "__main__":
    main()
