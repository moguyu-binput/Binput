#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
生成 GamepadT9 的**带词频**拼音表 dict.txt。

格式(每行一条, tab 分隔, 兼容旧的"只有拼音键"格式):
    拼音串<TAB>词频        例:  shoudao\t1234
    拼音串                 例:  shoudao      (没有 tab 就按默认频算)

为什么要词频: 1.0.2 之前只要"这串拼音成词"就一律 +1000, 词频完全不参与排序,
于是 7443 里 四个(15981) 会被 七个(2105) 压在后面 —— 平票时只能靠音节热度决胜负,
和真实常用度对不上。把词频接回去之后, 成词加分才分得出高下。

一个拼音键对应多个汉字词(是的 / 使得 都是 shide), 这里取**最高频那条**:
代表"这个读音最常出现的那个词", 也不会被同音词的数量带偏。

数据来源:
  1. Rime 八股文词表  https://github.com/rime/rime-essay        —— 汉语词 + 词频
  2. pypinyin                                                   —— 词 -> 无声调拼音

用法:
    python tools/build_dict_freq.py                 # 默认取词频最高的 6 万条
    python tools/build_dict_freq.py --top 100000
    python tools/build_dict_freq.py --out 路径
"""
import argparse
import os
import re
import sys
import time

try:
    from pypinyin import lazy_pinyin, Style
except ImportError:
    sys.exit("缺少 pypinyin, 先执行:  pip install pypinyin")

try:
    from opencc import OpenCC
    _cc = OpenCC("t2s")
except ImportError:
    sys.exit("缺少 opencc, 先执行:  pip install opencc-python-reimplemented")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ESSAY = os.path.join(ROOT, "data", "essay.txt")
DEFAULT_OUT = os.path.join(ROOT, "skin-lab", "GamepadT9-Game", "dict.txt")
ENGINE = os.path.join(ROOT, "skin-lab", "GamepadT9-Game", "T9Engine.cs")

HANZI = re.compile(r"^[\u4e00-\u9fff]{1,5}$")


def load_syllables():
    src = open(ENGINE, encoding="utf-8").read()
    m = re.search(r'const string SYLLABLES\s*=\s*"([^"]+)"', src)
    if not m:
        sys.exit("无法从 T9Engine.cs 提取音节表")
    return set(m.group(1).split())


SYLLABLES = load_syllables()


def load_words(topn):
    """读 essay.txt, 按词频降序返回 [(简体词, 词频)]"""
    best = {}
    with open(ESSAY, encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 2:
                continue
            word = _cc.convert(parts[0])
            try:
                freq = int(parts[1])
            except ValueError:
                continue
            if not HANZI.match(word):
                continue
            if best.get(word, -1) < freq:
                best[word] = freq
    return sorted(best.items(), key=lambda kv: -kv[1])[:topn]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--top", type=int, default=60000)
    ap.add_argument("--out", default=DEFAULT_OUT)
    args = ap.parse_args()

    if not os.path.exists(ESSAY):
        sys.exit(f"缺少 {ESSAY}")

    print(f"合法音节 {len(SYLLABLES)} 个")
    t0 = time.time()
    words = load_words(args.top)
    print(f"读入 {len(words)} 条词 (耗时 {time.time() - t0:.1f}s), 开始转拼音...")

    # 拼音键 -> 最高词频
    keys = {}
    skipped = 0
    t0 = time.time()
    for i, (word, freq) in enumerate(words):
        syls = lazy_pinyin(word, style=Style.NORMAL)
        if any(s not in SYLLABLES for s in syls):
            skipped += 1
            continue
        k = "".join(syls)
        if keys.get(k, 0) < freq:
            keys[k] = freq
        if i and i % 10000 == 0:
            print(f"  {i}/{len(words)} ...", flush=True)
    print(f"转拼音完成 (耗时 {time.time() - t0:.1f}s), 跳过 {skipped} 条读音异常的词")

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write("# GamepadT9 拼音表(带词频): 每行 \"拼音串<TAB>词频\"\n")
        f.write("# 词频取自 rime/rime-essay, 同音词取最高频那条。旧格式(只有拼音键)照样能读。\n")
        f.write("# 汉字由系统输入法出, 这里不需要汉字。共 %d 条\n" % len(keys))
        for k in sorted(keys):
            f.write(f"{k}\t{keys[k]}\n")

    size = os.path.getsize(args.out) / 1024
    print(f"写出 {args.out}")
    print(f"  {len(keys)} 条拼音, {size:.0f} KB")

    for probe in ("shide", "qige", "sige", "ni", "nihao", "xiexie", "wo",
                  "women", "hao", "zhang", "shoudao", "zhidao"):
        v = keys.get(probe)
        print(f"  {probe:<10} -> {v if v else '(无)'}")


if __name__ == "__main__":
    main()
