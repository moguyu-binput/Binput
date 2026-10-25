r"""滑行记录体检 —— 按门槛档位分组算成功率。

用法:  python tools/slide_stats.py
       python tools/slide_stats.py path\to\strokes.txt

门槛改成手动档位之后(六档试完又往两头各扩了几档), 每段记录末尾会多一栏
dwell=(滑这一道时用的是哪一档)。
这个脚本就按它分组, 好回答"哪一档最好"这个问题 —— 没有那一栏, 换挡试下来的
数据会混成一锅, 根本分不出哪条是哪档滑的。

"成功" = 没按 X 退格 **而且** 真的把拼音发出去过(按 RB / 上屏 / 网格里挑中)。
这是 2.0.0-beta2 之后的新判据: 以前滑完扔下不管的也算成功, 因为 ref 就是你
自己滑出来的那串, 两边永远相等, 等于没判。
"""
import os
import re
import sys
from collections import defaultdict

sys.stdout.reconfigure(encoding="utf-8")

PATH = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.environ.get("LOCALAPPDATA", ""), "GamepadT9", "strokes.txt")

if not os.path.exists(PATH):
    print("找不到记录文件:", PATH)
    sys.exit(1)

blocks = []
cur = None
for line in open(PATH, encoding="utf-8", errors="replace"):
    s = line.strip()
    if not s or s[0] == "#":
        continue
    if s.endswith("begin"):
        cur = {"keeps": [], "drops": [], "seq": None, "ref": None,
               "dwell": None, "ms": 0, "ts": s[:19]}
        continue
    if "seq=" in s:
        if cur is None:
            continue
        cur["seq"] = re.search(r"seq=(\S+)", s).group(1)
        cur["ref"] = re.search(r"ref=(\S+)", s).group(1)
        m = re.search(r"dwell=(\d+)", s)
        cur["dwell"] = int(m.group(1)) if m else None
        m = re.search(r"ms=(\d+)", s)
        cur["ms"] = int(m.group(1)) if m else 0
        blocks.append(cur)
        cur = None
        continue
    if cur is None:
        continue
    m = re.search(r"dwell=(\d+)", s)
    if not m:
        continue
    v = int(m.group(1))
    if s.endswith("KEEP"):
        cur["keeps"].append(v)
    elif s.endswith("DROP"):
        cur["drops"].append(v)
    # FREE 不参与统计: 它是松手兜底白送的, 驻留可能只有三四十毫秒


def pct(a, q):
    a = sorted(a)
    if not a:
        return 0
    return a[min(len(a) - 1, int(len(a) * q))]


print("记录文件:", PATH)
print("总段数: %d\n" % len(blocks))

groups = defaultdict(list)
for b in blocks:
    groups[b["dwell"] if b["dwell"] is not None else "旧"].append(b)


def row(b):
    ok = b["ref"] not in (None, "", "-") and b["ref"] == b["seq"]
    return ok


head = "%-8s %6s %6s %8s %10s %10s %10s %10s" % (
    "门槛", "段数", "成功", "成功率", "KEEP中位", "KEEP最短", "DROP最长", "≥420ms")
print(head)
print("-" * len(head))

rows = []
for key in sorted(groups, key=lambda k: (k == "旧", k)):
    g = groups[key]
    ok = sum(1 for b in g if row(b))
    ks = [v for b in g for v in b["keeps"]]
    ds = [v for b in g for v in b["drops"]]
    over = sum(1 for v in ks if v >= 420)
    name = f"{key}ms" if key != "旧" else "未记档(旧)"
    print("%-8s %6d %6d %7.0f%% %10d %10d %10d %9.0f%%" % (
        name, len(g), ok, 100 * ok / len(g),
        pct(ks, .5) if ks else 0, min(ks) if ks else 0,
        max(ds) if ds else 0, 100 * over / len(ks) if ks else 0))
    if key != "旧":
        rows.append((key, len(g), ok, ks, ds))

print()
print("· \"未记档(旧)\" 是门槛改档之前攒下的老数据 —— 那时候记录里还没写 dwell 栏,")
print("  分不出是哪一档滑的, 只能单独放着。想看干净的结果就把 strokes.txt 删掉重攒。")
print("· ≥420ms 那一列是会被**连发**再记一次的停留占比(连发间隔写死 420ms, 这版没动)。")
print("  门槛调高之后第一发变晚、第二发跟着变晚, 这一列会自己降下来。")

# 各档横向比一比
if len(rows) >= 2:
    print("\n== 各档横比 ==")
    best = max(rows, key=lambda r: (r[2] / r[1] if r[1] else 0, -r[1]))
    for key, n, ok, ks, ds in rows:
        rate = 100 * ok / n if n else 0
        mark = "  <- 目前最好" if key == best[0] else ""
        print("  %4dms  %2d 段 / %2d 成功 = %3.0f%%%s" % (key, n, ok, rate, mark))
    tot = sum(r[2] for r in rows)
    if tot < 8:
        print("\n  (每档才几条, 说明不了什么 —— 同一档至少滑十来道再来看)")
