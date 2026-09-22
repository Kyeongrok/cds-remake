using System.Text.Json.Serialization;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 도시 표 — 문화권과 시장에 내놓는 물건.
/// </summary>
/// <remarks>
/// <code>
///   도시 표 VA 0x004D14B0 (파일오프셋 0x0CFAB0), 226행 x 136바이트, .rdata
///   +0x00  이름 ptr("리스본")     +0x04,+0x08  칸 좌표
///   +0x0C  도시가 차지하는 칸 수(2 또는 3) — 다가섰는지 가릴 때 쓴다
///   +0x1C  지역 무리(0~26) — 항구 "마을정보" 가 이 무리 안의 도시를 늘어놓는다
///   +0x10,+0x14  딸린 내륙 도시 번호(-1 = 없음)
///   +0x18  조선소가 만들 줄 아는 선체 비트(0 코그 ~ 7 다우) — <see cref="HullMaskOf"/>
///   +0x64  들어서면 발견되는 발견물 넷(-1 은 빈 칸) — <see cref="DiscoveriesOf"/>
///   +0x20  문화권 (0~10)         +0x24  나라 번호
///   +0x28  규모(처음 값, 0~7)     +0x2C  시세 첫값(어디나 100)
///   +0x30  특산품                 +0x3C  시장 물건 8칸 (i32), 빈 칸은 -1
/// </code>
///
/// <b>+0x10 은 특산품이 아니라 도시 번호다.</b> 값이 -1~225 까지 나오는데 교역품은 70가지뿐이라
/// 교역품일 수가 없다. 세빌리아는 <c>+0x10 = 4</c>(톨레도)고, 게임 도시정보 창에 뜨는 둘째
/// 특산품 "대포" 는 톨레도의 특산품이다. 런던은 <c>+0x10 = 41</c>(요크)이고 요크의 특산품이
/// 철광석이다 — 41번 교역품도 마침 철광석이라 오래도록 틀린 줄 모르고 있었다.
///
/// 시장 칸의 값은 아이템 번호(<see cref="ItemTable"/> 의 색인)다. 리스본은
/// <c>[33, 34, 37, 66]</c> — 나침반·육분의·레이피아·66번이다.
///
/// <b>메모리를 읽을 까닭이 없다.</b> 게임이 돌 때는 도시 레코드(<c>0x005863A8</c> + 도시번호
/// x 92)의 <c>+0x20~+0x3C</c> 에 같은 값이 올라와 있지만, 그것은 이 표를 옮겨 놓은 것이다 —
/// 켜 놓은 게임에서 226곳을 읽어 이 표와 대 보니 <b>한 칸도 다르지 않았다</b>. 게다가
/// 여기는 <c>.rdata</c> 라 놀이 중에 바뀌지도 않는다(시세는 다르다 — 그쪽은 돌아다닌다).
///
/// 도시 <b>이름</b>은 안 읽는다. 앱은 그것을 DB 에서 내고(<see cref="CityTable"/>),
/// 거기서는 사람이 고칠 수도 있기 때문이다. 문화권은 여기서 읽는다 — 여관 값처럼
/// 규칙이 문화권 <b>번호</b>로 표를 타는 자리가 있어서, 이름으로 되짚으면 DB 에서
/// 이름 한 글자만 달라져도 값이 틀어진다.
/// </remarks>
public sealed class CityExeTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\도시표-게임.json</c>).</summary>
    private const string CacheName = "도시표-게임";

    /// <summary>알맹이 모양 판. 지역 무리를 더하며 5 로, 들어서면 발견되는 발견물을 더하며 10 으로 올렸다.</summary>
    private const int Version = 10;

    private const int TableVa = 0x004D14B0;
    private const int RowSize = 136;

    /// <summary>줄 안에서 시장 칸이 시작되는 자리.</summary>
    private const int StockOffset = 0x3C;

    /// <summary>도시 수.</summary>
    public const int Count = 226;

    /// <summary>한 도시가 내놓는 칸 수.</summary>
    public const int Slots = 8;

    /// <summary>빈 칸.</summary>
    private const int Empty = -1;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄 — 리스본.</summary>
    private static readonly int[] Probe = [33, 34, 37, 66];

    /// <summary>줄 안에서 문화권이 놓인 자리.</summary>
    private const int CultureOffset = 0x20;

    /// <summary>규모·나라·특산품이 놓인 자리.</summary>
    /// <remarks>
    /// 규모는 <c>+0x28</c> 이다(0~7). 게임이 놀이를 시작할 때 이 값을 살아 있는 도시 레코드
    /// (<c>0x005863A8</c> + 도시번호 x 92)의 <c>+0x08</c> 로 옮기고, 도시정보 창은 거기서
    /// 읽어 막대로 그린다. <c>+0x0C</c> 는 2 아니면 3 뿐이라 규모가 아니었다.
    /// </remarks>
    private const int ScaleOffset = 0x28, NationOffset = 0x24;
    private const int SpecialOffset = 0x30;

    /// <summary>칸 좌표와 차지하는 칸 수가 놓인 자리.</summary>
    private const int CellXOffset = 0x04, CellYOffset = 0x08, ReachOffset = 0x0C;

    /// <summary>지역 무리가 놓인 자리.</summary>
    private const int RegionOffset = 0x1C;

    /// <summary>
    /// 도시를 <b>지웠을 때 깔 바탕 타일</b> 3×3 이 놓인 자리(<c>+0x74</c>, 낱말 아홉).
    /// </summary>
    /// <remarks>
    /// 지도 자료에는 도시 그림이 박혀 있고, 렌더러(<c>0x0048A1E0</c>)가 그릴 때 그 위에
    /// 이 블록을 덮어 도시를 지운다. <c>[dy*3 + dx]</c> 는 도시 칸에서 오른쪽 dx, 아래
    /// dy 인 칸이고, <c>0xFFFF</c> 는 「안 덮는다」다. 자세한 것은 볼트
    /// <c>75.분석-도시 등장 시기</c>.
    /// </remarks>
    private const int EraseOffset = 0x74, EraseSide = 3;

    /// <summary>
    /// 도시 형편 낱말이 놓인 자리(<c>+0x62</c>). 판을 열 때 도시 레코드 <c>+0x04</c> 로
    /// 옮겨진다(<c>0x004299B7</c>).
    /// </summary>
    /// <remarks>
    /// <b>비트 0</b> 은 「이미 아는 도시」다 — 선 101곳이 죄다 유럽·지중해고, 나머지는
    /// 항해하다 가까이 가야 켜진다(<c>0x0048D983</c>). <b>비트 2</b> 는 「아직 안 세워졌다」다
    /// (<see cref="CityFounding"/>).
    /// </remarks>
    private const int FlagOffset = 0x62;

    /// <summary>
    /// 건물 낱말(<c>+0x60</c>) — 비트 n 이 건물 코드 n(0 항구 · 2 왕궁 · 6 조선소 · 10 성문 · 11 저택 · 12~15 NPC 저택).
    /// 놀이를 열면 도시 레코드 <c>+0x1C</c> 로 옮겨지고(<c>0x00429A1D</c>) 역사 대본이 켜고 끈다.
    /// </summary>
    private const int BuildingOffset = 0x60;

    /// <summary>
    /// 자택 코드. 이 비트는 새 판이 모항에만 세우는 「모항」 표시라(<c>0x0045E449</c>) 건물이 있고 없음과 따로 본다.
    /// </summary>
    public const int HomeCode = 11;

    /// <summary>「이미 아는 도시」 비트와 「아직 안 세워짐」 비트.</summary>
    public const int KnownBit = 1, UnfoundedBit = 4;

    /// <summary>덧씌움 블록의 칸 수.</summary>
    public const int EraseCells = EraseSide * EraseSide;

    /// <summary>「안 덮는다」를 뜻하는 값.</summary>
    public const ushort Keep = 0xFFFF;

    /// <summary>딸린 내륙 도시 번호가 놓인 두 자리.</summary>
    private static readonly int[] InlandOffsets = [0x10, 0x14];

    /// <summary>
    /// 도시에 <b>들어서면 발견되는</b> 발견물 번호 넷이 놓인 자리(<c>+0x64</c>, -1 은 빈 칸).
    /// </summary>
    /// <remarks>
    /// 도시 화면을 열 때(<c>0x004928BB</c>) 네 칸을 차례로 돌며, 열려 있고 아직 못 찾은 것
    /// (<c>0x004AAD20</c>)이면 그 번호의 발견 대본(DISEV.CDS)을 튼다. 인도 문화권 열세 도시가 2(인도),
    /// 테르나테·암보이나가 4(향료제도), 중국 열일곱 도시가 5(중국), 일본 세 도시가 8(지팡그)을 들고
    /// 있고, 예루살렘(215)·테노치티클란·멕시코(192)·쿠스코(193)·툼베스(264)도 하나씩 든다. 지도에
    /// 사각형이 없는(<c>-1</c>) 발견물은 이 길로만 잡힌다.
    /// </remarks>
    private const int DiscoveryOffset = 0x64, DiscoverySlots = 4;

    /// <summary>문화권 수(0~10).</summary>
    public const int CultureCount = 11;

    /// <summary>JSON 으로 적어 두는 알맹이. 바깥 색인이 도시 번호다.</summary>
    /// <param name="Erase">도시마다의 3×3 바탕 타일 — <see cref="EraseCells"/> 칸씩이다.</param>
    internal sealed record Snapshot(int[][] Stock, int[] Cultures, int[] Scales,
                                    int[] Nations, int[][] Specials,
                                    int[] CellX, int[] CellY, int[] Reach, int[] Regions,
                                    ushort[][] Erase, int[] Flags, int[]? Buildings = null,
                                    int[]? HullMasks = null, int[][]? Discoveries = null);

    private readonly int[][] _stock;
    private readonly int[] _cultures;
    private readonly int[] _scales;
    private readonly int[] _nations;
    private readonly int[][] _specials;
    private readonly int[] _cellX, _cellY, _reach, _regions;
    private readonly ushort[][] _erase;
    private readonly int[] _flags;
    private readonly int[] _buildings;
    private readonly int[] _hullMasks;
    private readonly int[][] _discoveries;

    private CityExeTable(Snapshot snapshot)
    {
        _discoveries = snapshot.Discoveries ?? [];
        _hullMasks = snapshot.HullMasks ?? [];
        _buildings = snapshot.Buildings ?? [];
        _stock = snapshot.Stock;
        _cultures = snapshot.Cultures;
        _scales = snapshot.Scales;
        _nations = snapshot.Nations;
        _specials = snapshot.Specials;
        _erase = snapshot.Erase;
        _flags = snapshot.Flags;
        _cellX = snapshot.CellX;
        _cellY = snapshot.CellY;
        _reach = snapshot.Reach;
        _regions = snapshot.Regions;
    }

    /// <summary>
    /// 그 도시가 든 <b>지역 무리</b>(<c>+0x1C</c>, 0~26). 표 밖이면 -1.
    /// </summary>
    /// <remarks>
    /// 이베리아 열넷 · 프랑스 여덟 · 이탈리아 열둘 … 하는 식으로 스물일곱 무리다.
    /// 항구의 "마을정보" 가 <b>같은 무리</b>의 도시만 늘어놓는다(<c>0x004775F0</c>).
    /// 문화권(<c>+0x20</c>)과는 다른 값이다 — 문화권은 열하나뿐이다.
    /// </remarks>
    public int RegionOf(int cityId) =>
        cityId >= 0 && cityId < _regions.Length ? _regions[cityId] : -1;

    /// <summary>같은 무리에 든 도시 번호들. 도시 번호 차례 그대로다.</summary>
    public List<int> InRegion(int region)
    {
        var got = new List<int>();
        if (region < 0) return got;
        for (int city = 0; city < _regions.Length; city++)
            if (_regions[city] == region) got.Add(city);
        return got;
    }

    /// <summary>
    /// 도시가 앉은 칸과 그 도시가 <b>차지하는 칸 수</b>. 표에 없는 번호면 false.
    /// </summary>
    /// <remarks>
    /// <see cref="Reach"/> 는 <c>+0x0C</c> 다 — 2 아니면 3 뿐이라 오래도록 무엇인지 몰랐는데,
    /// 도시에 다가섰는지 가리는 자리(<c>0x0048DA29</c>)가 이 값으로 훑을 범위를 정한다.
    /// 지도에 그려진 도시 그림이 그만큼 넓다.
    /// </remarks>
    public bool TryCell(int cityId, out int x, out int y, out int reach)
    {
        x = y = reach = 0;
        if (cityId < 0 || cityId >= _cellX.Length) return false;
        x = _cellX[cityId];
        y = _cellY[cityId];
        reach = _reach[cityId];
        return true;
    }

    /// <summary>
    /// 처음 규모. 놀이 중에 도시가 자라면 이 값보다 커진다 — 게임이 돌 때의 값은 딴 자리다.
    /// </summary>
    public int ScaleOf(int cityId) =>
        Live?.CityScales.TryGetValue(cityId, out int grown) == true ? grown
        : cityId >= 0 && cityId < _scales.Length ? _scales[cityId] : 0;

    /// <summary>
    /// 놀이 중에 바뀐 값을 든 주인공 — 역사 대본이 바꾼 나라(<see cref="Player.HistoryNations"/>)와
    /// 규모(<see cref="Player.CityScales"/>)가 표 첫값을 덮는다. 편집기처럼 놀이가 없는 자리면 null.
    /// </summary>
    public Player? Live => LivePlayer?.Invoke();

    /// <summary>지금 주인공을 알려 주는 길. 새 판으로 주인공이 갈려도 따라가게 함수로 둔다.</summary>
    public Func<Player>? LivePlayer { get; set; }

    /// <summary>표에 적힌 처음 나라 — 사람이 갈아 둔 것은 들지만 놀이 중 바뀐 것은 안 든다.</summary>
    public int StartNationOf(int cityId)
    {
        int changed = CityNationEdits.Of(cityId);
        if (changed != CityNationEdits.None) return changed;
        return cityId >= 0 && cityId < _nations.Length ? _nations[cityId] : -1;
    }

    /// <summary>
    /// 그 도시를 가진 나라 번호. 모르면 -1.
    /// </summary>
    /// <remarks>
    /// 도시의 <b>언어</b>가 여기서 나온다 — <see cref="NationTable"/> 의 그 나라가 쓰는 말이다.
    /// 도시에 언어를 박아 두지 않은 것은 정복하면 말이 바뀌기 때문이다.
    /// </remarks>
    /// <remarks>사람이 갈아 둔 것이 있으면 그것이 이긴다(<see cref="CityNationEdits"/>).</remarks>
    /// <summary>
    /// 그 도시를 지울 때 깔 바탕 타일 3×3. 못 구하면 빈 배열.
    /// </summary>
    /// <remarks>
    /// <c>[dy*3 + dx]</c> 가 도시 칸에서 오른쪽 dx, 아래 dy 인 칸이고,
    /// <see cref="Keep"/>(0xFFFF)은 「안 덮는다」다.
    /// </remarks>
    public ushort[] EraseOf(int cityId) =>
        cityId >= 0 && cityId < _erase.Length ? _erase[cityId] : [];

    /// <summary>덧씌움 블록의 한 변.</summary>
    public const int EraseWidth = EraseSide;

    /// <summary>
    /// 그 도시에 그 건물이 <b>지금</b> 있는지 — 건물 낱말의 비트 하나. 맵 포인트 목록·도시 그림 누르기·방향키가
    /// 비트가 꺼진 건물을 건너뛴다(<c>0x0049304E</c> · <c>0x00491D40</c> · <c>0x00491981</c>). 그림은 그대로다.
    /// </summary>
    /// <remarks>
    /// 역사 대본이 바꾼 값(<see cref="Player.CityBuildings"/>)이 먼저다. 표를 못 읽은 옛 굽기면 늘 있다고 본다.
    /// 자택(<see cref="HomeCode"/>)은 모항 표시라 여기서 가리지 않는다.
    /// </remarks>
    public bool HasBuilding(int cityId, int code)
    {
        if (code == HomeCode || code is < 0 or > 15) return true;
        int word;
        if (Live?.CityBuildings.TryGetValue(cityId, out int changed) == true) word = changed;
        else if (cityId >= 0 && cityId < _buildings.Length) word = _buildings[cityId];
        else return true;
        return (word >> code & 1) != 0;
    }

    /// <summary>표에 적힌 처음 건물 낱말. 모르면 -1.</summary>
    public int StartBuildingsOf(int cityId) =>
        cityId >= 0 && cityId < _buildings.Length ? _buildings[cityId] : -1;

    /// <summary>
    /// 그 도시 조선소가 <b>만들 줄 아는</b> 선체 비트(<c>+0x18</c>, 비트 n = 선체 n). 표 밖이면 0.
    /// 무엇을 지금 파는지는 규모와 해가 더 가린다(<see cref="Engine.Town.ShipyardStock"/>).
    /// </summary>
    public int HullMaskOf(int cityId) =>
        cityId >= 0 && cityId < _hullMasks.Length ? _hullMasks[cityId] : 0;

    /// <summary>
    /// 그 도시에 <b>들어서면 발견되는</b> 발견물 번호들(<c>+0x64</c>, 표 차례 그대로). 빈 칸은 빼고 낸다.
    /// </summary>
    public IReadOnlyList<int> DiscoveriesOf(int cityId) =>
        cityId >= 0 && cityId < _discoveries.Length ? _discoveries[cityId] : [];

    /// <summary>그 도시의 형편 낱말(<c>+0x62</c>). 범위 밖이면 0.</summary>
    public int FlagsOf(int cityId) =>
        cityId >= 0 && cityId < _flags.Length ? _flags[cityId] : 0;

    /// <summary>
    /// 놀이를 켤 때부터 <b>아는 도시</b>인지 — 유럽·지중해 101곳이 그렇다.
    /// </summary>
    public bool KnownAtStart(int cityId) => (FlagsOf(cityId) & KnownBit) != 0;

    public int NationOf(int cityId)
    {
        // 역사 대본이 넘긴 나라가 먼저다(1492년 그라나다 → 에스파니아 따위).
        if (Live?.HistoryNations.TryGetValue(cityId, out int now) == true) return now;
        int changed = CityNationEdits.Of(cityId);
        if (changed != CityNationEdits.None) return changed;
        return cityId >= 0 && cityId < _nations.Length ? _nations[cityId] : -1;
    }

    /// <summary>그 도시의 특산품(교역품 종류). 제 것에 딸린 내륙 도시 것을 더해 최대 셋이다.</summary>
    /// <remarks>
    /// 항구 61곳이 내륙 도시 하나를, 25곳이 둘을 끼고 있다(세빌리아 - 톨레도,
    /// 런던 - 요크, 함부르크 - 마그데부르크·아우크스부르크 …). 끼인 도시가 또 남을 끼는
    /// 일은 없고, 두 항구가 함께 끼는 것은 시라즈(바스라·호르무즈) 하나뿐이다.
    /// </remarks>
    public IReadOnlyList<int> SpecialsOf(int cityId) =>
        cityId >= 0 && cityId < _specials.Length ? _specials[cityId] : [];

    /// <summary>
    /// 그 도시의 문화권 번호(0~10). 모르는 도시면 -1.
    /// </summary>
    /// <remarks>
    /// 0 이베리아 · 1 북유럽 · 2 지중해 · 3 아프리카 · 4 이슬람 · 5 인도 · 6 중국 ·
    /// 7 중앙아시아 · 8 동남아시아 · 9 일본 · 10 아메리카.
    ///
    /// 사람이 갈아 둔 것이 있으면 그것이 이긴다(<see cref="CityCultureEdits"/>) — 도구
    /// 창에서 세빌리아를 이슬람으로 갈면 그 마을 조선소에 앉는 얼굴까지 따라 바뀐다.
    /// EXE 는 그대로고, 앱이 읽은 값만 덮는 것이다.
    /// </remarks>
    public int CultureOf(int cityId)
    {
        int changed = CityCultureEdits.Of(cityId);
        if (changed != CityCultureEdits.None) return changed;
        return cityId >= 0 && cityId < _cultures.Length ? _cultures[cityId] : -1;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그 도시가 내놓는 아이템 번호들. 빈 칸(-1)은 빼고 낸다 — 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<int> Of(int cityId) =>
        cityId >= 0 && cityId < _stock.Length ? _stock[cityId] : [];

    /// <summary>물건을 하나라도 내놓는 도시 수.</summary>
    public int CitiesWithStock => _stock.Count(s => s.Length > 0);

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static CityExeTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               Version);
        LastError = error;
        return snapshot == null ? null : new CityExeTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var stock = new int[Count][];
        var cultures = new int[Count];
        var scales = new int[Count];
        var nations = new int[Count];
        var specials = new int[Count][];
        var cellX = new int[Count];
        var cellY = new int[Count];
        var reach = new int[Count];
        var regions = new int[Count];
        var erase = new ushort[Count][];
        var flags = new int[Count];
        var buildings = new int[Count];
        var hullMasks = new int[Count];
        var discoveries = new int[Count][];
        for (int city = 0; city < Count; city++)
        {
            int row = TableVa + city * RowSize;

            var found = new List<int>(DiscoverySlots);
            for (int k = 0; k < DiscoverySlots; k++)
            {
                int id = exe.Int(row + DiscoveryOffset + k * 4);
                if (id >= 0) found.Add(id);
            }
            discoveries[city] = [.. found];

            // 도시를 지웠을 때 깔 바탕 타일 3×3.
            var block = new ushort[EraseCells];
            for (int i = 0; i < EraseCells; i++)
                // Word 는 dword 를 읽으므로 아래 낱말만 떼어 쓴다.
                block[i] = (ushort)(exe.Word(row + EraseOffset + i * 2) & 0xFFFF);
            erase[city] = block;
            flags[city] = (int)(exe.Word(row + FlagOffset) & 0xFFFF);
            buildings[city] = (int)(exe.Word(row + BuildingOffset) & 0xFFFF);
            hullMasks[city] = (int)(exe.Word(row + 0x18) & 0xFFFF);

            cellX[city] = exe.Int(row + CellXOffset);
            cellY[city] = exe.Int(row + CellYOffset);
            reach[city] = exe.Int(row + ReachOffset);
            regions[city] = exe.Int(row + RegionOffset);
            int culture = exe.Int(row + CultureOffset);
            cultures[city] = culture >= 0 && culture < CultureCount ? culture : -1;
            scales[city] = exe.Int(row + ScaleOffset);
            nations[city] = exe.Int(row + NationOffset);

            // 제 것이 앞, 딸린 내륙 도시 것이 뒤다 — 게임 도시정보 창도 그 차례로 낸다.
            var made = new List<int>(3);
            void Add(int at)
            {
                int g = exe.Int(at + SpecialOffset);
                if (g >= 0 && g < GoodsTable.Count && !made.Contains(g)) made.Add(g);
            }
            Add(row);
            foreach (int at in InlandOffsets)
            {
                int inland = exe.Int(row + at);
                if (inland >= 0 && inland < Count) Add(TableVa + inland * RowSize);
            }
            specials[city] = [.. made];

            var slots = new List<int>(Slots);
            for (int s = 0; s < Slots; s++)
            {
                int id = exe.Int(TableVa + city * RowSize + StockOffset + s * 4);
                // 0 은 잠수폭탄이라 시장에 안 나온다. 게임도 0 이하는 빈 칸으로 본다.
                if (id > 0 && id < ItemTable.Count) slots.Add(id);
                else if (id != Empty && id != 0) { /* 표 밖의 값은 조용히 버린다 */ }
            }
            stock[city] = [.. slots];
        }

        if (!stock[0].SequenceEqual(Probe))
        {
            error = "시장 물건 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(stock, cultures, scales, nations, specials, cellX, cellY, reach,
                            regions, erase, flags, buildings, hullMasks, discoveries);
    }
}
