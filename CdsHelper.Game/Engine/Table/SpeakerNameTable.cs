using System.Text;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Table;

/// <summary>
/// 대본 화자 이름(일본어, CP932) → 후원자·인물 번호.
/// </summary>
/// <remarks>
/// 대본의 화자 칸은 한국어판에서도 일본어 그대로다. 화자 해석기 <c>0x0040C880</c> 은 主人公·副官·監察官·執事 가
/// 아니면 두 표의 <b>일본어 이름</b>과 바이트째 견준다(<c>0x00425080</c>).
/// <code>
///   0x00523BB8  후원자 81 x 0x3C  +0 이름 ptr   → 0x004AD810(번호) 후원자 — 얼굴은 후원자 표 그 줄
///   0x004ED3E0  인물 281 x 0xCC   +0 이름 ptr   → 인물 밑표 그 줄의 얼굴
/// </code>
/// 파브리스·데·페레로(후원자 5)가 라몬을 부르는 장면이 그 꼴이다(「ファブリス＝デ＝フェレロ」).
/// </remarks>
public sealed class SpeakerNameTable
{
    private const string CacheName = "화자이름표";
    private const int SnapshotVersion = 1;

    private const int SponsorNamesVa = 0x00523BB8, SponsorRow = 0x3C, SponsorCount = 0x51;
    private const int PersonNamesVa = 0x004ED3E0, PersonRow = 0xCC, PersonCount = 0x119;

    /// <summary>한 이름.</summary>
    /// <param name="Sponsor">후원자 번호. 인물이면 -1.</param>
    /// <param name="Person">인물 번호. 후원자면 -1.</param>
    public readonly record struct Entry(string Name, int Sponsor, int Person);

    internal sealed record Snapshot(List<Entry> Names);

    private readonly Dictionary<string, Entry> _byName = [];

    private SpeakerNameTable(Snapshot snapshot)
    {
        // 후원자를 먼저 본다 — 게임도 후원자 표부터 찾는다.
        foreach (var e in snapshot.Names) _byName.TryAdd(e.Name, e);
    }

    /// <summary>그 이름의 후원자·인물. 없으면 null.</summary>
    public Entry? Find(string name) => _byName.TryGetValue(name, out var e) ? e : null;

    public static SpeakerNameTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out _, SnapshotVersion);
        return snapshot == null ? null : new SpeakerNameTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp932 = Encoding.GetEncoding(932);

        var names = new List<Entry>();
        for (int k = 0; k < SponsorCount; k++)
            if (exe.Raw(exe.Word(SponsorNamesVa + k * SponsorRow)) is { Length: > 0 } raw)
                names.Add(new Entry(cp932.GetString(raw), k, -1));
        for (int k = 0; k < PersonCount; k++)
            if (exe.Raw(exe.Word(PersonNamesVa + k * PersonRow)) is { Length: > 0 } raw)
                names.Add(new Entry(cp932.GetString(raw), -1, k));

        if (names.Count == 0 || names[0].Name != "ジョアン二世")
        {
            error = "화자 이름표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(names);
    }
}
