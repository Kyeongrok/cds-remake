using System.IO;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 해전 화면 그림 — <c>SCOMBAT.CDS</c> 에서 미리 뽑아 둔 <c>asset/scombat</c> 을 읽는다.
/// </summary>
/// <remarks>
/// 조각 크기는 짐작한 것이 아니라 EXE 가 찍는 자리에서 그대로 나온 값이다. 자세한 것은
/// 볼트 <c>61.분석-해전 그림(SCOMBAT.CDS)</c> 과 <c>tools/extract_scombat.py</c> 에 있다.
/// <code>
///   sea-00            800x600  바다 바탕
///   ship0-00 ~ 7-11   48x48    배 여덟 벌 x 열두 방향
///   cell-00 · 01      48x32    칸(마름모) — 빈 칸 · 짚은 칸
///   blast-00 ~ 17     48x48    폭발 · 불길 · 잔해
///   mark-00 ~ 09      32x32    방향 화살표 · 작은 배 · 문장
///   bar-a-00 …        640x32   상단 정보 띠
/// </code>
/// <b>격자 한 칸은 32점</b>이다 — 게임이 칸 좌표에 <c>shl eax, 5</c> 를 먹인다.
/// </remarks>
public sealed class CombatArt
{
    /// <summary>뽑아 둔 그림이 든 곳.</summary>
    public const string ArtDirectory = "asset/scombat";

    /// <summary>바다 바탕의 크기. 해전 판만은 넓은 화면으로 돈다.</summary>
    public const int SeaWidth = 800, SeaHeight = 600;

    /// <summary>배 한 장의 크기와 방향 수.</summary>
    public const int ShipSize = 48, Ways = 12;

    /// <summary>배 벌 수 — 파트 5~12 여덟이다.</summary>
    public const int Fleets = 8;

    /// <summary>격자 한 칸(<c>shl eax, 5</c>).</summary>
    public const int Cell = 32;

    private readonly string _dir;

    private CombatArt(string dir) => _dir = dir;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>뽑아 둔 그림 폴더를 잡는다. 없으면 null.</summary>
    public static CombatArt? Open()
    {
        LastError = "";
        string dir = Path.Combine(AppContext.BaseDirectory, ArtDirectory);
        if (!Directory.Exists(dir)) dir = ArtDirectory;         // 개발 중에는 소스 옆이다
        if (!File.Exists(Path.Combine(dir, "sea-00.png")))
        {
            LastError = $"{dir} 에 해전 그림이 없습니다 (tools/extract_scombat.py 로 뜹니다)";
            return null;
        }
        return new CombatArt(dir);
    }

    /// <summary>그 이름의 그림 파일. 없으면 null.</summary>
    public string? Path_(string name)
    {
        string path = Path.Combine(_dir, name + ".png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>배 한 장 — 벌 <paramref name="fleet"/> 의 <paramref name="way"/> 번째 방향.</summary>
    public string? Ship(int fleet, int way) =>
        Path_($"ship{Math.Clamp(fleet, 0, Fleets - 1)}-{((way % Ways) + Ways) % Ways:D2}");

    /// <summary>바다 바탕.</summary>
    public string? Sea() => Path_("sea-00");

    /// <summary>칸(마름모). <paramref name="lit"/> 이면 짚은 칸이다.</summary>
    public string? CellArt(bool lit) => Path_(lit ? "cell-01" : "cell-00");
}
