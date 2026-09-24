using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 술집·여관에 서 있는 손님 그림 146명. <see cref="BuildingPhoto"/> 와 같은 MPCG.CDS 에 있다.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>14.분석-술집 화면과 대사</c> 에 있다.
/// <code>
///   파트 170 + n     손님 n (n = 0~145)   — 읽기 0x0042DDB0 이 파트에 0xAA 를 더한다
///   표 0x0056E3A0    146행 x 12바이트 = (성별, 폭, 높이)   — 크기 재기 0x0049D710
/// </code>
/// 성별은 0 남자 · 1 여자, 폭은 48·56·64·66, 높이는 72·80·88·96·104 가 나온다.
/// 크기가 다 64 의 배수라 <c>길이/64</c> 로 재면 폭이 64 가 아닌 넷이 어긋난 줄무늬가 된다 —
/// 그래서 짐작하지 않고 표를 읽는다.
///
/// <b>색은 사진 팔레트의 앞 64색</b>이다. 팔레트 84개 전부에서 0~63 구간이 한 바이트도
/// 다르지 않아, 아무 사진 팔레트나 써도 손님 색은 같다. <b>비침은 색인 55</b> 다
/// (건물 사진 쪽 64 와 다르다).
///
/// 그림은 <c>asset/guest/guest-000.png</c> ~ <c>guest-145.png</c> 에서 읽는다. 게임 폴더가
/// 없어도 손님이 보이게 미리 뽑아 둔 것이다(<c>tools/extract_tavern_guests.py</c>).
/// 파일이 빠졌으면 예전처럼 MPCG.CDS 에서 그때그때 푼다.
///
/// 문화권마다 쓰는 구간이 정해져 있다(<see cref="Ranges"/>). 게임은 시작을
/// <c>0x0049D580(문화권)</c>, 개수를 <c>0x0049D500(문화권)</c> 이 낸다 — 둘 다 점프표를 쓰는
/// switch 라 값을 읽어 옮겨 적었다.
/// </remarks>
public sealed class TavernGuests
{
    /// <summary>손님이 시작되는 파트. 게임도 손님 번호에 이 값을 더해 읽는다.</summary>
    private const int FirstPart = 170;

    /// <summary>손님 수.</summary>
    public const int Count = 146;

    /// <summary>크기·성별 표(EXE). 146행 x 12바이트.</summary>
    private const int TableVa = 0x0056E3A0;
    private const int RowSize = 12;

    /// <summary>손님 그림의 비침 색인.</summary>
    private const byte Transparent = 55;

    /// <summary>색을 꺼내 올 사진 팔레트 파트. 앞 64색은 어느 것이나 같다.</summary>
    private const int PalettePart = 3;

    /// <summary>한 화면에 서는 손님 수. 게임도 <c>cmp eax,5</c> 로 잘라 낸다(0x0042DB18).</summary>
    public const int MaxOnScreen = 5;

    /// <summary>뽑아 둔 그림이 든 곳.</summary>
    public const string ArtDirectory = "asset/guest";

    /// <summary>손님 한 명.</summary>
    /// <param name="Index">손님 번호(0~145). 파트 번호는 여기에 170 을 더한 것이다.</param>
    /// <remarks>
    /// 레코드 <b>구조체</b>는 빈 생성자가 늘 있어서, 적어 둔 JSON 을 되읽을 때 어느 것을 쓸지
    /// 일러 주지 않으면 값이 전부 0 으로 들어온다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Guest(int Index, bool Female, int Width, int Height);

