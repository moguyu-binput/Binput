# -*- coding: utf-8 -*-
"""把 214 那套发布脚本复制成 215。

【注意】make_dist 那份**不能**做全局 "2.1.4" -> "2.1.5" 替换: 使用说明里有
好几处 "2.1.4 改的 / 2.1.4 起统一" 是**历史叙述**, 换成 2.1.5 就变成胡说了
(2.1.3 / 2.1.4 两版都踩过这个坑)。所以这里只对 make_dist 做**逐处定点替换**,
并且跑完断言一遍: 凡是 "2.1.5 改的 / 起 / 又补了 / 又统一" 这种句式一律不许出现,
历史那几句必须还停在 2.1.4。

另外 2.1.5 把数据目录搬到了程序旁的 save\\、名字也从 GamepadT9 改成了 Binput,
所以三个脚本里写死的旧路径 / 旧互斥体名 / 旧窗口标题也要一起改。
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
T = r"C:\Users\<你>\Binput\tools"

JOBS = [
    ("make_dist_214.py", "make_dist_215.py"),
    ("smoke_214.py", "smoke_215.py"),
    ("zip_214.py", "zip_215.py"),
    ("_vercheck_214.py", "_vercheck_215.py"),
]

# ---- make_dist 定点替换 ---------------------------------------------------
# 每条都是 (说明, 老文本, 新文本)。老文本必须**原样**出现在 214 那份里,
# 匹配不上就直接报错 —— 静默跳过会产出一个"说是新版其实写着旧路径"的坏包。

HEAD_OLD = '''"""组装 柄输 2.1.4 发布包(精简版 / 完整版)。

从 _pub_214_x64 / _pub_214_sc 取编译产物(exe + dict.txt + skin\\\\)，
配上文档，产出两个可直接压缩的文件夹。不带 .pdb。

2.1.4 = 顺着上一版往下捋的三处手感(打字逻辑 / 九键拼音 / 词库一处没碰):'''

HEAD_NEW = '''"""组装 柄输 2.1.5 发布包(精简版 / 完整版)。

从 _pub_215_x64 / _pub_215_sc 取编译产物(exe + dict.txt + skin\\\\)，
配上文档，产出两个可直接压缩的文件夹。不带 .pdb。

2.1.5 = 只动"东西存在哪儿、叫什么名字"(九键拼音 / 滑行 / 词库一处没碰):'''

BODY_OLD = '''        1 **翻页的说法统一了** —— 以前只有符号面板左右外侧挂着那对
          ◀LB / RB▶; 设置页那些带第二页的深层、窗口F 的列表页明明也能翻,
          左右却干干净净。现在判据只有一个: **当前这一页的页数 > 1**,
          三处都挂同一对; 只有一页时不亮, 免得出"挂着箭头按下去没反应"
        2 **拖着摇杆不回中** —— 以前进页 / 退页 / LB RB 翻页一律回正中,
          于是摇杆拖在 3 上按 A 进深层, 光标先在正中闪一下再跳回手里。
          现在: 手还在摇杆上就听摇杆的, 没碰摇杆才回正中
        3 **设置页里 L3 不再偷偷换九键 / 全键盘** —— 以前按了当场看不见
          (屏幕上摆的是设置), 退出来才发现变了, 看着像随机出错
"""'''

BODY_NEW = '''        1 **英文名改成 Binput**(中文名还是「柄输」) —— 程序集 / exe / 图标 /
          托盘 / 互斥体 / 日志里那一串字全跟着改了
        2 **数据目录搬到程序旁边** —— 不再往 C 盘用户目录里塞, 就放在程序
          所在文件夹的 save\\\\ 里; 没写权限时自动退回 %LOCALAPPDATA%\\\\Binput
        3 **十来个 txt 合成一份 Binput.save** —— 设置 + 习惯 + 快捷三段,
          整份加密; 目录里从此只有 save / save.bak / log.txt 三样
        4 **删掉滑行的驻留记录**(那份没有上限、只长不缩的 strokes.txt)
