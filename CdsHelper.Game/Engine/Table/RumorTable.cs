using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 술집 주인이 들려주는 소문 표(CDS_95.EXE <c>0x00525078</c>, 24바이트 x 191).
/// </summary>
/// <remarks>
/// <code>
///   +0x00  발견물 번호 — 계약 힌트 줄의 +0x08(<see cref="HintTable.Hint.Discovery"/>)과 짝짓는다
///   +0x04  도시 넷 — 이 소문을 들을 수 있는 도시(−1 은 빈 칸, 0x004B0970)
///   +0x14  본문 — 그 도시에서 들으면 이 글을 그대로 읽어 준다(0x004B0990)
/// </code>
/// 술집 「정보를 듣는다」가 이 표로 줄을 고르고(<c>0x0042E780</c>) 말할 도시를 고른다(<c>0x0042F790</c>).
/// 계약 힌트 표(<see cref="HintTable"/>, <c>0x004D8E80</c>)와는 딴 표다.
/// </remarks>
public sealed class RumorTable
{
    private const string CacheName = "소문표";
    private const int Version = 1;

    private const int TableVa = 0x00525078;
    private const int RowSize = 0x18;

    /// <summary>줄 수(<c>0x00414390</c> 이 0~0xBE 를 돈다).</summary>
    public const int Count = 191;

    /// <summary>한 줄에 든 도시 칸 수.</summary>
    public const int CitySlots = 4;

    /// <summary>본문 한 줄의 가장 긴 길이(바이트) — 가장 긴 것이 244 다. 기본 64 로는 거의 다 null 이 된다.</summary>
    private const int TextLimit = 512;

    /// <param name="Discovery">발견물 번호(계약 힌트의 목표와 같은 값).</param>
    /// <param name="Cities">들을 수 있는 도시 넷. −1 은 빈 칸이다.</param>
    /// <param name="Text">그 도시에서 들려주는 본문.</param>
    [method: JsonConstructor]
    public readonly record struct Rumor(int Discovery, int[] Cities, string Text);

    internal sealed record Snapshot(List<Rumor> Rumors);

    private readonly List<Rumor> _rumors;

    private RumorTable(Snapshot snapshot) => _rumors = snapshot.Rumors;

    public static string LastError { get; private set; } = "";

    public IReadOnlyList<Rumor> Rumors => _rumors;

    public static RumorTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error, Version);
        LastError = error;
        return snapshot == null ? null : new RumorTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";
        var rumors = new List<Rumor>(Count);
        for (int k = 0; k < Count; k++)
        {
            int row = TableVa + k * RowSize;
            var cities = new int[CitySlots];
            for (int j = 0; j < CitySlots; j++) cities[j] = exe.Int(row + 4 + j * 4);
            string? text = exe.Text(exe.Word(row + 0x14), TextLimit);
            if (text == null) break;
            rumors.Add(new Rumor(exe.Int(row), cities, text));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 대 본다 — 아프리카 남단(발견물 0, 도시 97·99·90)이다.
        if (rumors.Count != Count || rumors[0] is not { Discovery: 0 } first
            || first.Cities is not [97, 99, 90, -1])
        {
            error = "소문 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(rumors);
    }
}
