# -*- coding: utf-8 -*-
# 补两处: QuickMode 改成"扣掉设置面"之后, 这两处要的是 **QuickOpen**
# ("F 开着就别干", 跟有没有被设置盖住无关) —— 否则会出现
# "翻到设置面、按 RT 往游戏里发了个回车" 这种只因为盖住才放出来的动作。
import io, os, sys
sys.stdout.reconfigure(encoding="utf-8")
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "skin-lab", "GamepadT9-Game")
P = os.path.join(SRC, "InputCore.cs")
s = io.open(P, encoding="utf-8", newline="").read()
assert s.count("\n") == s.count("\r\n"), "不是纯 CRLF"

PAIRS = [
    ('''            else if (Layout == Layout.NUM9 && !r3 && !QuickMode)
            {
                TextOutput.Digit(0);
''',
     '''            // 【要的是 QuickOpen, 不是 QuickMode】"F 开着"就不能拿 L3 去出 0 ——
            // 跟设置有没有盖在上面无关, 盖着的时候更不该出。
            else if (Layout == Layout.NUM9 && !r3 && !QuickOpen)
            {
                TextOutput.Digit(0);
''',
     "L3 出 0 的判据"),
    ('''        if (rt && !_prevRT && !QuickMode)
''',
     '''        // 【要的是 QuickOpen, 不是 QuickMode】同上: F 开着就不往外发回车,
        // 不能因为翻到了设置面就把这只键放出来。
        if (rt && !_prevRT && !QuickOpen)
''',
     "RT 回车的判据"),
]
for old, new, tag in PAIRS:
    o = old.replace("\n", "\r\n")
    n = new.replace("\n", "\r\n")
    if s.count(o) != 1:
        raise SystemExit("!! %s 锚点出现 %d 次" % (tag, s.count(o)))
    s = s.replace(o, n, 1)
    print("  ok  " + tag)

assert s.count("\r\r\n") == 0
io.open(P, "w", encoding="utf-8", newline="").write(s)
print("已写 InputCore.cs")

# 复查: 剩下的 QuickMode 都得是"设置盖住时该让位"的那几处
import re
left = [l.strip() for l in s.split("\r\n") if "QuickMode" in l and not l.strip().startswith("//")]
print("\n剩下的 QuickMode 用法:")
for l in left:
    print("   " + l)
