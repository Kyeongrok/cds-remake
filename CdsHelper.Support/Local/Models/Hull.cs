using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 조선소에서 살 수 있는 선체 한 종류. 값은 게임 화면(조선소 → 구입)에서 그대로 옮겼다.
/// </summary>
/// <param name="Name">선체명.</param>
/// <param name="Hp">내구력.</param>
/// <param name="Speed">추진력.</param>
/// <param name="Capacity">적재용량.</param>
/// <param name="Tonnage">적재중량.</param>
/// <param name="Crew">필요승인.</param>
/// <param name="Guns">대포수.</param>
/// <param name="Price">값(닢). 선체 표 <c>+0x38</c> 계수의 1000배다.</param>
/// <param name="Skin">
/// 배 그림 벌(0~3). <c>asset/ship-g0</c> ~ <c>ship-g3</c> 와 짝이고, 큰 배일수록 큰 번호다.
/// <see cref="SpriteFolder"/> 를 준 배는 이 값을 안 본다.
/// </param>
/// <param name="MaxMasts">세울 수 있는 마스트 수(1~3).</param>
/// <param name="CanChangeSail">돛 종류를 바꿀 수 있는 배인지.</param>
/// <param name="SpriteFolder">
/// 8방향 그림이 든 폴더의 온 경로. 등록해 넣은 배가 제 그림을 들고 다니는 자리다.
/// null 이면 <see cref="Skin"/> 대로 <c>asset/ship-g*</c> 에서 읽는다.
/// </param>
/// <param name="Id">
/// 게임 선체 번호(<c>0x004FC1E0</c> 차례, <see cref="Table"/>). 등록해 넣은 배는 −1 이다.
/// <b>세이브는 선체를 이름으로 적으므로</b> 이 칸을 더해도 옛 세이브가 안 깨진다.
/// </param>
/// <param name="HpTop">
/// 개조로 갈 수 있는 <b>내구 한계</b>(선체 표 <c>+0x18</c>). 0 이면 등록해 넣은 배라
/// <see cref="Ship.RefitCeiling"/> 배로 갈음한다. <paramref name="SpeedTop"/>(<c>+0x10</c>) ·
/// <paramref name="CapacityTop"/>(<c>+0x28</c>) · <paramref name="TonnageTop"/>(<c>+0x20</c>) ·
/// <paramref name="GunsTop"/>(<c>+0x30</c>) 도 같다.
/// </param>
/// <param name="Movie">
/// 선체 동영상 줄기 — 원본 여덟 척(<c>S00</c>~<c>S07</c>)은 이름으로 찾으므로 안 적는다. 앱에 같이 싣는 배가
/// 제 동영상을 쓸 때 적는다(바사는 <c>S08_0001</c>). 동영상 은행 · 릴리즈 <c>movie-assets</c> 에서 찾는다.
/// </param>
/// <param name="Cities">
/// 이 배를 파는 조선소의 도시 번호. null 이면 <b>모든 조선소</b>(손으로 등록한 배의 예전 그대로), 비어 있으면 <b>어디서도 안 판다</b> —
/// 앱에 실어만 두고 팔 곳은 나중에 잇는다(<see cref="SoldIn"/>).
/// </param>
public sealed record Hull(
    string Name, int Hp, int Speed, int Capacity, int Tonnage, int Crew, int Guns, int Price,
    int Skin, int MaxMasts = 3, bool CanChangeSail = true, string? SpriteFolder = null, int Id = -1,
    int HpTop = 0, int SpeedTop = 0, int CapacityTop = 0, int TonnageTop = 0, int GunsTop = 0,
    string? Movie = null, int[]? Cities = null)
{
    /// <summary>그 도시 조선소가 이 배를 파는지 — 원본 선체(번호 0 ~ 7)는 도시 표가 정하므로 여기서는 붙인 배만 본다.</summary>
    public bool SoldIn(int city) => Cities is null || Cities.Contains(city);

    /// <summary>어디서도 안 파는 배인지 — 앱에 실어만 두었다.</summary>
    public bool SoldNowhere => Cities is { Length: 0 };

    /// <summary>내구를 개조로 올릴 수 있는 데까지(선체 표 <c>+0x18</c>).</summary>
    public int HpCeiling => HpTop > 0 ? HpTop : Hp * Ship.RefitCeiling;

    /// <summary>추진력 한계(<c>+0x10</c>).</summary>
    public int SpeedCeiling => SpeedTop > 0 ? SpeedTop : Speed * Ship.RefitCeiling;

    /// <summary>적재용량 한계(<c>+0x28</c>).</summary>
    public int CapacityCeiling => CapacityTop > 0 ? CapacityTop : Capacity * Ship.RefitCeiling;

    /// <summary>적재중량 한계(<c>+0x20</c>).</summary>
    public int TonnageCeiling => TonnageTop > 0 ? TonnageTop : Tonnage * Ship.RefitCeiling;

    /// <summary>포탑 한계(<c>+0x30</c>).</summary>
    public int GunsCeiling => GunsTop > 0 ? GunsTop : Guns * Ship.RefitCeiling;

    /// <summary>게임 선체 번호.</summary>
    public const int Cog = 0, Caravel = 1, LargeCaravel = 2, Carrack = 3, LargeCarrack = 4,
                     HeavyCarrack = 5, Galleon = 6, Dhow = 7;

    /// <summary>
    /// 선체표 한 줄(<c>0x004FC1E0</c>, 64바이트) — <b>아래값</b>만 옮겼다(대포는 위값도).
    /// </summary>
    /// <param name="Crew">필요승원 — 표 <c>+0x34</c> 에 10 을 더한 값.</param>
    /// <param name="GunsMin">대포 아래값(<c>+0x2C</c>).</param>
    /// <param name="GunsMax">대포 위값(<c>+0x30</c>). 적 배 대포 수를 여기서 자른다.</param>
    /// <param name="PriceFactor">값 계수(<c>+0x38</c>, x1000).</param>
    /// <param name="SailBits">돛 비트(<c>+0x3C</c>, 2비트 x 3 — 메인·세브·선미).</param>
    public readonly record struct Spec(
        int Id, string Name, int Speed, int Hp, int Tonnage, int Capacity,
        int GunsMin, int GunsMax, int Crew, int PriceFactor, int SailBits)
    {
        /// <summary>돛 비트를 마스트 셋의 돛(0 없음 · 1 삼각 · 2 사각)으로 푼다.</summary>
        public int[] Sails => [SailBits & 3, (SailBits >> 2) & 3, (SailBits >> 4) & 3];
    }

    /// <summary>
    /// 선체표 여덟 줄 그대로다(<c>0x004FC1E0</c>, 볼트 <c>92.분석-적 함대 배 짜기</c> 6절).
    /// </summary>
    /// <remarks>
    /// 조선소에 내는 것은 여전히 <see cref="Builtin"/> 다섯이다 — 이 표는 적 함대를 짓고
    /// 해전 그림(SCOMBAT 파트 5+번호)을 고르는 데 쓴다. 번호가 곧 차례라 바꾸면 안 된다.
    /// 값은 <c>exe-tables/hulls.json</c>(<see cref="HullTable"/>)에서 읽는다 — 없으면 <see cref="Defaults"/> 다.
    /// </remarks>
    public static Spec[] Table => _table ??=
        [.. Rows.Select(r => new Spec(r.Id, r.Name, r.Speed, r.Hp, r.Tonnage, r.Capacity,
                                      r.GunsMin, r.GunsMax, r.Crew, r.Price / 1000, r.SailBits))];

    private static Spec[]? _table;
    private static HullTable.Row[]? _rows;
    private static Hull[]? _builtin;

    /// <summary>선체 여덟 줄 — <c>exe-tables/hulls.json</c> 이 있으면 그것, 없으면 <see cref="Defaults"/>.</summary>
    public static HullTable.Row[] Rows => _rows ??= HullTable.Load() ?? Defaults;

    /// <summary>
    /// 코드에 적어 둔 선체 여덟 줄 — 원본 선체표(<c>0x004FC1E0</c>)의 아래값 · 위값(개조 한계)과 조선소 다섯의 마스트 · 돛종류 · 그림 벌.
    /// <c>exe-tables/hulls.json</c> 이 없거나 깨졌을 때만 쓴다.
    /// </summary>
    /// <remarks>
    /// 마스트는 코구 · 다우가 표의 돛 수(못 늘린다, <c>0x00494A50</c>), 카라벨 둘, 그 밖 셋이다.
    /// 돛종류는 카락 이상만 바꾼다 — 코구 · 카라벨 · 대형카라벨 · 다우는 안 된다(<c>0x00494E00</c>: 선체 0~2·7).
    /// </remarks>
    public static readonly HullTable.Row[] Defaults =
    [
        //  번호          이름          내구 추진 용량  중량 승원 대포↓ 대포↑ 값      돛   한계: 내구 추진 용량 중량 대포  마스트 돛바꿈 그림 조선소
        new(Cog,          "코구",         30, 70, 125, 1250, 10,  0,  5,   7000,  2,  35,  90, 150, 1750,  5,  1, false, 0, false),
        new(Caravel,      "카라벨",       20, 80, 125, 1250, 15,  2,  8,  10000,  1,  30, 100, 225, 2000,  8,  2, false, 1, true),
        new(LargeCaravel, "대형카라벨",   35, 50, 250, 2000, 30,  8, 14,  40000,  5,  50,  70, 300, 2750, 14,  3, false, 1, true),
        new(Carrack,      "카락",         30, 60, 200, 1750, 20,  6, 12,  50000,  6,  50,  80, 275, 2500, 12,  3, true,  2, true),
        new(LargeCarrack, "대형카락",     40, 50, 300, 2500, 35, 10, 20, 100000, 10,  60,  70, 375, 3250, 20,  3, true,  2, false),
        new(HeavyCarrack, "중카락",       60, 35, 400, 4000, 45, 24, 32, 180000, 26,  80,  55, 500, 5000, 32,  3, true,  2, true),
        new(Galleon,      "갤리온",       70, 55, 375, 3500, 40, 24, 40, 250000, 26, 100,  75, 500, 5000, 40,  3, true,  3, true),
        new(Dhow,         "다우",         30, 70, 200, 1750, 25,  8, 12,  60000,  5,  50,  90, 300, 2500, 12,  2, false, 0, false),
    ];

    /// <summary>선체 줄을 선체로 — 대포는 아래값으로 낸다.</summary>
    private static Hull FromRow(HullTable.Row r) =>
        new(r.Name, r.Hp, r.Speed, r.Capacity, r.Tonnage, r.Crew, r.GunsMin, r.Price, r.Skin,
            MaxMasts: Math.Clamp(r.MaxMasts, 1, MastLimit), CanChangeSail: r.CanChangeSail, Id: r.Id,
            HpTop: r.HpTop, SpeedTop: r.SpeedTop, CapacityTop: r.CapacityTop, TonnageTop: r.TonnageTop, GunsTop: r.GunsTop);

    /// <summary>
    /// 선체표의 <b>위값</b>(개조 한계) — 내구 <c>+0x18</c> · 추진 <c>+0x10</c> · 용량 <c>+0x28</c> · 중량 <c>+0x20</c> · 대포 <c>+0x30</c>.
    /// </summary>
    /// <remarks>
    /// 조선소에 안 나오는 선체(코구 · 대형카락 · 다우)를 <see cref="FromTable"/> 로 지을 때 쓴다. 예전에는 이 값을
    /// 안 넣어 한계가 「아래값 x <see cref="Ship.RefitCeiling"/>」로 물러나, 코구 추진 한계가 원본 90 이 아니라 140 이었다.
    /// </remarks>
    // 위값은 이제 선체 줄(<see cref="Rows"/>)의 …Top 칸이다.

    /// <summary>
    /// 이 배의 게임 선체 번호 — 해전 그림 벌(SCOMBAT 파트 5+번호)이 이것이다(<c>0x00442D93</c>).
    /// </summary>
    /// <remarks>
    /// 붙박이는 <see cref="Id"/> 를 들고 있다(고쳐 이름을 바꿔도 <c>with</c> 로 남는다).
    /// 등록해 넣은 배는 번호가 없어 이름이 표에 있으면 그것을, 없으면 지도 그림벌
    /// (<see cref="Skin"/> 0 코구 · 1 카라벨 · 2 카락 · 3 갤리온)로 어림한다 — 어림은 우리 것이다.
    /// </remarks>
    public int GameId
    {
        get
        {
            if (Id is >= 0 and < 8) return Id;
            int byName = Array.FindIndex(Table, s => s.Name == Name);
            if (byName >= 0) return byName;
            return Skin switch { 0 => Cog, 2 => Carrack, 3 => Galleon, _ => Caravel };
        }
    }

    /// <summary>선체 번호 → 지도 그림 벌(<c>0x005695D8</c>).</summary>
    private static readonly int[] TableSkins = [0, 1, 1, 2, 2, 2, 3, 0];

    /// <summary>
    /// 게임 선체 번호의 선체 — 해전에서 빼앗은 배(<c>0x00434D30</c>)가 이것으로 함대에 든다.
    /// </summary>
    /// <remarks>
    /// 조선소 선체(<see cref="All"/>)에 같은 이름이 있으면 그것을 쓴다. 코구·대형카락·다우처럼 조선소에
    /// 안 나오는 선체는 선체표 아래값으로 짓는다 — 값은 계수의 1000배, 마스트는 코구·다우가 표의 돛 수
    /// (못 늘린다, <c>0x00494A50</c>), 카라벨 둘, 그 밖 셋이다.
    /// </remarks>
    public static Hull FromTable(int id)
    {
        var row = Rows[Math.Clamp(id, 0, Rows.Length - 1)];
        if (All.FirstOrDefault(h => h.Name == row.Name) is { } known) return known;
        return FromRow(row);
    }

    /// <summary>선체표 이름으로 찾는다. 없으면 null — 세이브를 되돌릴 때 조선소에 없는 선체를 살린다.</summary>
    public static Hull? FromTableName(string name)
    {
        int at = Array.FindIndex(Table, s => s.Name == name);
        return at < 0 ? null : FromTable(at);
    }

    /// <summary>
    /// 마스트 자리 수. 원본도 셋이 끝이다.
    /// </summary>
    /// <remarks>
    /// 위 <see cref="MaxMasts"/> 의 기본값에는 이 이름을 못 쓴다 — 매개변수 목록이 몸통보다
    /// 먼저 풀리기 때문이다. 그래서 거기만 3 을 그대로 적었다.
    /// </remarks>
    public const int MastLimit = 3;

    /// <summary>등록해 넣은 배인지 — 그림을 제 폴더에서 읽는 배다.</summary>
    public bool IsRegistered => SpriteFolder != null;

    /// <summary>
    /// 살 수 있는 다섯 종류. 게임 표에 나오는 차례 그대로다(위가 큰 배).
    /// </summary>
    /// <remarks>
    /// <b>값은 선체 표에서 읽어 왔다.</b> 예전에는 아래에서부터 100닢씩 올려 어림으로
    /// 넣어 두었는데(카라벨 100닢), 원본 값과 자릿수가 달랐다. 표
    /// (<c>0x004FC1E0</c>, 64바이트 x 8)의 <c>+0x38</c> 이 계수이고 구입값이 그 <b>1000배</b>다 —
    /// <c>0x0044B450</c> 이 <c>[표+0x38]</c> 을 읽어 <c>5c → 25c → 125c → shl 3</c> 으로 민다.
    /// <code>
    ///   코구 7  카라벨 10  대형카라벨 40  카락 50  대형카락 100  중카락 180  갤리온 250  다우 60
    /// </code>
    /// 나머지 값도 같은 표에서 왔다 — 내구 <c>+0x14</c> · 추진 <c>+0x0C</c> · 용량 <c>+0x24</c> ·
    /// 중량 <c>+0x1C</c> · 대포 <c>+0x2C</c> 는 <b>아래값</b>이고(윗값이 그 옆 칸이다),
    /// 필요승인은 <c>+0x34</c> 에 10 을 더한 것이다.
    /// </remarks>
    /// <remarks>
    /// <see cref="Skin"/> 은 원본의 선체→그림 표(<c>0x005695D8</c>)를 그대로 옮긴 것이다.
    /// 배 아틀라스(<c>0x005D68C8</c>, 48x48 x 8방향 x 넉 벌)에서 어느 벌을 뜨는지가 이 값이다
    /// (<c>0x0048A9A2</c> 가 <c>표[선체] * 9 &lt;&lt; 11</c> 로 자리를 짚는다).
    /// <code>
    ///   선체   0 코구  1 카라벨  2 대형카라벨  3 카락  4 대형카락  5 중카락  6 갤리온  7 다우
    ///   그림   0       1         1            2       2           2         3        0
    /// </code>
    /// 카라벨을 0 으로 두었던 것은 <b>틀렸다</b> — 0 은 코구·다우 벌이라 바다에 다우선이
    /// 떴다. 카라벨은 대형카라벨과 같은 1 이다.
    /// </remarks>
    /// <remarks>
    /// 원본에서는 해가 가고 기술이 오르면 살 수 있는 선체가 늘지만, 여기서는 이 다섯을
    /// 고정으로 낸다.
    /// </remarks>
    public static Hull[] Builtin => _builtin ??=
        [.. Rows.Where(r => r.Shipyard).OrderByDescending(r => r.Price).Select(FromRow)];

    private static Hull[]? _all;

    /// <summary>
    /// 조선소에 낼 선체 전부 — 붙박이 다섯에 등록해 넣은 배를 얹은 것이다. 값이 비싼 쪽이 위다.
    /// </summary>
    /// <remarks>
    /// 처음 볼 때 한 번 읽고 들고 있는다. 배를 등록·고침·지운 뒤에는 <see cref="Reload"/> 로 버린다.
    /// </remarks>
    public static Hull[] All => _all ??= ShipRegistry.BuildHulls();

    /// <summary>들고 있던 선체 목록을 버린다 — 다음에 볼 때 다시 읽는다.</summary>
    public static void Reload() => (_all, _rows, _table, _builtin) = (null, null, null, null);

    /// <summary>
    /// 처음에 타고 시작하는 배(카라벨).
    /// </summary>
    /// <remarks>
    /// 붙박이 중에서 고른다 — 등록해 넣은 배가 더 싸다고 해서 시작하는 배까지 바뀌면 곤란하다.
    /// </remarks>
    public static Hull Cheapest => Builtin[^1];

    /// <summary>
    /// 조선소가 되사 주는 값 — 산 값의 <b>6할</b>이다. 시세는 부르는 쪽이 먹인다.
    /// </summary>
    /// <remarks>
    /// 원본은 선체 표(<c>0x004FC1E0</c>, 64바이트)의 <c>+0x38</c> 한 값으로 둘 다 낸다.
    /// <code>
    ///   구입  0x0044B450   c * 1000   (5c → 25c → 125c → &lt;&lt;3)
    ///   매각  0x00423A30   c *  600   (5c → 25c → 75c → 375c → &lt;&lt;4 → /10)
    /// </code>
    /// 그래서 매각은 늘 구입의 6할이고, 그 뒤에 도시 시세를 먹인다(<c>0x00429DC0</c> —
    /// <c>값 x 시세 / 100</c>, 적어도 1닢).
    ///
    /// <b>절대값은 원본과 다르다.</b> 원본의 <c>+0x38</c> 은 코구 7 · 카라벨 10 ·
    /// 대형카라벨 40 · 카락 50 · 대형카락 100 · 중카락 180 · 갤리온 250 이라 구입값이
    /// 만 닢에서 이십오만 닢까지다. 여기 <see cref="Price"/> 는 조선소 화면에서 옮긴
    /// 100~500 짜리 사다리라 자릿수가 다르다 — 비율만 원본 것을 쓴다.
    /// </remarks>
    public int SellPrice => Price * SellPercent / 100;

    /// <summary>되사 주는 비율(%). 원본의 600/1000 이다.</summary>
    public const int SellPercent = 60;
}
