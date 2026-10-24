namespace Binput;

/// <summary>
/// 窗口F 那几个按钮真要干的事(由 InputCore 实现, OverlayWindow 拿着用)。
/// 和 <see cref="IQuickUI"/> 正好反过来: 那边是"光标/层怎么走", 这边是"按下去做什么"。
/// 分开是为了让画格子那段代码一个字都不用知道 QuickStore 怎么存盘、怎么发键。
/// </summary>
public interface IQuickHost
{
    /// <summary>列表层短按 A: 把第 slot 格的东西发出去(空格子什么也不做)。</summary>
    void Send(int slot);

    /// <summary>深层 1 号键: 把**预览框里现在的文字**整段写进这一格。</summary>
    void WritePhrase(int slot);

    /// <summary>深层 2 号键: 摆出全键盘录一串快捷键。</summary>
    void RecordHotkey(int slot);

    /// <summary>深层 3 号键: 删掉这一格(位置留着, 后面的不往前挪)。</summary>
    void Delete(int slot);

    /// <summary>绑数字层: 把这一格绑到数字 num(1~9)。对面有东西就互换。</summary>
    void Bind(int slot, int num);

    /// <summary>绑数字层按 Y: 解除绑定(这一格搬到第 2 页往后第一个空位)。</summary>
    void Unbind(int slot);
}
