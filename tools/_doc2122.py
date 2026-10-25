# -*- coding: utf-8 -*-
"""往《版本说明.md》最前面插 2.1.22 那一节。

纪律(踩过的): 一律用列表 + join 拼整段再替换, 别用 '''...'''.replace('\n','\r\n')
—— 那样容易写出 \r\r\n, make_dist 一读一写就会翻成多出来的空行。
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOC = os.path.join(ROOT, "skin-lab", "GamepadT9-Game", "版本说明.md")

lines = [
    "## 〇、2.1.22 更新了什么",
    "",
    "全键盘下按住 LT 进数字层、松开还原，那颗「0」会赖在全键盘的左下角不走。修掉了。",
    "",
    "### 怎么回事",
    "",
    "「0」不是九宫格里的一格，它是**另画的一颗键**，浮在九宫格下沿（8 的下面），只在",
    "数字层露脸 —— 所以它的显隐得每一帧问一次「现在是不是数字层」。",
    "",
    "而「离开数字层」最要紧的那条路（松开 LT 还原成**全键盘**），走的恰恰是渲染里",
    "「不是九键才画」那个分支的另一边：那一帧整个九键那一块都被跳过，于是没人再管它。",
    "",
    "结果它维持着上一帧的「看得见」，连坐标都还是按九键 360 宽算出来的那个 142",
    "（360 ÷ 2 − 38）。而键盘区这时候已经撑到 640 —— 142 在 640 里就是偏左那一块，",
    "看着就是「全键盘左下角多了一颗 0」。",
    "",
    "现在把这一句挪到分支外面：**只要不是数字层就当场收掉**，谁也别想赖在别的键盘上。",
    "",
    "（换脸时那一层「旧照片」是在刷新最开头拍的、拍在收掉它之前，所以数字层退场那一趟",
    "照样由照片陪着淡出，不会「啪」一下凭空消失。）",
    "",
    "### 这一版该怎么验收",
    "",
    "1. 按 L3 切到**全键盘**，按住 LT：数字层出来，中间那颗 0 在正下方。",
    "2. 松开 LT：回到全键盘，**0 跟着一起没了**（以前它留在左下角）。",
    "3. 来回按几次（按住 / 松开），全键盘上始终不该出现那颗 0。",
    "",
    "## 〇、2.1.21 更新了什么",
]

raw = open(DOC, "rb").read().decode("utf-8")
old_head_prev = "## 〇、2.1.21 更新了什么"
if "## 〇、2.1.22 更新了什么" in raw:
    raise SystemExit("2.1.22 那一节已经在文档里了, 别插第二次")
if old_head_prev not in raw:
    raise SystemExit("没找到 2.1.21 那个标题, 文档结构变了?")

eol = "\r\n"
body = raw.replace(eol, "\n")
new = eol.join(lines)
i = body.index(old_head_prev)
body = body[:i] + new.replace(eol, "\n") + body[i + len(old_head_prev):]
open(DOC, "wb").write(body.replace("\n", eol).encode("utf-8"))

chk = open(DOC, "rb").read().decode("utf-8")
print("CRCRLF =", chk.count("\r\r\n"))
print("裸 LF  =", chk.count("\n") - chk.count("\r\n"))
print("已插入 2.1.22 一节")
