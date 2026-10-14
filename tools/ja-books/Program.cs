// 일본어판 대본 대사를 한국어 대본 책의 같은 자리(책 · 파트 · 파트 안 대사 차례)에 짝지어 lang/ja-books.json 으로 굽는다.
// 쓰는 법: dotnet run -- <일본어판 CDS95 폴더> <lang/ja-books.json> <한국어 책 JSON 이 든 exe-tables 의 윗 폴더>
// 대사 수가 같은 파트만 적는다 — 런타임(DisevRunner.JapaneseLine)도 수가 다르면 안 쓴다.
using System.IO;
using System.Text;
using CdsHelper.Game.Local.Helpers;
using System.Text.Json;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Support.Local.Helpers;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var cp932 = Encoding.GetEncoding(932);
string jpDir = args[0], outPath = args[1];
Directory.SetCurrentDirectory(args[2]);   // 저장소 뿌리 — exe-tables 를 찾게

var books = new (string Cache, string File)[]
{
    ("발견이벤트", "DISEV.CDS"), ("이야기0", "STORY0.CDS"), ("이야기1", "STORY1.CDS"),
    ("PEX", "PEX.CDS"), ("PDG", "PDG.CDS"), ("PHT", "PHT.CDS"), ("PCQ", "PCQ.CDS"),
    ("EEX", "EEX.CDS"), ("EDG", "EDG.CDS"), ("EHT", "EHT.CDS"), ("ECQ", "ECQ.CDS"),
};

var result = new SortedDictionary<string, SortedDictionary<int, List<string>>>();
foreach (var (cache, file) in books)
{
    var kr = DisevBook.Open(cache);
    var jp = Ls12Reader.Open(Path.Combine(jpDir, file));
    if (kr == null || jp == null) { Console.WriteLine($"{cache}: 못 엶 kr={kr != null} jp={jp != null}"); continue; }
    int matched = 0, total = 0, lines = 0;
    var parts = new SortedDictionary<int, List<string>>();
    for (int p = 0; p < Math.Min(kr.Count, jp.PartCount); p++)
    {
        var k = Dialogs(kr.Part(p));
        var j = Dialogs(jp.Decode(p) ?? []);
        if (k.Count == 0) continue;
        total++;
        if (k.Count != j.Count) continue;
        matched++;
        lines += j.Count;
        parts[p] = j.Select(Body).ToList();
    }
    result[cache] = parts;
    Console.WriteLine($"{cache}: 파트 {matched}/{total} 짝 맞음, 대사 {lines}");
}
File.WriteAllText(outPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));

// 파트 안 대사 명령(Say · AskYesNo · SayBare)의 날바이트를 토막 · 명령 차례대로 — 런타임(DisevRunner)과 같은 잣대다.
static List<byte[]> Dialogs(byte[] data)
{
    var list = new List<byte[]>();
    if (data.Length == 0 || DisevPart.Parse(data, out _) is not { } part) return list;
    foreach (int start in part.ChunkStarts)
    {
        var (from, to) = part.ChunkRange(start);
        foreach (var op in DisevScript.Parse(part.Data, from, to))
        {
            var raw = part.Data.AsSpan(op.Offset, Math.Min(op.Length, part.Data.Length - op.Offset)).ToArray();
            if (op.Kind == "대사") list.Add(raw);
        }
    }
    return list;
}

// 대사 본문을 CP932 로 푼다. 제독 이름 자리표는 %P, 딴 자리표(조사 따위)는 뺀다. 81 5E 는 원본처럼 「/」.
string Body(byte[] raw)
{
    int start = raw.Length > 0 && raw[0] == 0x0A ? 1 : 2;
    int end = Array.IndexOf(raw, (byte)0, start);
    var data = raw.AsSpan(start, (end < 0 ? raw.Length : end) - start);
    int bodyStart = 0;
    for (int i = 0; i + 1 < Math.Min(data.Length, 40); i++)
        if (data[i] == 0x81 && data[i + 1] == 0x46) { bodyStart = i + 2; break; }
    var src = data[bodyStart..];
    var cooked = new List<byte>();
    var sb = new StringBuilder();
    void Flush() { sb.Append(cp932.GetString(cooked.ToArray())); cooked.Clear(); }
    for (int i = 0; i < src.Length;)
    {
        if (i + 1 < src.Length && src[i] == 0x81 && src[i + 1] == 0x5E) { Flush(); sb.Append('/'); i += 2; continue; }
        if (i + 4 <= src.Length && src[i] == 0x81 && src[i + 1] == 0x93 && src[i + 2] == 0x82)
        {
            Flush();
            if (NameToken.IsName(src[i + 3])) sb.Append("%P");
            i += 4;
            continue;
        }
        cooked.Add(src[i]);
        i++;
    }
    Flush();
    return sb.ToString().Replace('　', ' ').TrimEnd();
}
