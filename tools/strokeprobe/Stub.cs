namespace Binput;

/// <summary>
/// 给 Config / Quick 用的最小桩。**刻意不去动真实的数据目录** ——
/// 所有写盘都走 SetDirForTest 指到一个临时目录里, 你机器上那份 strokes.txt 一个字节都不碰。
/// </summary>
static class App
{
    public static void Log(string s) { }
}
