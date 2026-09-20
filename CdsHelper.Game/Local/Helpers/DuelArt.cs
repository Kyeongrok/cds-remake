using System.IO;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 일기토 화면 그림 — <c>FIGHTER.CDS</c> 에서 미리 뽑아 둔 <c>asset/duel</c> 을 읽는다.
/// </summary>
/// <remarks>
/// 게임의 일기토 판은 <c>0x180 x 0x100</c>(384x256)이고 두 층이다
/// (<c>0x004AA7BB</c>). 위가 배경, 아래가 눈금판이다.
/// <code>
///   duel-field · wood · sand · tavern · mosque · temple · deck   384x136  배경 일곱
///   duel-panel-<배경>                                             384x112  눈금판 일곱
/// </code>
/// 사람 그림만은 <see cref="FighterSprites"/> 가 CDS 에서 그때그때 푼다 — 스프라이트셋이 아홉에
/// 장이 서른셋이라 미리 뽑아 두면 파일이 삼백 장 가까이 된다.
///
/// <b>눈금판의 자리는 그림에 이미 찍혀 있다.</b> 파란 회색(<c>144,156,181</c>) 네모가
/// 무엇이 앉을 자리인지 알려 주는 자리표라, 그것을 재어 <see cref="Slots"/> 에 적어
/// 두었다 — 눈으로 맞춘 값이 아니다.
/// </remarks>
public sealed class DuelArt
{
    /// <summary>뽑아 둔 그림이 든 곳.</summary>
    public const string ArtDirectory = "asset/duel";

    /// <summary>배경 크기.</summary>
    public const int ArenaWidth = 384, ArenaHeight = 136;

    /// <summary>눈금판 크기.</summary>
    public const int PanelWidth = 384, PanelHeight = 112;

    /// <summary>판 전체 — 배경과 눈금판을 얹은 크기.</summary>
    public const int BoardWidth = ArenaWidth, BoardHeight = ArenaHeight + PanelHeight;

    /// <summary>
    /// 눈금판 위의 자리들. 왼쪽이 상대, 오른쪽이 나다.
    /// </summary>
    /// <remarks>
    /// <c>asset/duel/duel-panel-*.png</c> 의 자리표 네모를 그대로 재었다.
    /// <code>
    ///   초상   왼 (  7,  8) 84x96      오른 (293,  8) 84x96
    ///   고른 손 왼 (112, 24) 64x16     오른 (208, 24) 64x16
    ///   막대   왼 (112, 52) 72x8       오른 (200, 52) 72x8    상(H)
    ///          왼 (112, 68)            오른 (200, 68)         중(M)
    ///          왼 (112, 84)            오른 (200, 84)         하(L)
    /// </code>
    /// </remarks>
    public static class Slots
    {
        public const int PortraitW = 84, PortraitH = 96;
        public const int FoePortraitX = 7, MyPortraitX = 293, PortraitY = 8;

        public const int MoveW = 64, MoveH = 16, MoveY = 24;
        public const int FoeMoveX = 112, MyMoveX = 208;

        public const int BarW = 72, BarH = 8;
        public const int FoeBarX = 112, MyBarX = 200;

        /// <summary>부위 셋의 세로 자리 — 상 · 중 · 하.</summary>
        public static readonly int[] BarY = [52, 68, 84];
    }

    /// <summary>배경 이름 — <see cref="FighterSprites.SetForCulture"/> 와 짝이 아니다.</summary>
    /// <remarks>
    /// 배경은 <b>무대 번호</b>로 갈리고 그림 파트는 <c>0x12 + 무대 * 2</c> 다(<c>0x004A9200</c>).
    /// <code>
    ///   0 갑판 · 1 초원 · 2 숲 · 3 모래 · 4 술집 · 5 모스크 · 6 사원
    /// </code>
    /// 밑값은 1(초원)이고, 시설이 술집·여관이면 도시 문화권으로 4·5·6 이 된다
    /// (<c>0x004A2D88</c>~<c>0x004A2DCE</c>, 표 <c>0x004A2E6C</c>/<c>0x004A2E58</c>).
    /// 해전 일기토는 무대 0 이다(<c>0x0043A4A8</c>).
    /// </remarks>
    public const string Field = "duel-field", Deck = "duel-deck",
                        Tavern = "duel-tavern", Mosque = "duel-mosque", Temple = "duel-temple";

    /// <summary>
    /// 술집·여관 일기토의 배경 — 도시 문화권으로 갈린다(표 <c>0x004A2E6C</c>).
    /// 0·1·2 술집 · 3·4·5·10 모스크 · 6·7·8·9 사원.
    /// </summary>
    public static string TavernFor(int culture) => culture switch
    {
        0 or 1 or 2 => Tavern,
        6 or 7 or 8 or 9 => Temple,
        _ => Mosque,
    };

    /// <summary>
    /// 눈금판 — <b>배경을 안 탄다</b>. 어느 배경이든 같은 한 장이다.
    /// </summary>
    /// <remarks>
    /// 예전에는 「눈금판은 제 팔레트가 없어 배경 것을 같이 쓴다」고 보고 배경마다 한 장씩
    /// 뽑았다. <b>틀렸다</b> — 눈금판 색인이 11~73 이라 죄다 74 밑이고, 게임은 74 부터
    /// 그림마다의 팔레트를 얹으므로(<see cref="GamePalette.OwnPaletteBase"/>) 그 아래는
    /// <b>공용 색표</b>를 본다.
    ///
    /// 배경 팔레트를 씌운 탓에 나무빛이 분홍으로 뭉개지고 막대 자리와 H·M·L 글자가
    /// 바탕에 묻혀 있었다. 공용 색표로 뽑으니 원본과 같아진다.
    /// </remarks>
    public static string PanelFor(string arena) => "duel-panel";

    private readonly string _dir;

    private DuelArt(string dir) => _dir = dir;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>뽑아 둔 그림 폴더를 잡는다. 없으면 null.</summary>
    public static DuelArt? Open()
    {
        LastError = "";
        string dir = Path.Combine(AppContext.BaseDirectory, ArtDirectory);
        if (!Directory.Exists(dir)) dir = ArtDirectory;         // 개발 중에는 소스 옆이다
        if (!File.Exists(Path.Combine(dir, PanelFor(Field) + ".png")))
        {
            LastError = $"{dir} 에 일기토 그림이 없습니다 (tools/extract_duel_art.py 로 뜹니다)";
            return null;
        }
        return new DuelArt(dir);
    }

    /// <summary>그 이름의 그림 파일. 없으면 null.</summary>
    public string? Path_(string name)
    {
        string path = Path.Combine(_dir, name + ".png");
        return File.Exists(path) ? path : null;
    }
}
