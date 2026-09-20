namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 미니게임 번호. 원본 MINI GAME 차림표의 <c>MG00</c>~<c>MG06</c> 차례(<c>0x0045FCCC</c>)와 같다.
/// </summary>
/// <remarks>
/// 발견 대본이 부르는 명령은 둘이다.
/// <code>
///   0E 04 [u16 n]                    0x00408D16 → 뜀표 0x0040C1B0 — 0·1·2·3·6. 4·5 는 건너뛴다
///   0E 14|1A [u32 판자] 04 [u16 n]   0x00408DF7 — 4 코인 게임(0x004531F0(1))
///                                                  5 발라몬의 탑(0x00431740(판자, 1))
/// </code>
/// 둘 다 이기면 결과를 1 로 두고(<c>43 47</c> 이 뛴다) 레지스트리 <c>MG%02d</c> 를 켜 차림표에
/// 그 놀이를 푼다(<c>0x00406B60</c>).
/// </remarks>
public enum DisevMinigame
{
    /// <summary>성배 퍼즐(<c>0x004684D0</c>).</summary>
    Grail = 0,

    /// <summary>스핑크스 퀴즈(<c>0x0047BFE0</c>).</summary>
    Sphinx = 1,

    /// <summary>미궁 64 퍼즐(<c>0x0042C8A0</c>).</summary>
    Maze = 2,

    /// <summary>낚시 게임(<c>0x0047BDD0</c>).</summary>
    Fishing = 3,

    /// <summary>코인 게임(<c>0x004531F0</c>). 대본은 <c>0E 14/1A … 04 04 00</c> 으로 부른다.</summary>
    Coin = 4,

    /// <summary>발라몬의 탑 퍼즐(<c>0x00431740</c>). 대본은 <c>0E 14/1A [판자] 04 05 00</c> 으로 부른다.</summary>
    Tower = 5,

    /// <summary>화살표 입방체 퍼즐(<c>0x0049B3C0</c>).</summary>
    Cube = 6,
}

public static class DisevMinigameExtensions
{
    /// <summary>화면에 적는 이름.</summary>
    public static string Title(this DisevMinigame game) => game switch
    {
        DisevMinigame.Grail => "성배 퍼즐",
        DisevMinigame.Sphinx => "스핑크스 퀴즈",
        DisevMinigame.Maze => "미궁 64 퍼즐",
        DisevMinigame.Fishing => "낚시 게임",
        DisevMinigame.Coin => "코인 게임",
        DisevMinigame.Tower => "발라몬의 탑 퍼즐",
        DisevMinigame.Cube => "화살표 입방체 퍼즐",
        _ => "(없음 — 게임이 건너뜀)",
    };

    /// <summary>
    /// <c>0E 04</c> 로 부를 수 있는가. 코인 게임·발라몬의 탑은 <c>0E 04</c> 뜀표에서 건너뛰어
    /// (<c>0x0040C1C0</c>·<c>0x0040C1C4</c> → <c>0x0040BCD5</c>) <c>0E 14/1A</c> 으로만 뜬다.
    /// </summary>
    public static bool ByMinigameCommand(this DisevMinigame game) =>
        game is DisevMinigame.Grail or DisevMinigame.Sphinx or DisevMinigame.Maze
             or DisevMinigame.Fishing or DisevMinigame.Cube;

    /// <summary><c>0E 14/1A</c> 으로 부를 수 있는가 — 코인 게임·발라몬의 탑뿐이다(<c>0x00408E2B</c>).</summary>
    public static bool ByPuzzleCommand(this DisevMinigame game) =>
        game is DisevMinigame.Coin or DisevMinigame.Tower;
}
