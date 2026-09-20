namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 바람과 해류의 표. CDS_95.EXE 에 박혀 있다.
/// </summary>
/// <remarks>
/// 바다를 <b>50열 x 25행</b>으로 나눈 표 세 장이 전부다. 각 0x9C4(=2500)바이트, 한 칸이 16비트다.
/// <code>
///   0x004CB290  바람 1~6월
///   0x004CBC54  바람 7~12월
///   0x004CC618  해류 (계절을 안 탄다)
/// </code>
/// 게임이 칸을 고르는 자리는 <c>0x00424E50</c> 이다. 함대 원본 좌표(경도 0~40000,
/// 위도 0~20000)를 그대로 800 으로 나눈다 — 한 칸이 지도 50x50 칸(경위도 7.2도)이다.
/// <code>
///   열 = 경도 / 800        (0~49)
///   행 = 위도 / 800        (0~24)
///   색인 = 행 * 50 + 열
/// </code>
/// 한 칸(16비트)의 속.
/// <code>
///   비트 0..3    방위 0~15 (16방위)
///   비트 4..7    세기 바람 1~6 · 해류 0~7
///   비트 8..11   기후대 0~12    세 표가 이 니블만은 완전히 같다
///   비트 12..15  안 쓴다
/// </code>
///
/// <b>방위는 "불어가는 쪽"이다.</b> 게임은 <see cref="Vector"/> 의 (dx, dy) 를 그대로 더해서
/// 물건을 옮긴다(구름 흘리기 <c>0x004893D0</c>). 그러니 방위 0 은 "북에서 오는 바람"이 아니라
/// <b>북으로 미는</b> 바람이다. 기상학 용어와 반대이니 화면에 글자를 쓸 때 조심할 것.
///
/// 게임이 화면에 찍을 때는 <c>방위/2</c> 로 8방위까지만 깎는다(<c>0x0048ABA2</c>). 우리는
/// 16방위를 그대로 쓴다 — 표에 든 값이 그것이다.
///
/// 표는 안 바뀌는 값이라 <see cref="ExeTable"/> 규칙대로 한 번 읽어 적어 두고 다음부터는
/// 그것을 쓴다. 게임이 없는 자리에서도 지도를 그릴 수 있다.
/// </remarks>
public sealed class WindTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\바람표.json</c>).</summary>
    private const string CacheName = "바람표";

    private const int WindFirstHalfVa = 0x004CB290;   // 1~6월
    private const int WindSecondHalfVa = 0x004CBC54;  // 7~12월
    private const int CurrentTableVa = 0x004CC618;
    private const int VectorTableVa = 0x00569558;     // 16방위 (dx, dy), 크기 64
    private const int RingTableVa = 0x00519D30;       // 물결 고리표, 색인 = 타일 >> 7

    /// <summary>표에 칸을 더하면 올린다 — 옛 모양으로 적어 둔 JSON 을 버리게 하는 표다.</summary>
    private const int CacheVersion = 2;

    /// <summary>표의 가로 칸 수.</summary>
    public const int Cols = 50;

    /// <summary>표의 세로 칸 수.</summary>
    public const int Rows = 25;

    /// <summary>표의 칸 수.</summary>
    public const int Count = Cols * Rows;

    /// <summary>한 칸이 덮는 원본 좌표 폭. 지도 칸으로는 50칸, 도로는 7.2도다.</summary>
    public const int CellRaw = 800;

    /// <summary>방위 가짓수. 0=북, 4=서, 8=남, 12=동 으로 <b>반시계</b>다.</summary>
    public const int DirCount = 16;

    /// <summary><see cref="Vector"/> 의 길이. 게임이 이만큼 쌓이면 한 칸을 넘긴다.</summary>
    public const int VectorLength = 64;

    /// <summary>바람 표가 갈리는 마지막 달. 1~6월이 앞 표, 7~12월이 뒤 표다.</summary>
    public const int FirstHalfLastMonth = 6;

    /// <summary>기후대 번호의 최댓값(0~12, 13가지).</summary>
    public const int ZoneMax = 12;

    /// <summary>물결 고리표의 칸 수. 색인이 이보다 크면 고리에 없는 타일이다.</summary>
    public const int RingCount = 21;

    /// <summary>물결 띠의 주기(칸). 이 중 <see cref="RippleBandOn"/> 칸이 켜진다.</summary>
    public const int RipplePeriod = 16;

    /// <summary>물결 띠에서 타일이 갈리는 칸 수. 나머지 8칸은 그대로다.</summary>
    public const int RippleBandOn = 8;

    /// <summary>물결이 갈리는 지형 부류. 이 부류인 그림번호는 <c>0x80</c> 하나뿐이다.</summary>
    public const int RippleClass = 1;

    /// <summary>바람이나 해류 한 칸.</summary>
    /// <param name="Dir">방위 0~15. <b>불어가는 쪽</b>이다.</param>
    /// <param name="Speed">세기. 바람은 1~6, 해류는 0~7.</param>
    /// <param name="Zone">기후대 0~12. 비·눈과 식량·물 조달이 이것으로 갈린다.</param>
    public readonly record struct Flow(int Dir, int Speed, int Zone)
    {
        /// <summary>세기가 0 이면 흐름이 없는 칸이다(뭍이거나 무풍·무해류).</summary>
        public bool IsStill => Speed == 0;
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(ushort[] WindFirstHalf, ushort[] WindSecondHalf,
                                    ushort[] Current, int[] VectorX, int[] VectorY,
                                    ushort[] Ring);

    private readonly Snapshot _data;

    private WindTable(Snapshot data) => _data = data;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static WindTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               CacheVersion);
        LastError = error;
        return snapshot == null ? null : new WindTable(snapshot);
    }

    // ── 칸 고르기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 함대 원본 좌표(경도 0~40000, 위도 0~20000)가 든 칸의 색인. 표 밖이면 -1.
    /// </summary>
    /// <remarks>게임 <c>0x00424E50</c> 이 하는 나눗셈 그대로다.</remarks>
    public static int CellOf(int lonRaw, int latRaw)
    {
        int col = lonRaw / CellRaw;
        int row = latRaw / CellRaw;
        return col < 0 || col >= Cols || row < 0 || row >= Rows ? -1 : row * Cols + col;
    }

    /// <summary>열·행으로 고르는 색인. 표 밖이면 -1.</summary>
    public static int CellOfGrid(int col, int row) =>
        col < 0 || col >= Cols || row < 0 || row >= Rows ? -1 : row * Cols + col;

    /// <summary>그 칸의 왼쪽 위 모서리 원본 좌표(경도, 위도).</summary>
    public static (int LonRaw, int LatRaw) CornerOf(int cell) =>
        (cell % Cols * CellRaw, cell / Cols * CellRaw);

    /// <summary>1~6월이면 앞 표를 쓴다.</summary>
    public static bool IsFirstHalf(int month) => month >= 1 && month <= FirstHalfLastMonth;

    // ── 값 꺼내기 ────────────────────────────────────────────────────────────

    /// <summary>그 칸의 바람. 달로 표 두 장 중 하나를 고른다.</summary>
    public Flow WindAt(int cell, int month) =>
        Unpack(Raw(IsFirstHalf(month) ? _data.WindFirstHalf : _data.WindSecondHalf, cell));

    /// <summary>그 칸의 해류. 계절을 안 탄다.</summary>
    public Flow CurrentAt(int cell) => Unpack(Raw(_data.Current, cell));

    /// <summary>그 칸의 기후대(0~12). 표 밖이면 -1.</summary>
    public int ZoneAt(int cell) => cell < 0 || cell >= Count ? -1 : (_data.Current[cell] >> 8) & 0xF;

    /// <summary>
    /// 방위 하나의 (dx, dy). 길이가 <see cref="VectorLength"/> 이고 <c>dy</c> 는 <b>화면 아래가 +</b>다
    /// (방위 0 = 북 = <c>(0, -64)</c>).
    /// </summary>
    public (int Dx, int Dy) Vector(int dir)
    {
        int d = ((dir % DirCount) + DirCount) % DirCount;
        return (_data.VectorX[d], _data.VectorY[d]);
    }

    // ── 물결 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 물결이 한 칸 돌아간 뒤의 타일 번호표. 색인은 타일 번호(<c>낱말 &amp; 0x3FFF</c>) 그대로이고,
    /// 갈릴 것이 없는 타일은 제 번호가 그대로 들어 있다.
    /// </summary>
    /// <remarks>
    /// 게임은 그리는 칸마다 이렇게 한다(<c>0x0048A4D8</c>~).
    /// <code>
    ///   부류가 1 이고, 띠 안에 들면   타일 = 고리표[타일 >> 7]
    /// </code>
    /// 부류 1 인 그림번호는 <c>0x80</c> 하나뿐이라 바다 칸의 절반만 갈린다. 고리표
    /// (<c>0x00519D30</c>)는 스물한 칸이고, 실제로 갈리는 것은 바다 타일 여섯 쌍뿐이다.
    ///
    /// <b>고리에 없는 타일은 건드리지 않는다.</b> 지도에는 색인이 21 을 넘는 칸도 몇 개씩
    /// 있는데(45~105), 원본은 표 밖을 그대로 읽어 엉뚱한 그림을 낸다. 여기서는 제자리에 둔다 —
    /// 한두 칸짜리 특수 타일이라 눈에 띄지 않고, 표 밖을 읽는 것을 흉내낼 값어치가 없다.
    /// </remarks>
    /// <param name="terrain">지형 부류표. 없으면 그림번호 <c>0x80</c> 으로 대신 가른다.</param>
    public ushort[] BuildRippleTiles(TerrainTable? terrain)
    {
        var next = new ushort[TileCount];
        for (int t = 0; t < TileCount; t++)
        {
            next[t] = (ushort)t;
            int group = t >> 7;
            if (group >= RingCount) continue;

            bool ripples = terrain != null
                ? terrain.ClassOfCell(t) == RippleClass
                : (t & 0xFF) == RippleClassLowByte;
            if (ripples) next[t] = _data.Ring[group];
        }
        return next;
    }

    /// <summary>타일 번호의 가짓수(<c>0x4000</c>). WORLD.CDS 칸의 아래 14비트다.</summary>
    public const int TileCount = 0x4000;

    /// <summary>부류가 1 인 유일한 그림번호. 지형표를 못 열었을 때 대신 쓴다.</summary>
    private const int RippleClassLowByte = 0x80;

    /// <summary>
    /// 물결 띠 안인지. <paramref name="cellX"/>·<paramref name="cellY"/> 는 칸 좌표,
    /// <paramref name="tick"/> 은 흐른 틱 수다.
    /// </summary>
    /// <remarks>
    /// 원본은 <b>화면</b> 격자의 열·행을 쓴다(<c>0x0048A518</c>). 우리는 지도 칸을 쓴다 —
    /// 지도를 밀거나 키워도 물결이 바다에 붙어 있어야 지도로 볼 만하다. 서 있을 때 보이는
    /// 모습은 같다.
    /// </remarks>
    public bool InRippleBand(int dir, int speed, int cellX, int cellY, int tick)
    {
        var (dx, dy) = Vector(dir);
        int p = (int)Math.Floor((dx * (double)cellX + dy * (double)cellY - speed * (double)tick * 16.0)
                                / VectorLength);
        return (p & (RipplePeriod - 1)) < RippleBandOn;
    }

    private static ushort Raw(ushort[] table, int cell) =>
        cell < 0 || cell >= Count ? (ushort)0 : table[cell];

    private static Flow Unpack(ushort word) =>
        new(word & 0xF, (word >> 4) & 0xF, (word >> 8) & 0xF);

    // ── EXE 에서 읽기 ────────────────────────────────────────────────────────

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var first = ReadTable(exe, WindFirstHalfVa);
        var second = ReadTable(exe, WindSecondHalfVa);
        var current = ReadTable(exe, CurrentTableVa);

        // 판이 다른 EXE 를 잘못 읽지 않으려고 표의 생김새를 본다. 어느 한 값이 아니라
        // 세 표가 같이 지켜야 하는 규칙을 보므로 값이 손질된 EXE 에서도 통과한다.
        //   · 위 니블은 안 쓰니 늘 0
        //   · 기후대는 0~12
        //   · 기후대 니블은 세 표가 완전히 같다 — 표는 셋인데 구역 나눔은 하나다
        for (int i = 0; i < Count; i++)
        {
            if ((first[i] >> 12) != 0 || (second[i] >> 12) != 0 || (current[i] >> 12) != 0)
            {
                error = "바람표의 위 니블이 비어 있지 않습니다(다른 판의 EXE 일 수 있습니다)";
                return null;
            }
            int zone = (current[i] >> 8) & 0xF;
            if (zone > ZoneMax || ((first[i] >> 8) & 0xF) != zone || ((second[i] >> 8) & 0xF) != zone)
            {
                error = "바람표와 해류표의 기후대가 어긋납니다(다른 판의 EXE 일 수 있습니다)";
                return null;
            }
        }

        var vx = new int[DirCount];
        var vy = new int[DirCount];
        for (int d = 0; d < DirCount; d++)
        {
            vx[d] = exe.Int(VectorTableVa + d * 8);
            vy[d] = exe.Int(VectorTableVa + d * 8 + 4);
        }

        // 방위 0 은 북(0, -64), 4 는 서(-64, 0) 여야 한다. 반시계인 것까지 여기서 걸린다.
        if (vx[0] != 0 || vy[0] != -VectorLength || vx[4] != -VectorLength || vy[4] != 0)
        {
            error = "방위 벡터표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var ring = new ushort[RingCount];
        for (int i = 0; i < RingCount; i++)
            ring[i] = (ushort)exe.Word(RingTableVa + i * 2);

        // 고리는 스물한 칸이 한 바퀴를 돈다. 첫 칸과 마지막 칸이 서로를 가리키는지만 본다.
        if (ring[1] != 0x0580 || ring[RingCount - 1] != 0x0080)
        {
            error = "물결 고리표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(first, second, current, vx, vy, ring);
    }

    private static ushort[] ReadTable(PeImage exe, int va)
    {
        var table = new ushort[Count];
        for (int i = 0; i < Count; i++)
            table[i] = (ushort)exe.Word(va + i * 2);   // 두 바이트짜리라 아래 16비트만 쓴다
        return table;
    }
}
