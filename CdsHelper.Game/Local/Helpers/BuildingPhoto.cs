using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 MPCG.CDS — 건물에 들어가면 오른쪽 아래에 뜨는 <b>타원 사진</b> 84장.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>21.분석-건물 사진(MPCG.CDS)과 메뉴 껍데기(MISC.CDS)</c> 에 있다.
/// <code>
///   파트 0        320x240   마스크(값 6종, 안 씀)
///   파트 1        320x240   타원 마스크 — 밖 47 / 안 63 / 테두리 7
///   파트 2k+2     320x240   8bpp 색인 (76,800바이트)   ← 사진 k
///   파트 2k+3     768바이트  256색 팔레트              ← 사진 k
/// </code>
/// 사진은 <b>k = 0~83</b> 이고, 어느 것을 낼지는 정적으로 넣은 표
/// (<c>exe-tables\건물사진표.json</c>)에서 <c>(문화권 행, 건물 코드)</c>로 고른다.
///
/// <b>타원은 마스크가 판다.</b> 사진 안에도 비침 색인(64, 팔레트값 252,0,252 마젠타)이
/// 들어 있지만 그것만 믿으면 안 된다 — 타원 꼭대기에 사진이 마젠타로 <i>흐려지는</i>
/// 점들이 섞여 있어서(색인 84·99·122 …가 다 마젠타 계열이다) 테 둘레에 분홍 티가 남는다.
/// 마스크로 자르고, 마스크가 테두리(7)라 한 자리는 팔레트 7 로 덮는다. 팔레트 7 은
/// 사진 84장이 모두 (49,24,24) 로 같다 — 게임 화면의 짙은 붉은 테가 이것이다.
///
/// 팔레트 한 색은 파일에 <b>(파랑, 빨강, 초록)</b> 순으로 적혀 있다.
/// <see cref="CityPictures"/> · <see cref="Portraits"/> 와 같은 관례다.
/// </remarks>
public sealed class BuildingPhoto
{
    public const int Width = 320, Height = 240;
    private const int Pixels = Width * Height;

    /// <summary>타원 마스크 파트와 그 값.</summary>
    private const int MaskPart = 1;
    private const byte OutsideMask = 47, EdgeMask = 7;

    /// <summary>테두리를 칠하는 색인. 사진마다 팔레트 7 이 (49,24,24) 로 같다.</summary>
    private const byte EdgeIndex = 7;

    /// <summary>사진 고르는 표 — 9행(문화권) x 16열(건물 코드), 값 0~83.</summary>
    private const string PickTableName = "건물사진표";
    private const int RowCount = 9, ColCount = 16;

    /// <summary>사진 장수. 파트로는 <c>2k+2</c> · <c>2k+3</c> 두 장씩이다.</summary>
    public const int PhotoCount = 84;

    private readonly Ls12Reader _archive;
    private readonly byte[] _mask;
    private readonly int[] _pick;

    private BuildingPhoto(Ls12Reader archive, byte[] mask, int[] pick)
    {
        _archive = archive;
        _mask = mask;
        _pick = pick;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>MPCG.CDS 와 사진 고르기 표를 함께 연다. 하나라도 어긋나면 null.</summary>
    public static BuildingPhoto? Open(string gameDirectory)
    {
        LastError = "";

        var path = CdsAssetPath.Resolve(gameDirectory, "MPCG.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < PhotoCount * 2 + 2)
        {
            LastError = "MPCG.CDS 에 사진이 모자랍니다";
            return null;
        }

        var mask = archive.Decode(MaskPart);
        if (mask == null || mask.Length < Pixels) { LastError = "타원 마스크를 못 풀었습니다"; return null; }

        var pick = TableCache.Read<PickSnapshot>(PickTableName)?.Data?.Pick;
        if (pick == null || pick.Length != RowCount * ColCount)
        {
            LastError = $"{TableCache.Folder}\\{PickTableName}.json 을 찾지 못했거나 형식이 맞지 않습니다";
            return null;
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다 — 유럽 행은 0,1,2,… 로 간다.
        if (pick[0] != 0 || pick[1] != 1 || pick[2] != 2)
        {
            LastError = "사진 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new BuildingPhoto(archive, mask, pick);
    }

    private sealed record PickSnapshot(int[] Pick);

    /// <summary>
    /// 문화권 이름을 표의 행으로 옮긴다. 게임은 문화권 열하나를 아홉 행으로 접는다 —
    /// <b>유럽 셋(이베리아·북유럽·지중해)이 한 행</b>이라 리스본이든 런던이든 같은 사진이 뜬다.
    /// </summary>
    /// <remarks>
    /// 이름은 <c>cities.json</c> 의 것이다. 게임에 없는 "발칸"(한 도시뿐)은 지중해로 본다.
    /// 모르는 이름은 유럽 행으로 물러선다 — 사진이 안 뜨는 것보다는 낫다.
    /// </remarks>
    public static int RowFor(string? culture) => culture switch
    {
        "아프리카" => 1,
        "이슬람" or "중근동" => 2,
        "인도" => 3,
        "중국" => 4,
        "중앙아시아" => 5,
        "동남아시아" => 6,
        "일본" => 7,
        "아메리카" => 8,
        _ => 0,     // 이베리아 · 북유럽 · 지중해 · 발칸 · 모르는 것
    };

    /// <summary>그 문화권 그 건물의 사진 번호(0~83). 건물 코드가 표 밖이면 -1.</summary>
    public int Pick(string? culture, int buildingCode)
    {
        if (buildingCode < 0 || buildingCode >= ColCount) return -1;
        return _pick[RowFor(culture) * ColCount + buildingCode];
    }

    /// <summary>
    /// 사진 한 장을 320x240 BGRA 로 푼다. 타원 밖은 알파 0 이다. 못 풀면 null.
    /// </summary>
    public uint[]? TryGetBgra(int photo)
    {
        if (photo < 0 || photo >= PhotoCount) return null;

        var idx = _archive.Decode(photo * 2 + 2);
        var pal = _archive.Decode(photo * 2 + 3);
        if (idx == null || idx.Length < Pixels || pal == null || pal.Length < 256 * 3) return null;

        var bgra = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            byte m = _mask[i];
            if (m == OutsideMask) continue;                  // 타원 밖 — 비침 그대로 둔다

            int v = m == EdgeMask ? EdgeIndex : idx[i];
            int k = v * 3;
            byte b = pal[k], r = pal[k + 1], g = pal[k + 2];

            // 마젠타로 찍힌 점은 비침이다. 색인 64 하나만 보면 안 된다 — 타원 꼭대기에
            // 마젠타로 흐려지는 점들이 섞여 있고(색인 84·99·122·149) 마스크는 그 자리를
            // "안" 이라 하므로, 색으로 걸러야 분홍 티가 안 남는다.
            if (r > 150 && b > 150 && g < 100) continue;

            bgra[i] = (uint)(0xFF << 24 | r << 16 | g << 8 | b);
        }
        return bgra;
    }
}
