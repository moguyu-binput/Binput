# 柄输 Binput —— 用手柄打中文

> Windows 上的 **手柄九键中文输入法**。给"游戏里握着手柄、又想打中文"这个场景做的，
> 主场是 FF14。
>
> A gamepad T9 Chinese input method for Windows — type Chinese with a controller
> without ever letting go of it. Built for FF14.

> ⚠️ **纯 AI 生成的实验项目，仅个人测试用。** 它能读手柄并模拟键盘输入，
> 可能被 Windows 安全机制拦下、可能与游戏反作弊冲突（有封号风险），
> 也可能丢字卡键。请自行判断是否使用，风险自负；作者不承担任何责任。

- [怎么装](#怎么装)
- [一句话思路](#一句话思路)
- [两个版本](#两个版本)
- [怎么用](#怎么用)
- [手柄键位速查](#手柄键位速查)
- [自己编译](#自己编译)
- [目录结构](#目录结构)
- [测试](#测试)
- [许可](#许可)

---

## 怎么装

下载 Release 里那两个包之一，解压，双击 `柄输.exe`（屏幕上看不到东西是对的，它待在右下角托盘）：

| 包 | 大小 | 需要本机装 .NET 吗 |
| --- | --- | --- |
| **精简版** | 约 7 MB | **要** —— [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **完整版** | 约 150 MB | 不要，运行时打包在里头 |

> **游戏里怎么都没反应？先把柄输改成"以管理员身份运行"。**
> 游戏要是管理员权限而柄输不是，Windows 会把柄输发过去的键**一个不剩地静默丢掉** ——
> 不报错、不弹窗，就是没反应。两边权限一样高才行。

系统要求：Windows 10 / 11 64 位。手柄有三条路一起读、结果并起来：XInput（Xbox 四个
槽位都扫）、`Windows.Gaming.Input`（Switch Pro 这类，按键映射由微软做）、HID + HidP
（PS5 DualSense —— 微软不认它，只能自己照设备描述符解）。插好几只也一起能用。

## 一句话思路

最先想的是"不让游戏读到手柄"（HidHide / ViGEm 都试过，都卡住了）。
现在改成 **"让游戏失去焦点"** —— 唤出键盘时把我们的窗口顶成前台，
游戏失焦后自己就不吃手柄了（FF14 设置里的"失焦手柄失效"正是这个前提）。

## 两个版本

字怎么打，**两个版本完全一样**：

- **九键**：只把数字解成拼音（`7468326` → `shou'dao`），发给系统输入法，
  **汉字、词组、联想全部由系统输入法给** —— 我们不自己出词
- **26 键**：纯英文，字母直接投递，不走输入法

**唯一的区别是"字去哪儿"**：

| 版本 | 界面 | 字去哪儿 |
| --- | --- | --- |
| **预输入版** | 前台窗口，上面有**预览框** | 先打在预览框里，收起时整段复制 + 粘进游戏 |
| **直出版** | 不抢焦点、没有预览框 | 按键**直接送到当前有焦点的窗口** |

FF14 里一般用**预输入版**（一句话打完再发，游戏不会吃到半截按键）；
普通 Windows 窗口里用**直出版**更顺手，跟用键盘没两样。

## 怎么用

1. 按 **L3 + R3**（左右两个摇杆垂直按下去）唤出键盘，再按一次收起。
   这对组合可以改：设置 → 唤醒键 → 键盘（F1~F12）/ 手柄（Back / Start / Back+Start / L3 / R3）。
2. 进设置：**长按 Y 约 1 秒**（界面会像翻卡片一样翻到背面），再长按一次翻回来。
   也可以托盘右键 → 设置…，或者点界面右下角的小齿轮。
3. 打字：九键 `7468326` → 十字键 ←/→ 挑拼音 → **RB** 整段发给输入法
   → 十字键选词、↑/↓ 翻页 → 再按一下 **RB** 空格上屏。
4. 整段联想都不对时按 **Y** 只定住头一个字，剩下的数字会自动重新切出第二个字的读音。

详细的键位和排错看 `版本说明.md` 和 Release 包里的「使用说明.txt」。

## 手柄键位速查

| 键 | 干嘛 |
| --- | --- |
| **L3 + R3** | 唤出 / 收起键盘（默认，可改） |
| 左摇杆 | 选键（九宫格和全键盘都是它） |
| A | 输入光标所在那颗键 |
| X | 退格　　RT：回车 |
| **Y（有拼音）** | 切分：只定住头一个字 —— **按下就生效** |
| **Y（没拼音）** | 按一下：九宫格 ↔ 全键盘　　按住满**半秒**（500ms）：进 / 出设置 |
| **Y（数字层）** | 按一下 = 出 0（原来 L3 那桩） |
| **Y（绑数字）** | 「快捷短语」绑数字那一层：解除绑定（原来 L3 那桩） |
| RB | 发出拼音；没有待发拼音时就是空格 |
| 十字键 ← → | 挑拼音时换切分；发出拼音后就是键盘方向键 |
| LT（按住） | 数字层，键上只显示数字 |
| L3 | —— 彻底闲着（三桩活全归 Y 了），只剩和 R3 一起唤出 / 收起 |
| R3 | —— 彻底闲着（调界面搬到设置 → 键盘 →「调整窗口」，按 B 退出） |
| 左上角长按 | 打开「快捷短语」面板 |

> **Y 的判据只有一条：这一下能不能切分拼音。** 有拼音就绝不进长按计时 ——
> 打字打一半手指在 Y 上多停一会儿，不该把设置页弹出来。

## 自己编译

```bash
# 精简版(要本机有 .NET 8 运行时才能跑)
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PlatformTarget=x64 -p:PublishSingleFile=true -o out-x64

# 完整版(运行时打包进 exe)
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PlatformTarget=x64 -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=false -o out-sc
```

两个 `-p:PublishSingleFile=true` 不能漏（漏了 exe 只有 533 KB）；
完整版那个 `IncludeNativeLibrariesForSelfExtract=false` 也不能漏
（WPF 的 native 库压进单文件会崩）。**两个 publish 不能并发跑**（同一个 obj/ 互写）。

需要 .NET 8 SDK + Windows。

## 目录结构

```
OverlayWindow.*.cs   界面那一个窗口，按职责拆成 9 个 partial 文件
                     (.xaml.cs 本体 / .Layer 设置层 / .Render 每帧怎么画 /
                      .Fx 怎么动 / .Pages 设置页 / .Theme 皮肤 / .Layout 摆位 /
                      .Build 建控件 / .Quick 快捷短语)
InputCore.cs         输入的全部逻辑：手柄怎么按、字怎么出去
T9Engine.cs          拼音切分与排序（数字串 -> 几种候选拼音）
Config.cs            存档（%LOCALAPPDATA%\Binput\Binput.save，DPAPI 加密）
GamepadInput.cs      XInput 读数
TextOutput.cs        SendInput 唯一的出口
SkinBg / SkinTex     贴纸风皮肤那套三段式贴图
tools/               十套自测探针 + 打包脚本
dict.txt             拼音表（只用来判断"这串拼音像不像人话"，里面一个汉字都没有）
skin/                贴纸风那套皮肤的贴图
版本说明.md           从 1.0 到现在的完整更新记录（中文）
```

## 测试

仓库里带 **十套命令行探针**，都是纯逻辑推演，不建窗口、不往真窗口发键：

```bash
cd tools/coretest && dotnet build -c Release && ./bin/Release/net8.0/coretest.exe
```

`coretest` 打字 / `fprobe` 换屏身份 / `gridprobe` 网格 / `quickprobe` 快捷短语 /
`strokeprobe` 滑行 / `useprobe` 习惯 / `mineprobe` 词库 / `fuzzprobe` 随机乱按 /
`giprobe` Windows.Gaming.Input 手柄探测（看 Windows 把每只手柄认成了什么、
每颗键标成了什么；需要真手柄） /
**`uiprobe` 界面选址（唯一真开一个窗口的那套，改界面 / 主题 / 动效必须跑）**。

## 许可

**GPL-3.0**（见 [LICENSE](LICENSE)）。

为什么不是 MIT：词库 `dict.txt` 里的**词频**来自
[rime/rime-essay](https://github.com/rime/rime-essay)（Rime 官方《八股文》词汇表，
那个仓库的 LICENSE 是 **LGPL-3.0**），我们这份是它的派生 —— 把词转成拼音串、
同一个拼音串下有多个词时只取词频最高的那条。LGPL-3.0 第 2 条 b 允许把改过的副本
**按 GPL-3.0 发布**，于是整个项目统一 GPL-3.0：代码和词库一个协议，
不用再解释"哪个文件归哪个协议"。

这意味着：**你改了这个项目再分发出去，也得按 GPL-3.0 把源码一并给出**。
只改给自己用、不分发，那随意。

词库 `dict.txt` 是一张**拼音表**（每行一个成词的拼音串），
只用来判断"这串拼音像不像一句人话"，**里面一个汉字都没有** ——
汉字由系统输入法出，所以这个项目本身不带任何字库。

第三方交代（词库来源、NuGet 包、.NET 运行时）见
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
