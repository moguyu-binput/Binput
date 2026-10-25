"""验证第 2 项: 设置(版本 + 收起档位)能不能跨重启保留。
写一份 mode.txt -> 启动真程序 -> 看日志里读回来的是不是同一档, 以及文件有没有被改写。
"""
import os, subprocess, time, ctypes, ctypes.wintypes as wt

EXE = r'C:\Users\<你>\Binput-x64\GamepadT9.exe'
CWD = r'C:\Users\<你>\Binput-x64'
DIR = os.path.join(os.environ['LOCALAPPDATA'], 'GamepadT9')
MODE = os.path.join(DIR, 'mode.txt')
LOG = os.path.join(DIR, 'log.txt')


def run(ver, tier):
    open(MODE, 'w').write(f'{ver} {tier}')
    open(LOG, 'w').close()
    subprocess.Popen([EXE], cwd=CWD)
    time.sleep(4)
    # 唤醒(走的是和双击第二个实例一样的消息), 确认它活着
    wm = ctypes.windll.user32.RegisterWindowMessageW('GamepadT9_Wake')
    ctypes.windll.user32.SendMessageTimeoutW(0xFFFF, wm, 0, 0, 0x0002, 1000,
                                             ctypes.byref(wt.DWORD()))
    time.sleep(2)
    os.system('taskkill /F /IM GamepadT9.exe >nul 2>&1')
    time.sleep(1)
    log = open(LOG, encoding='utf-8', errors='replace').read()
    saved = open(MODE).read().strip()
    print(f'--- 写入 {ver} {tier} -> 启动后文件={saved!r}')
    for line in log.splitlines():
        if '版本 ->' in line or '收起后' in line or '唤醒' in line:
            print('    ', line.strip())
    return saved


for v, t in [(1, 3), (0, 2), (1, 0)]:
    run(v, t)
