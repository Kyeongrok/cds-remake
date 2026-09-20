namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 교역소 표 — 지역마다 무엇을 파는지와 교역품 기준가, 도시마다의 특산품 값.
/// </summary>
/// <remarks>
/// <code>
///   교역품 표  VA 0x004DCBB0 + 종류 x 136 (70종)
///     +0x0C~+0x78  지역 기준가 28칸(지역 0~27)
///   지역 공통품 VA 0x004DF0E0 + 지역 x 20   i32 x 5, -1 이면 끝   (게임 0x0042A160)
///   도시 표    VA 0x004D14B0 + 도시 x 136
///     +0x10,+0x14  연결 내륙도시(-1 없음)   +0x1C 지역   +0x2C 시세 첫값
///     +0x30 특산품 종류   +0x34 특산품 기준가   +0x38 특산품 등급(0~3)
/// </code>
/// 리스본은 지역 0 이라 공통품이 돌소금 · 올리브유 · 총이고 특산품 대포(기준가 155)다 —
/// cds95-mod 가 게임 매각창과 대 본 값(돌소금 22 · 올리브유 18 · 총 80 · 대포 155)과 같다.
/// 지역 28 부터는 표가 아니라 딴 자료라 읽지 않는다.
/// </remarks>
public sealed class TradeTable
{
    private const string CacheName = "교역소표";
    private const int Version = 2;

    private const int GoodsVa = 0x004DCBB0, GoodsRow = 136, PriceOffset = 0x0C;
    private const int CommonVa = 0x004DF0E0, CommonRow = 20;
    private const int CityVa = 0x004D14B0, CityRow = 136;

    /// <summary>기준가가 있는 지역 수.</summary>
    public const int Regions = 28;

    /// <summary>지역 하나가 파는 공통품 칸 수.</summary>
    public const int CommonSlots = 5;

    internal sealed record Snapshot(int[][] Prices, int[][] Common, int[] Region, int[][] Inland,
                                    int[] Special, int[] SpecialPrice, int[] SpecialGrade, int[] Rate,
                                    bool[] Gate);

    /// <summary>
    /// 처음부터 파는 교역품인지(교역품 표 <c>+0x84</c> → 게이트 <c>0x0058BAB0</c>, <c>0x0042E2A0</c>).
    /// </summary>
    /// <remarks>
    /// 쌀 · 후추 · 커피 · 차 · 골동품 · 노예 따위 27종이 꺼진 채 시작하고, 발견 대본 한 명령
    /// (<c>0x004088D8</c>)만 켠다. 켠 것은 <c>Player.ActiveGoods</c> 에 들고, 교역소는 둘을 함께 본다
    /// (<c>TradePost.OnSale</c>).
    /// </remarks>
    public bool OnSale(int kind) => kind >= 0 && kind < _s.Gate.Length && _s.Gate[kind];

    private readonly Snapshot _s;

    private TradeTable(Snapshot s) => _s = s;

    public static string LastError { get; private set; } = "";

    /// <summary>그 지역에서 그 교역품의 기준가. 모르면 0.</summary>
    public int BasePrice(int region, int kind) =>
        kind >= 0 && kind < _s.Prices.Length && region >= 0 && region < Regions
            ? _s.Prices[kind][region] : 0;

    /// <summary>그 지역 공통품(표 차례, 끝의 -1 은 뺀다).</summary>
    public IReadOnlyList<int> CommonOf(int region) =>
        region >= 0 && region < _s.Common.Length ? _s.Common[region] : [];

    /// <summary>도시의 지역(0~27). 모르면 -1.</summary>
    public int RegionOf(int city) => city >= 0 && city < _s.Region.Length ? _s.Region[city] : -1;

    /// <summary>연결 내륙도시들("세비야 → 톨레도").</summary>
    public IReadOnlyList<int> InlandOf(int city) => city >= 0 && city < _s.Inland.Length ? _s.Inland[city] : [];

    /// <summary>도시 제 특산품 종류. 없으면 -1.</summary>
    public int SpecialOf(int city) => city >= 0 && city < _s.Special.Length ? _s.Special[city] : -1;

    /// <summary>특산품 기준가(<c>+0x34</c>).</summary>
    public int SpecialPriceOf(int city) => city >= 0 && city < _s.SpecialPrice.Length ? _s.SpecialPrice[city] : 0;

    /// <summary>특산품 등급(<c>+0x38</c>, 0~3). 뜻은 아직 확정 못 했다.</summary>
    public int SpecialGradeOf(int city) => city >= 0 && city < _s.SpecialGrade.Length ? _s.SpecialGrade[city] : 0;

    /// <summary>시세 첫값(<c>+0x2C</c>). 어디나 100 이다.</summary>
    public int RateOf(int city) => city >= 0 && city < _s.Rate.Length ? _s.Rate[city] : 100;

    public static TradeTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error, Version);
        LastError = error;
        return snapshot == null ? null : new TradeTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var prices = new int[GoodsTable.Count][];
        for (int kind = 0; kind < GoodsTable.Count; kind++)
        {
            prices[kind] = new int[Regions];
            for (int r = 0; r < Regions; r++)
                prices[kind][r] = exe.Int(GoodsVa + kind * GoodsRow + PriceOffset + r * 4);
        }

        var gate = new bool[GoodsTable.Count];
        for (int kind = 0; kind < GoodsTable.Count; kind++)
            gate[kind] = exe.Int(GoodsVa + kind * GoodsRow + 0x84) != 0;

        var common = new int[Regions][];
        for (int r = 0; r < Regions; r++)
        {
            var list = new List<int>(CommonSlots);
            for (int i = 0; i < CommonSlots; i++)
            {
                int kind = exe.Int(CommonVa + r * CommonRow + i * 4);
                if (kind < 0 || kind >= GoodsTable.Count) break;
                list.Add(kind);
            }
            common[r] = [.. list];
        }

        int n = CityExeTable.Count;
        var region = new int[n];
        var inland = new int[n][];
        var special = new int[n];
        var specialPrice = new int[n];
        var grade = new int[n];
        var rate = new int[n];
        for (int city = 0; city < n; city++)
        {
            int row = CityVa + city * CityRow;
            region[city] = exe.Int(row + 0x1C);
            var links = new List<int>(2);
            foreach (int at in (int[])[0x10, 0x14])
            {
                int id = exe.Int(row + at);
                if (id >= 0 && id < n) links.Add(id);
            }
            inland[city] = [.. links];
            int kind = exe.Int(row + 0x30);
            special[city] = kind >= 0 && kind < GoodsTable.Count ? kind : -1;
            specialPrice[city] = exe.Int(row + 0x34);
            grade[city] = exe.Int(row + 0x38);
            rate[city] = exe.Int(row + 0x2C);
        }

        // 리스본: 지역 0, 공통품 돌소금(10)·올리브유(11)·총(21), 특산 대포(22) 155.
        if (region[0] != 0 || special[0] != 22 || specialPrice[0] != 155
            || !common[0].SequenceEqual([10, 11, 21]))
        {
            error = "교역소 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(prices, common, region, inland, special, specialPrice, grade, rate, gate);
    }
}
