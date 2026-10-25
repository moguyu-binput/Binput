# -*- coding: utf-8 -*-
"""往当天工作日志追加 2.1.20 那一节(日志是 CRLF, 必须用 join 拼)。"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOC = os.path.join(ROOT, ".workbuddy", "memory", "2026-10-10.md")

lines = [
    "",
    "## 2.1.20 —— 展开全部候选时, 拼音行的底别跟着高亮一起收掉",
    "",
    "用户报: 「展开候选拼音的时候, 上面最开始的候选行背景会变透明, 浅色页面看不清」。",
    "",
    "### 定位",
    "- 用户说的「最开始的候选行」就是**拼音行**(PinyinRow): 展开网格时它摆的是",
    "  **最开始那几条**, 网格(GridCand)接着往下摊剩下的 —— 见 `GridOffset`。",
    "  (候选行 CandRow 只在符号模式有内容, 其余时候五格全 Hidden, 所以不可能是它。)",
    "- 拼音行在**卡片外面**, 底下直接压着用户的页面, 格子那层淡底是唯一把字从背景",
    "  里抠出来的东西。",
    "- 根因在 `OverlayWindow.Render.cs`: 「有底」和「高亮」共用同一个开关",
    "  `bool pyDim = inPy && !gridMode;`, 于是展开(收高亮)的那一刻底也一起没了。",
    "  收高亮本身是对的(否则行内最后一条和网格里选中那条同时亮着)。",
    "",
    "### 修法",
    "```",
    "bool pyDim  = inPy;                 // 底: 只要有拼音就一直在",
    "bool pyPick = inPy && !gridMode;    // 高亮: 光标不在网格里才有",
    "bool on     = pyPick && pyStart + i == c.PinyinIdx;",
    "```",
    "「还有更多」那个省略号(more)原本也挂 pyDim, 语义是「网格摊开了就别再挂」,",
    "改用 pyPick —— 与原来的 pyDim 完全等价, 行为不变。",
    "",
    "### 探针",
    "- uiprobe 新增 `PinyinBg()` 一组(4 条): 真窗口 + 真 Render, 直接读那颗格子的",
    "  Background: 没展开 `#FF0C447C` / 展开着 `#FF262230` / 收起后 `#FF0C447C`。",
    "- **故意回退复现过**: 把 pyDim 改回旧写法, 「展开着」变 `#00FFFFFF`, 断言报红 ——",
    "  防线有效, 不是摆设。",
    "- uiprobe 是 ProjectReference, 看不到 `internal`, 所以 Debug* 方法走反射",
    "  (`GetMethod(..., NonPublic)`); static 字段(Active)要带 `BindingFlags.Static`。",
    "",
    "### 出包",
    "commit `3c3c79e`(代码) + `6fbd3c2`(文档) + `71ef0de`(make_dist 回写的版本号标题),",
    "tag `v2.1.20`。主程序 0 警告 0 错误; 八套探针全绿; 两包冒烟全 True",
    "(726×752 / dict 46389 / 无异常); ProductVersion=1.0.0+6fbd3c2 ✓。",
    "exe SHA256: 精简 `f9c08445…`、完整 `3ae28e73…`。",
]

raw = open(DOC, "rb").read().decode("utf-8")
assert raw.endswith("\r\n")
open(DOC, "wb").write((raw + "\r\n".join(lines) + "\r\n").encode("utf-8"))
print("已追加", len(lines), "行")
