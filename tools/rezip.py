import os, zipfile, sys

root = r"C:\Users\<你>\WorkBuddy"

def zipdir(folder, zippath):
    src = os.path.join(root, folder)
    dst = os.path.join(root, zippath)
    if os.path.exists(dst):
        os.remove(dst)
    n = 0
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for dirpath, dirnames, filenames in os.walk(src):
            for fn in filenames:
                full = os.path.join(dirpath, fn)
                rel = os.path.relpath(full, root)
                z.write(full, rel)
                n += 1
    print("%s -> %d files, %.1f MB" % (zippath, n, os.path.getsize(dst) / 1024 / 1024))

zipdir("柄输-1.0.1-完整版", "柄输-1.0.1-完整版.zip")
zipdir("柄输-1.0.1-精简版", "柄输-1.0.1-精简版.zip")
