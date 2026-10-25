# -*- coding: utf-8 -*-
"""从 2.1.29 那三份脚本生成 2.1.30 的(机械替换 + 换掉说明段落)。

换行照抄源文件: 检测出它是 \\r\\n 还是 \\n, 插进去的新段落用同一套。
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
T = lambda n: os.path.join(ROOT, "tools", n)


def load(p):
    raw = open(p, "rb").read().decode("utf-8")
    eol = "\r\n" if ("\r\n" in raw or "\r\r\n" in raw) else "\n"
    return raw.replace("\r\r\n", "\n").replace("\r\n", "\n"), eol


def save(p, s, eol):
    open(p, "wb").write(s.replace("\r\n", "\n").replace("\n", eol).encode("utf-8"))
    print("  -> %s" % os.path.basename(p))


# ---------- 1. make_dist ----------
src, eol = load(T("make_dist_2129.py"))

NEW_HEAD = [
    '#!/usr/bin/env python',
    '# -*- coding: utf-8 -*-',
    '"""组装 柄输 2.1.30 发布包(精简版 / 完整版)。',
    '',
    '从 _pub_2130_x64 / _pub_2130_sc 取编译产物(exe + dict.txt + skin\\\\),',
    '配上文档, 产出两个可直接压缩的文件夹。不带 .pdb。',
    '',
    '2.1.30 = 「平移」那一套修了三个毛病, 都是翻页翻得快才看得出来的。',
    '',
    '一、旧页不再带模糊、也不再压暗: 那两层是给"页面化掉"那两套做景深用的,',
    '    平移的旧页是滑出屏幕的实心板子, 不该糊也不该暗。演完还回去(另外两套照旧)。',
    '',
    '二、滑出页面的部分是真的消失了: 上一版挂的是父容器的边(比页面宽出一圈),',
    '    页面滑出去还能在那一圈里被看见。现在改成显式的矩形 —— 就是页面这一块。',
    '',
    '三、连着翻页改成"接着滑": 按键一次不吞、内容每次立刻生效, 新的一页从它此刻',
    '    站的地方接着滑到位(时长按剩余距离缩短), 不再被拽回框外从头冲进来。',
    '"""',
]

NOTE41 = [
    '41. 2.1.30 「平移」那一套修了三个毛病(都是翻得快才看得出来)',
    '    · **旧页不带模糊、不压暗了**: 那两层(高斯模糊 + 压暗)是 1.x 给"页面化掉"',
    '      那两套做景深用的。平移的旧页是滑出屏幕的实心板子, 不该糊也不该暗 ——',
    '      演平移的这一场摘掉, 演完还回去, 默认 / 旋转照旧。',
    '    · **滑出页面是真的消失了**: 上一版挂的是父容器的边, 而父容器比页面宽出一圈,',
    '      页面滑出去还能在那一圈留白里被看见。现在显式画一个矩形 —— 就是页面这一块。',
    '    · **连着翻页改成"接着滑"**: 按键一次不吞、内容每次立刻生效; 动画不再从头',
    '      重演, 新的一页从**此刻站的地方**接着滑到位(时长按剩余距离缩短)。以前页一多、',
    '      连着翻会看见"下一页先在框外露出一大截, 再滑进来"。',
    '    · 连着翻也不再重复拍整页快照(又贵又花, 而且那一刻真身带着位移, 拍下来是',
    '      错开一截的): 内容照样换、进场照样演, 只是没有旧页陪着走那一程。',
    '    · 只动了「平移」这一套: 默认 / 旋转一个数都没改, 不选平移跟 2.1.29 一样。',
    '',
]

body = src.replace("\r\n", "\n").split("\n")
end = body.index('"""', 1) + 1
body = NEW_HEAD + body[end:]
out = "\n".join(body)

out = out.replace('VER = "2.1.29"', 'VER = "2.1.30"')
out = out.replace('"_pub_2129_x64", "柄输-2.1.29-精简版"', '"_pub_2130_x64", "柄输-2.1.30-精简版"')
out = out.replace('"_pub_2129_sc", "柄输-2.1.29-完整版"', '"_pub_2130_sc", "柄输-2.1.30-完整版"')

mark = "40. 2.1.29 「平移」动效改了三处手感"
if mark not in out:
    raise SystemExit("没找到 NOTES 里第 40 条, 结构变了?")
out = out.replace(mark, eol.join(NOTE41) + mark, 1)
save(T("make_dist_2130.py"), out, eol)

# ---------- 2. smoke ----------
src, eol = load(T("smoke_2129.py"))
out = src.replace("柄输-2.1.29-精简版", "柄输-2.1.30-精简版")
out = out.replace("柄输-2.1.29-完整版", "柄输-2.1.30-完整版")
out = out.replace("smoke_2129.py", "smoke_2130.py")
save(T("smoke_2130.py"), out, eol)

# ---------- 3. vercheck ----------
src, eol = load(T("_vercheck_2129.py"))
out = out.replace("柄输-2.1.29-精简版", "柄输-2.1.30-精简版")
src2 = src.replace("柄输-2.1.29-精简版", "柄输-2.1.30-精简版")
src2 = src2.replace("柄输-2.1.29-完整版", "柄输-2.1.30-完整版")
save(T("_vercheck_2130.py"), src2, eol)
print("done")
