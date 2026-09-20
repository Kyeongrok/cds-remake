using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시 그림 안에 서 있는 <b>마을 사람</b> 표 — 누르면 그 고장 이야기를 한 마디 한다.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x005152F8, 0x20 바이트 x 275 (도시마다 셋까지)
///   +0x00 도시 번호   +0x04 갈래(100·101·102 · 200·201·202)
///   +0x08,+0x0C 자리  +0x10,+0x14 크기        ; 누를 자리는 (x + w/4, y + h/4, w/2, h/2)
///   +0x18 말          +0x1C 둘째 말(1493년부터 쓰는 것, 리스본·세빌리아 넷뿐)
/// </code>
/// 사람 그림은 <b>도시 그림에 이미 그려져 있다</b> — 표는 누를 자리와 말만 든다(<c>0x00473800</c> 이 찾고
/// <c>0x00473880</c> 이 상자를 낸다). 건물을 먼저 보고, 안 걸리면 사람을 본다(<c>0x00491DC0</c>).
///
/// 갈래 100~102 는 <b>남자</b>, 200~202 는 <b>여자</b>다 — 커서를 올리면 이름표가 「남」·「여」로 뜨고
/// (<c>0x00491BB4</c> 의 <c>0x0053B3B0</c>·<c>0x0053B3B4</c>), 여자는 「이곳은 %s입니다.」를 안 한다
/// (<c>0x00492EC3</c>). 100 쪽은 반쯤 도시·나라 이름만 말한다(<c>0x00492E40</c>).
/// </remarks>
public sealed class TownFolkTable
{
    /// <summary>이 갈래부터가 여자다(<c>0x00491BB4</c> 의 <c>cmp 200</c>).</summary>
    public const int FemaleKind = 200;

    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\마을사람표.json</c>).</summary>
    private const string CacheName = "마을사람표";

    /// <summary>알맹이 모양 판.</summary>
    private const int Version = 1;

    private const int TableVa = 0x005152F8, RowSize = 0x20;

    /// <summary>표 줄 수.</summary>
    public const int Count = 275;

    /// <summary>갈래 여섯(<c>0x0056A0D0</c>).</summary>
    public static readonly int[] Kinds = [100, 101, 102, 200, 201, 202];

    /// <summary>둘째 말을 쓰기 시작하는 해(<c>0x00492DF2</c> 의 <c>cmp 0x5D5</c>).</summary>
    public const int SecondLineYear = 1493;

    /// <summary>마을 사람 하나.</summary>
    /// <param name="Kind">갈래 — 100~102 뜨내기 · 200~202 그 나라 사람.</param>
    /// <param name="Words">할 말.</param>
    /// <param name="Later">1493년부터 할 말. 없으면 빈 글이다.</param>
    [method: JsonConstructor]
    public readonly record struct Folk(int City, int Kind, int X, int Y, int Width, int Height,
                                       string Words, string Later = "")
    {
        /// <summary>누를 자리(<c>0x00473880</c>) — 상자의 가운데 절반이다.</summary>
        [JsonIgnore] public int HitX => X + Width / 4;
        [JsonIgnore] public int HitY => Y + Height / 4;
        [JsonIgnore] public int HitWidth => Width / 2;
        [JsonIgnore] public int HitHeight => Height / 2;

        /// <summary>그 해에 할 말.</summary>
        public string WordsOn(int year) =>
            year >= SecondLineYear && Later.Length > 0 ? Later : Words;
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Folk> Folk);

    private readonly List<Folk> _folk;

    private TownFolkTable(Snapshot snapshot) => _folk = snapshot.Folk;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그 도시에 선 사람들. 표 차례 그대로다.</summary>
    public List<Folk> InCity(int city) => [.. _folk.Where(f => f.City == city)];

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static TownFolkTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error, Version);
        LastError = error;
        return snapshot == null ? null : new TownFolkTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var folk = new List<Folk>(Count);
        for (int i = 0; i < Count; i++)
        {
            int row = TableVa + i * RowSize;
            int city = exe.Int(row + 0x00), kind = exe.Int(row + 0x04);
            if (city < 0 || city >= CityExeTable.Count || !Kinds.Contains(kind)) continue;

            string words = exe.Text(exe.Word(row + 0x18)) ?? "";
            if (words.Length == 0) continue;
            uint later = exe.Word(row + 0x1C);

            folk.Add(new Folk(city, kind, exe.Int(row + 0x08), exe.Int(row + 0x0C),
                              exe.Int(row + 0x10), exe.Int(row + 0x14),
                              words, later != 0 ? exe.Text(later) ?? "" : ""));
        }

        if (folk.Count < Count / 2)
        {
            error = "마을 사람 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(folk);
    }
}