    /// <summary>문화권 이름 -> 손님 구간(시작, 개수).</summary>
    /// <remarks>
    /// 이베리아·지중해가 같은 구간을 함께 쓴다. 합이 딱 146 이다. 게임에 없는 "발칸"(한 도시)은
    /// 지중해로 본다 — <see cref="BuildingPhoto.RowFor"/> 와 같은 셈이다.
    /// </remarks>
    private static readonly Dictionary<string, (int Start, int Count)> Ranges = new()
    {
        ["북유럽"] = (0, 17),
        ["이베리아"] = (17, 21),
        ["지중해"] = (17, 21),
        ["발칸"] = (17, 21),
        ["인도"] = (38, 17),
        ["중국"] = (55, 13),
        ["아프리카"] = (68, 16),
        ["이슬람"] = (84, 13),
        ["중근동"] = (84, 13),
        ["일본"] = (97, 11),
        ["동남아시아"] = (108, 11),
        ["중앙아시아"] = (119, 11),
        ["아메리카"] = (130, 16),
    };

    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\손님표.json</c>).</summary>
    private const string CacheName = "손님표";

    /// <summary>JSON 으로 적어 두는 알맹이 — 성별과 크기다. 그림은 asset 에 따로 있다.</summary>
    internal sealed record Snapshot(Guest[] Guests);

    /// <summary>MPCG.CDS. asset 에 그림이 다 있으면 안 열어도 된다.</summary>
    private readonly Ls12Reader? _archive;
    private readonly byte[]? _palette;
    private readonly Guest[] _guests;

    private TavernGuests(Guest[] guests, Ls12Reader? archive, byte[]? palette)
    {
        _guests = guests;
        _archive = archive;
        _palette = palette;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 손님 표를 연다. 크기 표는 적어 둔 JSON 에서, 그림은 <c>asset/guest</c> 에서 온다 —
    /// 둘 다 있으면 게임 폴더가 없어도 열린다. MPCG.CDS 는 그림이 빠졌을 때만 쓰므로
    /// 없어도 그만이다.
    /// </summary>
    public static TavernGuests? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        if (snapshot == null) return null;

        // 그림 파일이 다 있으면 CDS 는 아예 안 연다. 하나라도 빠졌을 때만 대비해 열어 둔다.
        Ls12Reader? archive = null;
        byte[]? palette = null;
        if (!ArtComplete(snapshot.Guests) && !string.IsNullOrEmpty(gameDirectory))
        {
            var path = CdsAssetPath.Resolve(gameDirectory, "MPCG.CDS");
            archive = File.Exists(path) ? Ls12Reader.Open(path) : null;
            if (archive is { PartCount: >= FirstPart + Count })
            {
                palette = archive.Decode(PalettePart);
                if (palette is not { Length: >= 64 * 3 }) { archive = null; palette = null; }
            }
            else
            {
                archive = null;
            }
        }

        return new TavernGuests(snapshot.Guests, archive, palette);
    }

    /// <summary>뽑아 둔 그림이 146장 다 있는지.</summary>
    private static bool ArtComplete(Guest[] guests)
    {
        foreach (var g in guests)
            if (!File.Exists(ArtPath(g.Index))) return false;
        return true;
    }

    private static string ArtPath(int index) =>
        Path.Combine(AppContext.BaseDirectory, ArtDirectory, $"guest-{index:D3}.png");

    /// <summary>EXE 에서 성별·크기 표를 읽어 낸다.</summary>
    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var guests = new Guest[Count];
        for (int i = 0; i < Count; i++)
        {
            int row = TableVa + i * RowSize;
            guests[i] = new Guest(i, exe.Int(row) == 1, exe.Int(row + 4), exe.Int(row + 8));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 크기가 말이 되는지 본다.
        if (guests[0].Width is < 32 or > 128 || guests[0].Height is < 48 or > 160)
        {
            error = "손님 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(guests);
    }

    /// <summary>자리 하나 — 그림과 그 자리에 앉은 인물(<paramref name="Person"/>, 없으면 -1).</summary>
    /// <summary>
    /// 자리 하나. <paramref name="Person"/> 은 인물 자리 번호이고, 없으면 -1 이다.
    /// </summary>
    /// <param name="Stranger">
    /// <b>무명 손님</b>인지. 게임은 자리를 네 갈래로 채우는데(<c>0x004A1A90</c>) 마지막
    /// 갈래가 이름 없는 손님이고, 세는 자리에 <c>inc</c> 가 붙어 있어 <b>적어도 하나</b>는
    /// 늘 든다. 이 손님은 고용도 결투도 안 되고 그 고장 소문만 건넨다.
    /// </param>
    public readonly record struct Slot(Guest Art, int Person, bool Stranger = false);

    /// <summary>
    /// 술집에 세울 사람들을 자리 차례대로 고른다. <b>맨 앞(가장 왼쪽)은 여급</b>, 그 뒤는
    /// 세이브에 그 술집으로 적힌 인물, 남는 자리는 지나가는 남자 손님이다.
    /// </summary>
    /// <param name="personKeys">
    /// 그 술집에 앉힐 인물의 고유값(세이브 인물 번호). 그림은 이 값으로 정해지므로
    /// 같은 인물은 늘 같은 모습으로 선다.
    /// </param>
    /// <remarks>
    /// 게임도 이 차례로 짓는다 — 인물이 있는 자리는 인물에서 그림을 얻고
    /// (<c>0x0049D440</c>: 인물 고유값을 그 문화권 남자 수로 나눈 나머지 번째),
    /// 빈 자리는 무작위 남자로 채운다(<c>0x0049D6C0</c>).
    ///
    /// <b>여급만 도시마다 고정이고 지나가는 손님은 들어갈 때마다 바뀐다.</b> 게임도
    /// 빈 자리를 채울 때마다 <c>rand(그 문화권 손님 수)</c> 를 새로 굴린다
    /// (<c>0x0049D6C0</c> → <c>0x0049D630</c>) — 그래서 술집을 여닫으면 서 있는 사람이
    /// 달라진다. 예전에는 <paramref name="seed"/> 로 둘 다 묶어 두어 늘 같았다.
    /// </remarks>
    /// <param name="withMaid">
    /// 맨 앞에 여자를 세울지. <b>술집에만</b> 여급이 선다 — 여관에는 여급이 없어서
    /// 세워 봐야 한잔 사도 아무 일이 없다.
    /// </param>
    /// <summary>
    /// 그 문화권이 쓰는 손님 전부 — 술집에 설 수 있는 얼굴들이다.
    /// </summary>
    /// <remarks>
    /// 자리에 앉히는 것(<see cref="Seat"/>)과 달리 굴리지 않고 구간을 그대로 낸다.
    /// 문화권마다 어떤 얼굴이 있는지 맞대어 볼 때 쓴다.
    /// </remarks>
    public IReadOnlyList<Guest> Of(string? culture)
    {
        if (!Ranges.TryGetValue(culture ?? "", out var range)) range = Ranges["이베리아"];

        var made = new List<Guest>(range.Count);
        for (int i = 0; i < range.Count && range.Start + i < _guests.Length; i++)
            made.Add(_guests[range.Start + i]);
        return made;
    }

    /// <summary>술집에 앉힐 인물 하나 — 인물 번호, 여자인지, 그 사람 <b>나라 수도의</b> 문화권 이름.</summary>
    public readonly record struct Sitter(int Id, bool Female, string? Culture);

    /// <summary>
    /// 인물이 서는 그림(<c>0x0049D440</c>) — 그 사람 <b>나라 수도의 문화권</b>에서 <b>그 사람 성별</b>의 손님 가운데
    /// <c>(4096 + 인물 번호) % 그 수</c> 번째다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   49d448  0x00477EA0(인물) → 나라 형편 칸 → 수도 도시 +0x58   ; 문화권 — 지금 술집 도시가 아니다
    ///   49d45f  vtbl[0x10] 여자인가                                   ; 남자 수 0x0049D600 · 여자 수 0x0049D4C0
    ///   49d468  0x004783C0(인물) = 0x00477AF0(1, 번호) = (1 &lt;&lt; 12) | 번호
    ///   49d483  0x0049D630(문화권, 성별, 나머지)                      ; 그 성별 가운데 나머지 번째
    /// </code>
    /// 예전에는 지금 도시의 문화권 · 늘 남자 · 인물 번호 그대로로 골라 원본과 다른 사람이 섰다.
    /// </remarks>
    private Guest? ArtOf(Sitter who, string? here)
    {
        if (!Ranges.TryGetValue(who.Culture ?? "", out var range)
            && !Ranges.TryGetValue(here ?? "", out range)) range = Ranges["이베리아"];

        var pool = new List<int>();
        for (int i = 0; i < range.Count; i++)
            if (_guests[range.Start + i].Female == who.Female) pool.Add(range.Start + i);
        if (pool.Count == 0) return null;
        return _guests[pool[Mod(PersonKeyBase + who.Id, pool.Count)]];
    }

    /// <summary>인물 고유값의 갈래 몫 — <c>0x00477AF0(1, 번호)</c> 의 <c>1 &lt;&lt; 12</c>.</summary>
    private const int PersonKeyBase = 1 << 12;

    public IReadOnlyList<Slot> Seat(string? culture, int seed, IReadOnlyList<Sitter> persons,
                                    bool withMaid = true)
    {
        if (!Ranges.TryGetValue(culture ?? "", out var range)) range = Ranges["이베리아"];

        var women = new List<int>();
        var men = new List<int>();
        for (int i = 0; i < range.Count; i++)
            (_guests[range.Start + i].Female ? women : men).Add(range.Start + i);
        if (men.Count == 0) return [];

        var seats = new List<Slot>(MaxOnScreen);

        // 여급 — 그 문화권에 여자가 없으면 그냥 건너뛴다(구간이 열한 명뿐인 문화권도 있다).
        // 여급은 그 마을 사람이라 늘 같은 모습이어야 하므로 도시 번호로 고정한다.
        var fixedRng = new Random(seed);
        if (withMaid && women.Count > 0)
            seats.Add(new Slot(_guests[women[fixedRng.Next(women.Count)]], -1));

        // 인물 — 각자 제 그림으로 앉는다. <b>한 자리는 비워 둔다</b> — 무명 손님 몫이다.
        for (int i = 0; i < persons.Count && seats.Count < MaxOnScreen - 1; i++)
            if (ArtOf(persons[i], culture) is { } art) seats.Add(new Slot(art, i));

        // 남는 자리는 무명 손님. 앞서 선 사람과 겹치지 않게 고른다.
        // 무명 손님은 들어갈 때마다 새로 굴린다.
        var rng = new Random();
        var taken = new HashSet<int>();
        foreach (var s in seats) taken.Add(s.Art.Index);
        Shuffle(men, rng);
        foreach (int i in men)
        {
            if (seats.Count >= MaxOnScreen) break;
            if (taken.Add(i)) seats.Add(new Slot(_guests[i], -1, Stranger: true));
        }
        return seats;
    }

    /// <summary>음수가 나오지 않는 나머지. 세이브 번호는 늘 0 이상이지만 눌러 둔다.</summary>
    private static int Mod(int value, int n) => ((value % n) + n) % n;

    private static void Shuffle(List<int> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// 손님 한 명을 BGRA 로 푼다. 크기는 <see cref="Guest.Width"/> x <see cref="Guest.Height"/>
    /// 다. 못 풀면 null.
    /// </summary>
    public uint[]? TryGetBgra(Guest guest)
    {
        int pixels = guest.Width * guest.Height;
        if (pixels <= 0) return null;

        var fromFile = FromAsset(guest);
        if (fromFile != null) return fromFile;

        if (_archive == null || _palette == null) return null;

        var idx = _archive.Decode(FirstPart + guest.Index);
        if (idx == null || idx.Length < pixels) return null;

        var bgra = new uint[pixels];
        for (int i = 0; i < pixels; i++)
        {
            byte v = idx[i];
            if (v == Transparent) continue;
            int k = v * 3;
            // 파일 속 팔레트는 (파랑, 빨강, 초록) 순이다.
            bgra[i] = (uint)(0xFF << 24 | _palette[k + 1] << 16 | _palette[k + 2] << 8 | _palette[k]);
        }
        return bgra;
    }

    /// <summary>
    /// 뽑아 둔 PNG 에서 푼다. 없거나 크기가 표와 다르면 null 을 내어 CDS 쪽으로 물러선다.
    /// </summary>
    /// <remarks>
    /// 크기를 따지는 것은 엉뚱한 그림을 그리지 않기 위해서다 — 폭이 한 점만 어긋나도
    /// 줄이 밀려 알아볼 수 없는 무늬가 된다(폭을 짐작하지 않고 표를 읽는 까닭과 같다).
    /// </remarks>
    private static uint[]? FromAsset(Guest guest)
    {
        string path = ArtPath(guest.Index);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat,
                                               BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != guest.Width || src.PixelHeight != guest.Height) return null;

            var bgra = new uint[guest.Width * guest.Height];
            src.CopyPixels(bgra, guest.Width * 4, 0);
            return bgra;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
