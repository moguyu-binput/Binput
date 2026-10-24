#!/usr/bin/env python
"""下载 HidHide 官方安装包到 thirdparty/HidHide/, 供发布包内置。

只从 GitHub 官方仓库取。作者在原仓库 README 里明确警告过:
除了 github.com/nefarius/HidHide 和 *.nefarius.at, 其他号称官方的站点都是骗子
(比如搜索时会出现一个 hidhide.com, 那是第三方站, 别从那儿下)。

用法:
    python tools/fetch_hidhide.py            # 取最新版
    python tools/fetch_hidhide.py 1.5.230.0  # 指定版本
"""
import hashlib
import json
import os
import sys
import urllib.request

API = "https://api.github.com/repos/nefarius/HidHide/releases/latest"
UA = {"User-Agent": "GamepadT9"}
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "thirdparty", "HidHide")


def main():
    tag = sys.argv[1] if len(sys.argv) > 1 else None
    os.makedirs(OUT_DIR, exist_ok=True)

    # 1. 查最新版
    req = urllib.request.Request(API, headers=UA)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            rel = json.load(r)
    except Exception as e:
        print("查询最新版失败: %s" % e)
        print("手动下载: https://github.com/nefarius/HidHide/releases/latest")
        return 1

    if tag is None:
        tag = rel.get("tag_name")
    print("目标版本: %s (最新为 %s)" % (tag, rel.get("tag_name")))

    # 2. 挑 x64 安装包。HidHide 只有 x64 构建 —— 没有 arm64 版
    asset = None
    for a in rel.get("assets", []):
        if a["name"].endswith(".exe") and "x64" in a["name"]:
            asset = a
            break
    if asset is None:
        print("没找到 x64 安装包, 资产列表:")
        for a in rel.get("assets", []):
            print("   ", a["name"])
        return 1

    url = asset["browser_download_url"]
    if tag != rel.get("tag_name"):
        url = "https://github.com/nefarius/HidHide/releases/download/%s/%s" % (
            tag, asset["name"])

    dst = os.path.join(OUT_DIR, asset["name"])
    print("下载: %s" % url)
    print("     -> %s" % dst)
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA),
                                    timeout=300) as r:
            data = r.read()
    except Exception as e:
        print("下载失败: %s" % e)
        return 1

    with open(dst, "wb") as f:
        f.write(data)

    # 3. 校验: 大小必须和 GitHub 报的一致, 顺便留个 SHA256 供核对
    expect = asset["size"]
    sha = hashlib.sha256(data).hexdigest()
    print()
    print("大小: %.2f MB (GitHub 声明 %d 字节, 实际 %d)"
          % (len(data) / 1048576, expect, len(data)))
    print("SHA256: %s" % sha)
    if len(data) != expect:
        print("!! 大小对不上, 下载可能不完整, 已保留文件但请勿打包")
        return 1
    if data[:2] != b"MZ":
        print("!! 不是 PE 文件(缺 MZ 头), 可能下到了网页而不是安装包")
        return 1

    # 4. 写下来源与哈希, 便于日后核对
    meta = os.path.join(OUT_DIR, "来源与校验.txt")
    with open(meta, "w", encoding="utf-8") as f:
        f.write("文件名: %s\n" % asset["name"])
        f.write("版本:   %s\n" % tag)
        f.write("来源:   %s\n" % url)
        f.write("大小:   %d 字节\n" % len(data))
        f.write("SHA256: %s\n" % sha)
    print()
    print("已记录来源与哈希: %s" % meta)
    print("打包时会自动收进发布包。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
