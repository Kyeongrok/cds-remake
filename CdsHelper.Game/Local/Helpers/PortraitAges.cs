using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 얼굴과 그 <b>중년 얼굴</b>의 짝.
/// </summary>
/// <remarks>
/// 게임은 주인공 얼굴 서른둘을 <b>앞 열여섯은 젊은 얼굴, 뒤 열여섯은 그 중년 얼굴</b>로
/// 짝지어 두고, 서른여섯 살부터 <c>얼굴 + 16</c> 을 띄운다
/// (<see cref="BarmaidTable.AgedFaceStep"/>).
///
/// <b>그 셈을 그대로 두면 얼굴을 더 넣는 순간 어긋난다.</b> 새로 넣은 414번에는 중년
/// 얼굴이 없는데 <c>+16</c> 을 하면 430번이라는 엉뚱한 얼굴이 나온다. 그래서 짝을
/// <b>또렷하게</b> 둔다.
///
/// <list type="number">
///   <item>사람이 짝지어 둔 것이 있으면 그것이 이긴다.</item>
///   <item>없으면 <b>앞 열여섯만</b> <c>+16</c> 로 물러선다 — 게임 그대로다.</item>
///   <item>그것도 아니면 <b>안 바꾼다</b>. 중년 얼굴이 없으면 젊은 얼굴 그대로 늙는다.</item>
/// </list>
/// </remarks>
public static class PortraitAges
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "초상화-중년짝";

    /// <summary>게임이 짝지어 둔 젊은 얼굴 수와, 중년 얼굴까지의 거리.</summary>
    public const int Paired = 16, Step = 16;

    /// <summary>짝지어 둔 한 줄.</summary>
    /// <param name="Face">젊은 얼굴 · <paramref name="Aged"/> 그 중년 얼굴.</param>
    /// <param name="Female">여자 벌인지.</param>
    public readonly record struct Entry(int Face, int Aged, bool Female);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Faces);

    private static Dictionary<(int, bool), int>? _map;

    /// <summary>짝이 바뀌었을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>사람이 짝지어 둔 것 전부.</summary>
    public static IReadOnlyDictionary<(int Face, bool Female), int> All => Map;

    /// <summary>
    /// 그 나이에 띄울 얼굴. 중년 얼굴이 없으면 <b>넘겨받은 얼굴 그대로</b> 돌려준다.
    /// </summary>
    /// <param name="face">젊은 얼굴 번호.</param>
    /// <param name="age">지금 나이.</param>
    /// <param name="female">여자 벌인지.</param>
    /// <param name="faces">몇 장이 들어 있는지 물어볼 초상화 벌. 없으면 장수를 안 따진다.</param>
    public static int At(int face, int age, bool female, Portraits? faces = null)
    {
        if (age < BarmaidTable.AgedFrom) return face;
        return AgedOf(face, female, faces);
    }

    /// <summary>그 얼굴의 중년 얼굴. <b>없으면 넘겨받은 얼굴 그대로</b>다.</summary>
    public static int AgedOf(int face, bool female, Portraits? faces = null)
    {
        if (face < 0) return face;

        int aged = Map.TryGetValue((face, female), out int paired) ? paired
                 // 게임이 짝지어 둔 앞 열여섯만 저절로 +16 이 된다.
                 : face < Paired ? face + Step
                 // 새로 넣은 얼굴에는 중년 얼굴이 없다.
                 : -1;

        return Real(aged, female, faces) ? aged : face;
    }

    /// <summary>그 얼굴이 중년 짝을 가졌는지 — 새 놀이 화면이 이것을 알려 준다.</summary>
    public static bool HasAged(int face, bool female, Portraits? faces = null) =>
        AgedOf(face, female, faces) != face;

    /// <summary>그 짝이 진짜 들어 있는 얼굴인지. 벌을 안 넘겨받았으면 안 따진다.</summary>
    private static bool Real(int aged, bool female, Portraits? faces)
    {
        if (aged < 0) return false;
        if (faces == null) return true;

        int count = female ? faces.FemaleCount : faces.MaleCount;
        return aged < count;
    }

    /// <summary>짝을 지어 둔다. <paramref name="aged"/> 가 −1 이면 짝을 없앤다.</summary>
    public static void Set(int face, int aged, bool female)
    {
        if (face < 0) return;
        if (aged < 0) { Map.Remove((face, female)); }
        else Map[(face, female)] = aged;
        Save();
    }

    /// <summary>지어 둔 짝을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    private static Dictionary<(int, bool), int> Map => _map ??= Load();

    private static Dictionary<(int, bool), int> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<(int, bool), int>();
        foreach (var row in saved?.Data.Faces ?? [])
            if (row.Face >= 0 && row.Aged >= 0) map[(row.Face, row.Female)] = row.Aged;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key.Item2).ThenBy(p => p.Key.Item1)
                      .Select(p => new Entry(p.Key.Item1, p.Value, p.Key.Item2)).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}짝", new Snapshot(rows), "사람이 지은 짝"));
        Changed?.Invoke();
    }
}
