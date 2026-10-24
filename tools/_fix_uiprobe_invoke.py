# 一次性脚本: 把 uiprobe 里"裸反射 Invoke"全换成 Call(), 并给 Main 每个场景加护栏。
#
# 为什么要改: 产品代码给 PushPage / ShowPage / ShowFacePhoto / FaceIn 加了带默认值的
# src 参数(手机主题: 从哪格进就从哪格退), 反射 Invoke **不会**替你填默认值 —— 少传
# 一个就抛 TargetParameterCountException。而探针是在 Dispatcher.PushFrame 的循环里调
# 它的, 那种异常浮不到 Main 的 try, 表现就是进程一句话不说地挂掉 + 弹 crashsender。
# 换成 Call() 之后: 参数按形参个数补齐默认值, 以后再加参数也不会炸。
import re, io, sys

P = r"C:\Users\<你>\Binput\tools\uiprobe\Program.cs"
s = open(P, encoding="utf-8").read()
orig = s

# ---- ① typeof(OverlayWindow).GetMethod("X", BF).Invoke(_w, new object[] { ... }) ----
pat1 = re.compile(
    r'typeof\(OverlayWindow\)\.GetMethod\("(\w+)",\s*BF\)\.Invoke\(\s*_w,\s*new object\[\]\s*\{(.*?)\}\s*\)',
    re.S)
s, n1 = pat1.subn(lambda m: 'Call("%s"%s)' % (m.group(1), (", " + m.group(2).strip()) if m.group(2).strip() else ""), s)

# ---- ② .Invoke(_w, null) 版(无参方法, 如 PageFxStyle) ----
pat2 = re.compile(r'typeof\(OverlayWindow\)\.GetMethod\("(\w+)",\s*BF\)\.Invoke\(\s*_w,\s*null\s*\)', re.S)
s, n2 = pat2.subn(lambda m: 'Call("%s")' % m.group(1), s)

# ---- ③ 先存成变量再 Invoke 的那两处(showPage / push) ----
pat3 = re.compile(r'\b(showPage|push)\.Invoke\(\s*_w,\s*new object\[\]\s*\{(.*?)\}\s*\)', re.S)
def rep3(m):
    name = "ShowPage" if m.group(1) == "showPage" else "PushPage"
    return 'Call("%s", %s)' % (name, m.group(2).strip())
s, n3 = pat3.subn(rep3, s)

# ---- ④ 用不上的方法句柄声明删掉 ----
s, n4 = re.subn(r'[ \t]*var (?:showPage|push) = typeof\(OverlayWindow\)\.GetMethod\("(?:ShowPage|PushPage)", BF\);\r?\n', "", s)

print(f"替换: 直接Invoke {n1} 处 / 无参 {n2} 处 / 变量Invoke {n3} 处 / 删声明 {n4} 处")

# ---- ⑤ 插 Call() 帮助函数 ----
if "static object Call(" not in s:
    helper = '''
    // ================= 反射调窗口里的私有方法 —— 一律走这里 =================
    //
    // 为什么不直接 Invoke: 产品代码经常给方法**加带默认值的参数**(2.1.33 给
    // PushPage / ShowPage / ShowFacePhoto / FaceIn 加了 src: 手机主题要记住"从哪一格
    // 钻进来的")。反射**不会**替你填默认值 —— 少传一个就抛 TargetParameterCountException。
    // 而这个异常是在 Dispatcher.PushFrame 的循环里冒出来的, 浮不到 Main 的 try, 表现就是
    // 进程一句话不说地挂掉、弹个 crashsender(2.1.33 连着崩了好几回, 全是这个)。
    // 现在按形参个数补齐默认值: 以后再加参数, 老用例照样跑。
    static object Call(string name, params object[] args)
    {
        var mi = typeof(OverlayWindow).GetMethod(name, BF)
                 ?? throw new MissingMethodException("OverlayWindow 上没有方法 " + name);
        var ps = mi.GetParameters();
        var full = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            if (i < args.Length) { full[i] = args[i]; continue; }
            if (!ps[i].HasDefaultValue)
                throw new ArgumentException($"{name}: 第 {i + 1} 个参数 {ps[i].Name} 没默认值, 调用方只给了 {args.Length} 个");
            full[i] = ps[i].DefaultValue;
        }
        return mi.Invoke(_w, full);
    }

'''
    anchor = "    record Snap(double Left"
    assert anchor in s, "找不到插入锚点"
    s = s.replace(anchor, helper.lstrip("\n") + anchor, 1)
    print("已插入 Call() 帮助函数")

