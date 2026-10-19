using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;

namespace CdsHelper.Game.UI.AddOns;

/// <summary>
/// 저장 변수를 Lua 글로 적는다 — 다음에 켤 때 그 글을 그대로 돌려 값을 되살린다.
/// </summary>
/// <remarks>
/// 숫자 · 글 · 참거짓 · 표만 적는다. 함수 · 패널 같은 것은 건너뛰고, 표가 저를 다시 가리키면 두 번째부터는 안 적는다.
/// </remarks>
internal static class LuaSaved
{
    /// <summary>이보다 깊은 표는 안 적는다.</summary>
    private const int MaxDepth = 32;

    public static string Write(DynValue value)
    {
        var sb = new StringBuilder();
        Write(sb, value, 0, []);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, DynValue value, int depth, HashSet<Table> seen)
    {
        switch (value.Type)
        {
            case DataType.Boolean: sb.Append(value.Boolean ? "true" : "false"); break;
            case DataType.Number: sb.Append(Number(value.Number)); break;
            case DataType.String: Quote(sb, value.String); break;
            case DataType.Table when depth < MaxDepth && seen.Add(value.Table):
                sb.Append("{\n");
                foreach (var pair in value.Table.Pairs)
                {
                    if (!Storable(pair.Key) || !Storable(pair.Value)) continue;
                    sb.Append(' ', (depth + 1) * 2).Append('[');
                    Write(sb, pair.Key, depth + 1, seen);
                    sb.Append("] = ");
                    Write(sb, pair.Value, depth + 1, seen);
                    sb.Append(",\n");
                }
                sb.Append(' ', depth * 2).Append('}');
                seen.Remove(value.Table);
                break;
            default: sb.Append("nil"); break;
        }
    }

    private static bool Storable(DynValue v) =>
        v.Type is DataType.Boolean or DataType.Number or DataType.String or DataType.Table;

    private static string Number(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n)) return "0";
        if (n == Math.Floor(n) && Math.Abs(n) < 1e15) return ((long)n).ToString(CultureInfo.InvariantCulture);
        return n.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append('\\').Append(((int)c).ToString("D3", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
