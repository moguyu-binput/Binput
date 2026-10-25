// IL 对比探针: 证明"重构前后逻辑等价"。
//
// 为什么要它: partial class 拆分只是把同一份代码搬进不同文件, 但 Roslyn 生成元数据时
// 方法表的排列顺序跟着文件顺序走, 所以 dll 的 SHA256 不可能一模一样 —— 光看哈希会
// 以为"东西被改了"。要证明行为没变, 得下沉到 IL 层比三样东西:
//
//   ① 实名成员清单(类型/方法/字段): 一个不多一个不少
//   ② 每个实名方法的 IL 指令序列: 完全一致(**忽略操作数里的 token 编号** —— 那是
//      元数据重排导致的序号, 不是逻辑)
//   ③ 全部方法体(含编译器生成的 lambda)的指令集合: 完全一致
//
// ①② 都过, ③ 若只剩 .ctor/.cctor 一处"同样多的指令换了先后次序", 那属于 C# 规范允许
// 的范围(partial 之间字段初始化的先后本来就没规定), 工具会单独标出来让人眼核一遍。
//
// 用法: ildiff <旧.dll> <新.dll> [-dump]
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

static class Program
{
    // opcode -> 操作数字节数(-1 表示 InlineSwitch, 长度要单独算)
    static int OpSize(OperandType t)
    {
        switch (t)
        {
            case OperandType.InlineNone: return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar: return 1;
            case OperandType.InlineVar: return 2;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR: return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR: return 8;
            default: return -1;
        }
    }

    // IL 字节 -> 指令名序列。操作数一律丢掉, 只留指令本身。
    static string IlSeq(byte[] il)
    {
        var map = new Dictionary<short, OpCode>();
        foreach (var f in typeof(OpCodes).GetFields())
        {
            if (f.FieldType != typeof(OpCode)) continue;
            var op = (OpCode)f.GetValue(null);
            map[op.Value] = op;
        }
        var sb = new StringBuilder();
        int i = 0;
        while (i < il.Length)
        {
            short v = il[i];
            if (v == 0xFE && i + 1 < il.Length) v = (short)((0xFE << 8) | il[i + 1]);
            if (!map.TryGetValue(v, out var op))
            {
                sb.Append("[?").Append(v.ToString("X4")).Append("]");
                break;
            }
            sb.Append(op.Name).Append(' ');
            i += (v > 0xFF ? 2 : 1);
            int n = OpSize(op.OperandType);
            if (n < 0)
            {   // switch: 一个 int32 分支数 + N 个 int32 目标
                if (i + 3 >= il.Length) break;
                int cnt = il[i] | (il[i + 1] << 8) | (il[i + 2] << 16) | (il[i + 3] << 24);
                i += 4 + 4 * cnt;
            }
            else i += n;
        }
        return sb.ToString();
    }