# ---- ⑥ Main 里每个场景加护栏: 崩了算 FAIL 继续跑, 不许整个进程没了 ----
old_main = """        PinyinBg();
        NumZero();
        NumLayerFx();
        ZeroFlicker();
        TierSteps();
        DeliverGate();
        NoteRise();
        En26SetWidth();
        RelSnapshot();
"""
new_main = """        // 【护栏】每个场景单独包一层: 里面抛了也算"这一条没过", 不许整个进程没了。
        // (以前一个 TargetParameterCountException 就把窗口连着进程一起带走, 只弹个
        //  crashsender, 看不出是哪一场崩的 —— 现在会点名。)
        void Scene(string tag, Action body)
        {
            Console.WriteLine($"--- 场景 {tag} ---");
            int before = _fail;
            try { body(); }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine($"  CRASH  {tag}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"         {ex.StackTrace?.Split('\\n')[0]?.Trim()}");
                try { _w?.Close(); } catch { }
            }
            Console.WriteLine($"  ({tag}: {(before == _fail ? "全过" : $"{_fail - before} 条没过")})");
        }

        Scene("拼音行底", PinyinBg);
        Scene("数字0", NumZero);
        Scene("数字层动效", NumLayerFx);
        Scene("零闪", ZeroFlicker);
        Scene("档位", TierSteps);
        Scene("投递门", DeliverGate);
        Scene("提示升起", NoteRise);
        Scene("全键盘设置宽", En26SetWidth);
        Scene("关系快照", RelSnapshot);
"""
assert old_main in s
s = s.replace(old_main, new_main, 1)

old2 = """        if (!(argv.Length > 0 && argv[0] == "slide")) SlideSettle();
        if (!(argv.Length > 0 && argv[0] == "slide")) SlideResume();
        if (argv.Length > 0 && argv[0] == "sl")  { SlideLevel();  app.Shutdown(); return 0; }
        if (argv.Length > 0 && argv[0] == "sp")  { SlidePhoto();  app.Shutdown(); return 0; }
        if (argv.Length > 0 && argv[0] == "fs")  { FlipSteady();  app.Shutdown(); return 0; }
        if (argv.Length > 0 && argv[0] == "sr")  { SlideResume(); app.Shutdown(); return 0; }
        if (argv.Length > 0 && argv[0] == "ss")  { SlideSettle(); app.Shutdown(); return 0; }
        if (!(argv.Length > 0 && argv[0] == "slide")) SlideLevel();
        if (!(argv.Length > 0 && argv[0] == "slide")) SlidePhoto();
        if (!(argv.Length > 0 && argv[0] == "slide")) FlipSteady();
"""
new2 = """        if (!(argv.Length > 0 && argv[0] == "slide")) Scene("手机·收敛", SlideSettle);
        if (!(argv.Length > 0 && argv[0] == "slide")) Scene("手机·接着滑", SlideResume);
        if (argv.Length > 0 && argv[0] == "sl")  { Scene("手机·进层", SlideLevel);  app.Shutdown(); return _fail; }
        if (argv.Length > 0 && argv[0] == "sp")  { Scene("手机·照片", SlidePhoto);  app.Shutdown(); return _fail; }
        if (argv.Length > 0 && argv[0] == "fs")  { Scene("换面不挪", FlipSteady);   app.Shutdown(); return _fail; }
        if (argv.Length > 0 && argv[0] == "sr")  { Scene("手机·接着滑", SlideResume); app.Shutdown(); return _fail; }
        if (argv.Length > 0 && argv[0] == "ss")  { Scene("手机·收敛", SlideSettle); app.Shutdown(); return _fail; }
        if (!(argv.Length > 0 && argv[0] == "slide")) Scene("手机·进层", SlideLevel);
        if (!(argv.Length > 0 && argv[0] == "slide")) Scene("手机·照片", SlidePhoto);
        if (!(argv.Length > 0 && argv[0] == "slide")) Scene("换面不挪", FlipSteady);
"""
assert old2 in s
s = s.replace(old2, new2, 1)

# One(...) 那三趟也包上
old3 = """        if (!(argv.Length > 0 && argv[0] == "plain")) One("默认(朴素)", FxStyle.Plain);
        if (!(argv.Length > 0 && argv[0] == "spin")) One("旋转", FxStyle.Spin);
        if (!(argv.Length > 0 && argv[0] == "slide")) One("平移", FxStyle.Slide);
"""
new3 = """        if (!(argv.Length > 0 && argv[0] == "plain")) Scene("默认(朴素)", () => One("默认(朴素)", FxStyle.Plain));
        if (!(argv.Length > 0 && argv[0] == "spin")) Scene("旋转", () => One("旋转", FxStyle.Spin));
        if (!(argv.Length > 0 && argv[0] == "slide")) Scene("手机", () => One("手机", FxStyle.Slide));
"""
assert old3 in s
s = s.replace(old3, new3, 1)
print("已给 Main 各场景加护栏")

# diag 分支也包一层(单个 diag 崩了直接点名, 不再 crashsender)
def diag_guard(m):
    return ('if (argv.Length > 0 && argv[0] == "%s") { try { %s(); } catch (Exception ex) '
            '{ Console.WriteLine("  CRASH  %s: " + ex); _fail++; } app.Shutdown(); return 0; }'
            % (m.group(1), m.group(2), m.group(2)))
s, n6 = re.subn(r'if \(argv\.Length > 0 && argv\[0\] == "(\w+)"\) \{ (\w+)\(\); app\.Shutdown\(\); return 0; \}',
                diag_guard, s)
print(f"diag 分支加护栏 {n6} 处")

open(P, "w", encoding="utf-8", newline="").write(s)
print("写回完成, 字节数 %d -> %d" % (len(orig), len(s)))