"""'''

FIXES = [
    # 1) 第一步就告诉用户: 数据也在这个文件夹里了(2.1.5 最实在的一条)
    (r'''   别只拷 exe —— 缺 dict.txt 拼音表读不到；缺 skin 的话
   可爱粉那套皮肤会退回纯色，功能一样不少，只是不好看。''',
     r'''   别只拷 exe —— 缺 dict.txt 拼音表读不到；缺 skin 的话
   可爱粉那套皮肤会退回纯色，功能一样不少，只是不好看。
   **2.1.5 起你的设置和输入习惯就存在这个文件夹的 save\\ 里** —— 整个文件夹
   拷走就是一份带着你全部习惯的柄输（详见第六节）。'''),

    # 2) 快捷那节: quick.txt -> save\Binput.save
    (r'''  这些数据存在 %LOCALAPPDATA%\GamepadT9\quick.txt，和输入习惯一样是**加密**的
  （绑你的 Windows 账号）。快捷键发出去同样受“管理员权限”那条规矩管 ——
  游戏是管理员开的而柄输不是的话，键会被 Windows 静默丢掉。''',
     r'''  这些数据存在程序旁边 save 文件夹里那份 Binput.save 里，和输入习惯放在一起，
  **整份加密**（绑你的 Windows 账号，双击打不开）。快捷键发出去同样受
  “管理员权限”那条规矩管 —— 游戏是管理员开的而柄输不是的话，
  键会被 Windows 静默丢掉。'''),

    # 3) 第六节 换版本 / 卸载: 数据位置换了
    (r'''整个文件夹删掉、换上新的就行。你的设置都记在
  C:\\Users\\<你的用户名>\\AppData\\Local\\GamepadT9\\
删程序不会带走，换版本后打开还是原来的样子。''',
     r'''整个文件夹删掉、换上新的就行 —— **但记得把里面那个 save 文件夹留下来**：
设置、输入习惯、快捷全在里面那份 Binput.save 里。换版本时把旧包的 save
整个拷进新包，打开还是原来的样子；删程序也一样，save 留着就不丢。
（装在没写权限的地方时会自动退回 C:\\Users\\<你的用户名>\\AppData\\Local\\Binput\\，
  日志里会记一行。）'''),

    # 4) 注意事项第 9 条: 那一串 txt 文件清单整个作废
    (r'''9. 数据都在用户目录，删程序不会丢
   C:\\Users\\<你>\\AppData\\Local\\GamepadT9\\
     mode.txt     版本 + 收起档位
     theme.txt    主题（0~7 纯色，8 可爱粉）
     key9num.txt  九键键上显不显示数字
     scale.txt    界面大小和位置
     pinyin.txt   学过的输入习惯（已加密，双击打开是乱码；见第 16 条）
     log.txt      日志''',
     r'''9. 数据在程序旁边的 save 文件夹里 —— 拷走它就是备份
   柄输\\save\\
     Binput.save       设置 + 输入习惯 + 快捷，**整份加密**，双击打不开
     Binput.save.bak   上一份好的（写坏时能救回来，永远只有一份）
     log.txt           日志（明文的，出问题你自己要看的就是它）
   整个 save 拷走 = 一份带着你全部习惯的柄输，拷到别的机器上照样用。
   （装在没写权限的地方，比如 Program Files，会自动退回
     C:\\Users\\<你>\\AppData\\Local\\Binput\\，日志里会记一行。）'''),

    # 5) 第 15 条快捷: quick.txt -> Binput.save
    (r'''    · 数据存在 %LOCALAPPDATA%\GamepadT9\quick.txt，**和输入习惯一样是加密的**。
      换机器不用自己拷文件了 —— 设置页「数据」里的「导出」把它和''',
     r'''    · 数据存在程序旁边 save\\Binput.save 里，**和输入习惯在同一份文件里、
      整份加密**。换机器不用自己拷文件了 —— 设置页「数据」里的「导出」把它和'''),

    # 6) 第 18 条三道保险: 目录换了
    (r'''18. 输入习惯 / 快捷存了三道保险（1.2.2 新加）
    · 换版本不会弄丢数据：数据都在 %LOCALAPPDATA%\GamepadT9\，跟 exe
      放在哪、是哪个版本完全无关，覆盖 exe、删旧包都不会碰它。''',
     r'''18. 输入习惯 / 快捷存了三道保险（1.2.2 新加，**2.1.5 起合成一份文件**）
    · 换版本不会弄丢数据：数据在程序旁边 save\\Binput.save 里，跟着文件夹走 ——
      换新包的时候把旧包那个 save 整个拷过去，打开还是原来的样子。'''),

    # 7) 第 19 条滑行: 那份 strokes.txt 记录已经整个撤掉了
    (r'''    · 记录照样在后台攒着（不再拿它改门槛），写在
      %LOCALAPPDATA%\GamepadT9\strokes.txt，**明文**，
      里面**没有**摇杆坐标、也没有你打了什么字，只有"第几格、几毫秒、
      算没算、当时门槛是哪一档"。想清空直接删这个文件就行。
      **只有"没按 X 删过、而且真的把拼音发出去过（按 RB / 上屏 / 网格里挑中）"
      的才算数** —— 滑完扔下不管的不算。''',
     r'''    · **2.1.5 起那份后台记录整个撤了** —— 它本来就没有上限（滑得越多越大），
      又不参与任何判定，留着只是占地方。门槛十档照旧，只是不再往后台记账。'''),

    # 8) 补一条第 20 条: 这一版到底改了什么
    (r'''    · 按住 LB 的时候左上角**恒为 JKL**，设置里那个「1 / 5 互换」这会儿不生效 ——
      滑行靠摇杆方位取键，方位不能跟着漂。
"""''',
     r'''    · 按住 LB 的时候左上角**恒为 JKL**，设置里那个「1 / 5 互换」这会儿不生效 ——
      滑行靠摇杆方位取键，方位不能跟着漂。

