namespace Binput;

/// <summary>
/// 探针用的日志桩。InputCore / ImeStatus 里那几处 App.Log 只是写文件,
/// 探针不跑真程序, 换成往控制台打就够了(而且探针的输出本来就没人看日志)。
/// </summary>
public static class App
{
    public static void Log(string s) { }
}
