#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
生成 GamepadT9 的拼音表 dict.txt (格式: 每行一个成词的拼音串, 如 "shoudao")

现在汉字、词组、联想**全部交给系统输入法**, 本程序只负责把数字切成拼音并按
"像不像一句人话"排序。所以词库里只需要**拼音键**(这串读音成不成词),
汉字一个都用不上 —— 早期版本的 "pinyin=词1,词2" 格式已经废弃。

数据来源:
  1. Rime 八股文词表  https://github.com/rime/rime-essay        —— 汉语词 + 词频
  2. pypinyin         pip install pypinyin                      —— 词 -> 无声调拼音

essay.txt 提供"哪些词是真词", pypinyin 把它们翻成拼音键。
词频现在只用来决定"取前多少条", 不再写进文件。

用法:
    python tools/build_dict.py              # 默认取词频最高的 6 万条
    python tools/build_dict.py --top 100000 # 更多(生成更慢, 拼音表更大)
"""
import argparse
import os
import re
import sys
import time
import unicodedata

try:
    from pypinyin import lazy_pinyin, Style
except ImportError:
    sys.exit("缺少 pypinyin, 先执行:  pip install pypinyin")

# Rime 八股文词表是繁体, 必须转简体
try:
    from opencc import OpenCC
    _cc = OpenCC("t2s")
except ImportError:
    sys.exit("缺少 opencc, 先执行:  pip install opencc-python-reimplemented")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ESSAY = os.path.join(ROOT, "data", "essay.txt")
OUT = os.path.join(ROOT, "GamepadT9-Game", "dict.txt")
ENGINE = os.path.join(ROOT, "GamepadT9", "T9Engine.cs")

# 只收 1~5 个汉字的词; 5 字以上基本是短语, 九键输入用不上
HANZI = re.compile(r"^[\u4e00-\u9fff]{1,5}$")


def load_syllables():
    """直接从 T9Engine.cs 里取音节表, 保证和切分算法用的是同一套"""
    src = open(ENGINE, encoding="utf-8").read()
    m = re.search(r'const string SYLLABLES\s*=\s*"([^"]+)"', src)
    if not m:
        sys.exit("无法从 T9Engine.cs 提取音节表")
    return set(m.group(1).split())


SYLLABLES = load_syllables()


def load_words(topn):
    """读 essay.txt, 按词频降序返回 [(词, 词频)]"""
    best = {}
    with open(ESSAY, encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 2:
                continue
            word = _cc.convert(parts[0])          # 繁 -> 简
            try:
                freq = int(parts[1])
            except ValueError:
                continue
            if not HANZI.match(word):
                continue
            # 繁简合并后可能出现同一个词, 保留词频最高的
            if best.get(word, -1) < freq:
                best[word] = freq
    ordered = sorted(best.items(), key=lambda kv: -kv[1])
    return ordered[:topn]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--top", type=int, default=60000, help="取词频最高的多少条")
    args = ap.parse_args()

    if not os.path.exists(ESSAY):
        sys.exit(f"缺少 {ESSAY}\n"
                 f"下载: curl -L -o data/essay.txt "
                 f"https://raw.githubusercontent.com/rime/rime-essay/master/essay.txt")

    print(f"合法音节 {len(SYLLABLES)} 个")
    t0 = time.time()
    words = load_words(args.top)
    print(f"读入 {len(words)} 条词 (耗时 {time.time() - t0:.1f}s), 开始转拼音...")

    keys = set()
    t0 = time.time()
    skipped = 0
    for i, (word, _freq) in enumerate(words):
        syls = lazy_pinyin(word, style=Style.NORMAL)
        # 读音必须都是合法音节, 否则 T9 编码会出 '0'
        if any(s not in SYLLABLES for s in syls):
            skipped += 1
            continue
        keys.add("".join(syls))
        if i and i % 10000 == 0:
            print(f"  {i}/{len(words)} ...", flush=True)

    print(f"转拼音完成 (耗时 {time.time() - t0:.1f}s), 跳过 {skipped} 条读音异常的词")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("# GamepadT9 拼音表: 每行一个成词的拼音串, 只用来判断切分像不像话\n")
        f.write("# 汉字由系统输入法出, 这里不需要汉字。共 %d 条\n" % len(keys))
        for key in sorted(keys):
            f.write(key + "\n")

    size = os.path.getsize(OUT) / 1024
    print(f"写出 {OUT}")
    print(f"  {len(keys)} 条拼音, {size:.0f} KB")

    # 抽查几个常见输入
    for probe in ("ni", "nihao", "xiexie", "wo", "women", "hao", "zhang", "shoudao"):
        print(f"  {probe:<8} -> {'有' if probe in keys else '(无)'}")


if __name__ == "__main__":
    main()
