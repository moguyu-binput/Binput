# -*- coding: utf-8 -*-
"""从 2.1.28 那三份脚本生成 2.1.29 的(机械替换 + 换掉说明段落)。

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
src, eol = load(T("make_dist_2128.py"))

NEW_HEAD = [
    '#!/usr/bin/env python',
    '# -*- coding: utf-8 -*-',
    '"""组装 柄输 2.1.29 发布包(精简版 / 完整版)。',
    '',
    '从 _pub_2129_x64 / _pub_2129_sc 取编译产物(exe + dict.txt + skin\\\\),',
    '配上文档, 产出两个可直接压缩的文件夹。不带 .pdb。',
    '',
    '2.1.29 = 上一版新加的「平移」动效改了三处手感, 别的都没动。',
    '',
    '一、不再淡出淡入, 改成实心滑: 两页全程不透明。旧页是滑到页面外才看不见的,',
    '    新页一直都在、只是刚才不在画面里。以前中间有一段两层半透明叠着走,',
    '    接缝处糊成一团, 看着像"溶解"而不是"滑页"。',
    '',
    '二、滑出页面的部分直接消失: 把页面背景当手机显示屏, 滑出这一块就不画了。',
    '    这个"屏幕框"只在演的这 280 毫秒里临时挂上, 演完立刻撤。',
    '',
    '三、速度曲线换成慢起 -> 加速 -> 减速 -> 停。以前那条起步那一瞬最快, 用在',
    '    短距离浮起落下是对的, 可平移要走整整一页宽, 起步就窜出去。整段仍是 280 毫秒。',
    '"""',
]

NOTE40 = [
    '40. 2.1.29 「平移」动效改了三处手感',
    '    · **不再淡出淡入**: 两页全程实心。旧页滑到页面外才看不见, 新页一直都在、',
    '      只是刚才不在画面里 —— 跟手机上左右翻桌面一个意思。以前中间有一段两层',
    '      半透明叠着走, 接缝糊成一团, 看着像"溶解"。',
    '    · **滑出页面就消失**: 把页面背景当手机显示屏, 滑出这一块就不画了(以前旧页',
    '      会一路滑到卡片外面的留白上还看得见)。屏幕框只在演的那 280 毫秒里临时挂,',
    '      演完立刻撤 —— 默认 / 旋转那两套不受影响。',
    '    · **速度曲线换成慢起 -> 加速 -> 减速 -> 停**: 两头都稳, 像手划屏幕。整段',
    '      时长仍是 280 毫秒。',
    '    · 只动了「平移」这一套: 默认 / 旋转一个数都没改, 不选平移跟 2.1.28 一样。',
    '',
]

body = src.replace("\r\n", "\n").split("\n")
end = body.index('"""', 1) + 1
body = NEW_HEAD + body[end:]
out = "\n".join(body)

out = out.replace('VER = "2.1.28"', 'VER = "2.1.29"')
out = out.replace('"_pub_2128_x64", "柄输-2.1.28-精简版"', '"_pub_2129_x64", "柄输-2.1.29-精简版"')
out = out.replace('"_pub_2128_sc", "柄输-2.1.28-完整版"', '"_pub_2129_sc", "柄输-2.1.29-完整版"')

mark = "39. 2.1.28 换页动效多了一种「平移」"
if mark not in out:
    raise SystemExit("没找到 NOTES 里第 39 条, 结构变了?")
out = out.replace(mark, eol.join(NOTE40) + mark, 1)
save(T("make_dist_2129.py"), out, eol)

# ---------- 2. smoke ----------
src, eol = load(T("smoke_2128.py"))
out = src.replace("柄输-2.1.28-精简版", "柄输-2.1.29-精简版")
out = out.replace("柄输-2.1.28-完整版", "柄输-2.1.29-完整版")
out = out.replace("smoke_2128.py", "smoke_2129.py")
save(T("smoke_2129.py"), out, eol)

# ---------- 3. vercheck ----------
src, eol = load(T("_vercheck_2128.py"))
out = src.replace('WANT_COMMIT = "c052a8c"', 'WANT_COMMIT = "4f65da4"')
out = out.replace("柄输-2.1.28-精简版", "柄输-2.1.29-精简版")
out = out.replace("柄输-2.1.28-完整版", "柄输-2.1.29-完整版")
save(T("_vercheck_2129.py"), out, eol)
print("done")