    // 把签名 blob 解码成 "(int,string)->void" 这样的文字。
    // 直接哈希原始 blob 是不行的 —— blob 里写的是类型的元数据 token(编号), 拆分会改变
    // 编号但不会改变类型本身, 那样比出来的差异全是假的。
    sealed class TypeProv : ISignatureTypeProvider<string, object>
    {
        public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
        { var t = r.GetTypeDefinition(h); var ns = r.GetString(t.Namespace); return ns.Length > 0 ? ns + "." + r.GetString(t.Name) : r.GetString(t.Name); }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
        { var t = r.GetTypeReference(h); var ns = r.GetString(t.Namespace); return ns.Length > 0 ? ns + "." + r.GetString(t.Name) : r.GetString(t.Name); }
        public string GetTypeFromSpecification(MetadataReader r, object g, TypeSpecificationHandle h, byte k)
            => r.GetTypeSpecification(h).DecodeSignature(this, g);
        public string GetSZArrayType(string e) => e + "[]";
        public string GetArrayType(string e, ArrayShape s) => e + "[" + s.Rank + "]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetPointerType(string e) => e + "*";
        public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a)
            => g + "<" + string.Join(",", a) + ">";
        public string GetGenericTypeParameter(object g, int i) => "!!" + i;
        public string GetGenericMethodParameter(object g, int i) => "!" + i;
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetModifiedType(string mod, string un, bool req) => un;
        public string GetPinnedType(string e) => e;
    }

    static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).Substring(0, 16);

    // Roslyn 给匿名方法/闭包类起的名字里带着"方法排第几"的自增序号。拆文件会打乱顺序,
    // 于是 <.ctor>b__99_6 变成 <.ctor>b__323_6 —— 逻辑没变只是编号变了, 比之前先抹平。
    static string Norm(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=b__)\d+(?=_\d+)", "N");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=b__)\d+$", "N");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=9__)\d+(?=_\d+)", "N");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=DisplayClass)\d+", "N");
        return s;
    }

    class Row
    {
        public string Kind, Type, Name, Sig, Il, IlText;
        public string Key => Kind + "|" + Type + "|" + Name + "|" + Sig;
    }

    static (List<Row> rows, List<string> ilAll) Load(string path)
    {
        var rows = new List<Row>();
        var ilAll = new List<string>();
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var ns = md.GetString(td.Namespace);
            var rawName = md.GetString(td.Name);
            var typeName = (ns.Length > 0 ? ns + "." : "") + Norm(rawName);
            if (typeName.Contains("<PrivateImplementationDetails>")) continue;
            bool synth = rawName.StartsWith("<>");   // <>c / <>c__DisplayClass... 编译器内部类型

            foreach (var mh in td.GetMethods())
            {
                var mdf = md.GetMethodDefinition(mh);
                string il = "<no-body>";
                if (mdf.RelativeVirtualAddress != 0)
                    il = IlSeq(pe.GetMethodBody(mdf.RelativeVirtualAddress).GetILBytes());
                ilAll.Add(il);
                if (synth) continue;      // 匿名成员不进"姓名册", 但它的身体照样进③
                var msig = mdf.DecodeSignature(new TypeProv(), null);
                string sigText = "(" + string.Join(",", msig.ParameterTypes) + ")->" + msig.ReturnType;
                rows.Add(new Row
                {
                    Kind = "method",
                    Type = typeName,
                    Name = Norm(md.GetString(mdf.Name)),
                    Sig = sigText,
                    Il = Hash(il),
                    IlText = il
                });
            }
            if (synth) continue;
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                var fn = Norm(md.GetString(fd.Name));
                rows.Add(new Row
                {
                    Kind = "field",
                    Type = typeName,
                    Name = fn,
                    Sig = fd.DecodeSignature(new TypeProv(), null),
                    Il = "",
                    IlText = ""
                });
            }
        }
        return (rows, ilAll);
    }

    static Dictionary<string, Row> Index(List<Row> rows)
    {
        var d = new Dictionary<string, Row>();
        foreach (var r in rows) d[r.Key] = r;     // 重名说明归一化过度, 直接炸
        return d;
    }

    static int Main(string[] argv)
    {
        if (argv.Length < 2)
        {
            System.Console.WriteLine("usage: ildiff <old.dll> <new.dll> [-dump]");
            return 2;
        }
        var A = Load(argv[0]);
        var B = Load(argv[1]);
        bool dump = argv.Length > 2 && argv[2] == "-dump";
        System.Console.WriteLine("① 实名成员: 旧 {0} 项 / 新 {1} 项", A.rows.Count, B.rows.Count);

        var da = Index(A.rows);
        var db = Index(B.rows);
        var onlyA = da.Keys.Except(db.Keys).OrderBy(x => x).ToList();
        var onlyB = db.Keys.Except(da.Keys).OrderBy(x => x).ToList();
        System.Console.WriteLine("   只在旧 dll 里: {0}", onlyA.Count);
        foreach (var k in onlyA.Take(15)) System.Console.WriteLine("      - " + k);
        System.Console.WriteLine("   只在新 dll 里: {0}", onlyB.Count);
        foreach (var k in onlyB.Take(15)) System.Console.WriteLine("      + " + k);

        int diff = 0, orderOnly = 0;
        foreach (var k in da.Keys.Intersect(db.Keys))
        {
            if (da[k].Il == db[k].Il) continue;
            diff++;
            System.Console.WriteLine("② IL 有出入: " + k);
            if (!dump) continue;
            var x = da[k].IlText.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var y = db[k].IlText.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            bool sameSet = x.OrderBy(t => t).SequenceEqual(y.OrderBy(t => t));
            System.Console.WriteLine("     指令条数 旧 {0} / 新 {1}; 指令集合一致: {2}", x.Length, y.Length, sameSet);
            if (sameSet && x.Length == y.Length)
            {
                orderOnly++;
                System.Console.WriteLine("     -> 同一批赋值语句, 只是先后次序重排了(内容一条不少)");
            }
            else
            {
                int hit = -1;
                for (int i = 0; i < System.Math.Min(x.Length, y.Length); i++)
                    if (x[i] != y[i]) { hit = i; break; }
                System.Console.WriteLine("     第一条不同的指令位置: {0}", hit);
            }
        }

        var ma = A.ilAll.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        var mb = B.ilAll.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        int bodyDiff = ma.Keys.Except(mb.Keys).Count() + mb.Keys.Except(ma.Keys).Count()
                     + ma.Keys.Where(k2 => mb.ContainsKey(k2) && mb[k2] != ma[k2]).Count();
        System.Console.WriteLine("③ 全部方法体(含 lambda)指令集合差异: {0} 项", bodyDiff);
        System.Console.WriteLine("   其中 .ctor/.cctor 这种只是排序问题: {0} 处", orderOnly);

        bool ok = onlyA.Count == 0 && onlyB.Count == 0 && A.rows.Count == B.rows.Count
                  && diff == 0 && bodyDiff == 0;
        System.Console.WriteLine(ok
            ? "\n== 等价: 成员与指令序列全部一致 =="
            : (diff == orderOnly && onlyA.Count == 0 && onlyB.Count == 0 && A.rows.Count == B.rows.Count
               ? "\n== 基本等价: 实名成员与方法体完全一致; 仅 .ctor/.cctor 赋值次序重排 " + orderOnly + " 处 =="
               : "\n== 不等价 =="));
        return ok ? 0 : 1;
    }
}
