# -*- coding: utf-8 -*-
"""从 2.1.26 那三份脚本生成 2.1.28 的(机械替换 + 换掉说明段落)。

换行照抄源文件: 检测出它是 \\r\\n 还是 \\n, 插进去的新段落用同一套。
2.1.26 那几份脚本自己就是 \\r\\r\\n 的(老毛病), 生成时先摊平再写干净 CRLF。
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
src, eol = load(T("make_dist_2126.py"))

NEW_HEAD = [
    '#!/usr/bin/env python',
    '# -*- coding: utf-8 -*-',
    '"""组装 柄输 2.1.28 发布包(精简版 / 完整版)。',
    '',
    '从 _pub_2128_x64 / _pub_2128_sc 取编译产物(exe + dict.txt + skin\\\\),',
    '配上文档, 产出两个可直接压缩的文件夹。不带 .pdb。',
    '',
    '2.1.28 = 换页动效多了一种「平移」。就加了一种风格, 别的都没动。',
    '',
    '一、平移: 手机上左右滑页那样。旧的一页横着往左滑走、一边滑一边淡掉, 新的一页',
    '        紧贴着它从右边滑进来、由透明转实 —— 两页**并排一起走**, 看着是一整条',
    '        内容在移动。按 B 退回上一层时整个动作反过来: 旧页往右走、新页从左边来。',
    '        滑的那一段会画到背景框外面去(滑到框边被齐刷刷切掉的话, 看着像卡住',
    '        而不是滑走)。整段约 280 毫秒。',
    '',
    '二、它用在哪儿: 「旋转」用在哪一批过渡上, 「平移」就用在完全相同的一批上 ——',
    '        往设置里钻一层 / 退回一层、进设置 / 回九键、九键换标点那一面, 全都走',
    '        同一套。两套只是"怎么走"不同, 覆盖范围一模一样。',
    '',
    '三、默认和旋转一个数都没改: 不选平移的话, 界面跟 2.1.26 一模一样。',
    '"""',
]

NOTE39 = [
    '39. 2.1.28 换页动效多了一种「平移」',
    '    · **设置 → 动效 现在有三档**: 默认(浮起淡入)、旋转(扑克牌)、**平移**。',
    '    · 平移演的是手机上左右滑页那样: 旧的一页横着往左滑走、一边滑一边淡掉,',
    '      新的一页紧贴着它从右边滑进来、由透明转实 —— 两页并排一起走, 看着是',
    '      一整条内容在移动。按 B 退回上一层时整个动作反过来(旧页往右、新页从左)。',
    '      整段大约 280 毫秒。改完下一次换页立刻生效。',
    '    · 它和「旋转」用在**完全相同的一批过渡**上: 往设置里钻一层 / 退回一层、',
    '      进设置 / 回九键、九键换标点那一面。两套只是走法不同。',
    '    · 原来那两套(默认 / 旋转)一个数都没改 —— 不选平移的话跟 2.1.26 一样。',
    '',
]

body = src.replace("\r\n", "\n").split("\n")
end = body.index('"""', 1) + 1
body = NEW_HEAD + body[end:]
out = "\n".join(body)

out = out.replace('VER = "2.1.26"', 'VER = "2.1.28"')
out = out.replace('"_pub_2126_x64", "柄输-2.1.26-精简版"', '"_pub_2128_x64", "柄输-2.1.28-精简版"')
out = out.replace('"_pub_2126_sc", "柄输-2.1.26-完整版"', '"_pub_2128_sc", "柄输-2.1.28-完整版"')

mark = "38. 2.1.26 之后"
if mark not in out:
    raise SystemExit("没找到 NOTES 里第 38 条, 结构变了?")
out = out.replace(mark, eol.join(NOTE39) + mark, 1)
save(T("make_dist_2128.py"), out, eol)

# ---------- 2. smoke ----------
src, eol = load(T("smoke_2126.py"))
out = src.replace("柄输-2.1.26-精简版", "柄输-2.1.28-精简版")
out = out.replace("柄输-2.1.26-完整版", "柄输-2.1.28-完整版")
out = out.replace("smoke_2124.py", "smoke_2128.py")
save(T("smoke_2128.py"), out, eol)

# ---------- 3. vercheck ----------
src, eol = load(T("_vercheck_2126.py"))
out = src.replace('WANT_COMMIT = "e1b1be5"', 'WANT_COMMIT = "fbc0e9c"')
out = out.replace("柄输-2.1.26-精简版", "柄输-2.1.28-精简版")
out = out.replace("柄输-2.1.26-完整版", "柄输-2.1.28-完整版")
save(T("_vercheck_2128.py"), out, eol)
print("done")