20. 2.1.5 这一版只动了"东西存在哪儿、叫什么名字"（打字逻辑一处没碰）
    · **英文名改成了 Binput**，中文名还是「柄输」—— exe、图标、托盘上那串字
      跟着改了，界面上给你看的仍旧是「柄输」。
    · **数据目录搬到程序旁边了**：不再往 C 盘用户目录里塞，就在程序所在文件夹
      的 save\\ 里。整个文件夹拷进 U 盘，就是一份带着你全部习惯的柄输。
    · **十来个 txt 合成一份 Binput.save**：设置、习惯、快捷全在里面，整份加密
      （密钥绑你的 Windows 账号，**不在文件里**），双击打不开，拷到别人机器上
      也解不开。这是防随手翻看、防拷走另行打开，**不是防取证**。
    · **体积不再越用越大**：输入习惯 8000 条封顶（超了按"用得少 + 最久没用"
      一次砍到 7200 条），一条快捷 200 字封顶，日志 150 KB 封顶（超了砍掉
      旧的一半）。
    · 头一次启动会**自动搬一次家**：老目录（AppData\\Local\\GamepadT9）里那份
      习惯和快捷会读进来写进新存档，老文件一个都不删。搬没搬到日志里有一行。
"""'''),
]

# ---- smoke / zip / vercheck 的定点修正 ------------------------------------
SMOKE = [
    # 数据在程序旁的 save 里了
    (r'''EXE = os.path.join(ROOT, sys.argv[1] if len(sys.argv) > 1 else "柄输-2.1.4-精简版", "柄输.exe")
CFG = os.path.join(os.environ["LOCALAPPDATA"], "GamepadT9")
LOG = os.path.join(CFG, "log.txt")''',
     r'''EXE = os.path.join(ROOT, sys.argv[1] if len(sys.argv) > 1 else "柄输-2.1.5-精简版", "柄输.exe")
# 2.1.5 起数据在**程序旁边的 save\\** 里(不再往 %LOCALAPPDATA% 塞),
# 所以日志也跟着走 —— 写死旧目录的话这份会把别人(或上一版)那份日志拿去断言。
CFG = os.path.join(os.path.dirname(EXE), "save")
LOG = os.path.join(CFG, "log.txt")'''),
    # 结束进程时认的镜像名
    ('for name in ("柄输.exe", "GamepadT9.exe"):',
     'for name in ("柄输.exe", "Binput.exe"):'),
    # 互斥体改名了
    (r'r"Local\GamepadT9_SingleInstance_v1"', r'r"Local\Binput_SingleInstance_v1"'),
    # 窗口标题: 2.1.5 起 OverlayWindow.xaml 里写的是「柄输」(以前是内部名
    # GamepadT9)。按老名字 FindWindow 永远找不到。
    (r'''    # 窗口标题是内部名 GamepadT9(日志里"焦点=GamepadT9 [柄输.exe]"那行),
    # 不是显示名"柄输" —— 按后者 FindWindow 永远找不到。
    for _ in range(20):
        hwnd = u32.FindWindowW(None, "GamepadT9")''',
     r'''    # 窗口标题 = 显示名「柄输」(OverlayWindow.xaml 的 Title)。2.1.5 以前写的是
    # 内部名 GamepadT9, 改名之后那句一起改了 —— 按老名字 FindWindow 找不到。
    for _ in range(20):
        hwnd = u32.FindWindowW(None, "柄输")'''),
]

VERCHECK = [
    ('WANT_COMMIT = "f093304"', 'WANT_COMMIT = "f712085"'),
]

# 跑完之后**不许**出现在 make_dist_215 里的句式 —— 出现就说明历史叙述被串了
BAD = ["2.1.5 改的", "2.1.5 起统一", "2.1.5 又补了", "2.1.5 又统一"]


def sub(s, old, new, tag):
    if old not in s:
        raise SystemExit("!! %s 匹配不上:\n%s" % (tag, old[:200]))
    return s.replace(old, new, 1)


def main():
    for a, b in JOBS:
        s = io.open(os.path.join(T, a), encoding="utf-8").read()

        if a.startswith("make_dist"):
            # 定点: 版本号 / 发布目录 / 包目录名
            s = sub(s, HEAD_OLD, HEAD_NEW, "文件头")
            s = sub(s, BODY_OLD, BODY_NEW, "版本摘要")
            s = sub(s, 'VER = "2.1.4"', 'VER = "2.1.5"', "VER")
            s = s.replace("_pub_214", "_pub_215")
            s = s.replace("柄输-2.1.4-精简版", "柄输-2.1.5-精简版")
            s = s.replace("柄输-2.1.4-完整版", "柄输-2.1.5-完整版")
            for i, (o, n) in enumerate(FIXES, 1):
                s = sub(s, o, n, "使用说明修正 %d" % i)
            for bad in BAD:
                if bad in s:
                    raise SystemExit("!! 历史叙述被串了: %r" % bad)
            # 旧路径不该再出现。**只查正文**: 末尾 VERDOC_OLD_200B 那段是
            # 2.0.0-beta 的史料(里面本来就该写着当年的 strokes.txt), 不能动。
            body = s.split("VERDOC_OLD_200B =", 1)[0]
            for must_gone in ("%LOCALAPPDATA%\\GamepadT9\\quick.txt",
                              "%LOCALAPPDATA%\\GamepadT9\\strokes.txt"):
                if must_gone in body:
                    raise SystemExit("!! 还留着旧路径: %s" % must_gone)
        elif a.startswith("smoke"):
            # 先做定点修正(它们认的还是 214 里那些老名字), 再把剩下的版本号换掉
            for o, n in SMOKE:
                s = sub(s, o, n, "smoke 修正")
            s = s.replace("2.1.4", "2.1.5")
        elif a.startswith("zip"):
            s = s.replace("2.1.4", "2.1.5")
        else:
            s = s.replace("2.1.4", "2.1.5")
            for o, n in VERCHECK:
                s = sub(s, o, n, "vercheck 修正")

        q = os.path.join(T, b)
        io.open(q, "w", encoding="utf-8", newline="\n").write(s)
        print("%-20s -> %-20s %6d 字节" % (a, b, len(s.encode("utf-8"))))

    # 交叉核对: 版本号该在的地方在, 不该在的地方不在
    d = io.open(os.path.join(T, "make_dist_215.py"), encoding="utf-8").read()
    print("\n版本号出现次数: 2.1.5=%d  2.1.4(历史叙述)=%d  2.1.3=%d"
          % (d.count("2.1.5"), d.count("2.1.4"), d.count("2.1.3")))


if __name__ == "__main__":
    main()
