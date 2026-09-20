using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// <b>세계일주 장면</b>(<c>0x00492040</c>) — 지구를 한 바퀴 돌고 계약을 맺은 도시로 돌아오면
/// 그 자리에서 날짜가 어긋난 것을 깨닫는다.
/// </summary>
/// <remarks>
/// 도시에 들어서는 자리에서 돈다(<c>0x00492430</c> 이 <c>0x0048E5E0</c> 안쪽에서 부른다) —
/// 항구 명령 창보다 먼저다. 관문은 다섯이고 하나라도 어긋나면 아무 일도 안 일어난다.
/// <code>
///   0x0047D460  계약이 있고, 바퀴 수(0x005B63D0)가 0 이 아니고, 지금 도시가 계약을 맺은 도시다
///   0x00492073  계약의 유적 번호가 발견물 7번(세계일주항로)의 것과 같다(0x00493E60)
///   0x00492099  발견물 7번의 칸 2 가 비어 있다 — 아직 발표하지 않았다(0x004AABA0)
///   0x004920D0  부관이 타고 있다(0x0047CC60(0,1) 의 +4 가 1)
/// </code>
/// <b>한 번만 도는 깃발은 없다.</b> 보고하면 계약이 끝나(<c>0x0044EE30</c>) 첫 관문이 막히므로
/// 그것으로 갈음한다 — 보고 전에 그 도시를 다시 드나들면 장면도 다시 돈다(원본 그대로다).
///
/// 이 장면이 세이브에 남기는 것은 둘뿐이다 — 아이템 179 「세계 일주 지도」와 발견물 7번
/// <b>발견</b>이다(<c>0x004AAC10</c>). 날짜를 고치지도, 바퀴 수를 되돌리지도, 명성을 올리지도
/// 않는다. 명성과 「역사 최초로 세계일주를 달성했다!」는 뒤에 <b>보고</b>할 때 나온다.
/// </remarks>
internal static class WorldRouteScene
{
    /// <summary>세계일주항로의 발견물 번호(<see cref="Palace.WorldRoute"/>).</summary>
    public const int Discovery = Palace.WorldRoute;

    /// <summary>손에 넣는 아이템 — 「세계 일주 지도」(<c>0x00492192</c> 의 <c>0xB3</c>).</summary>
    public const int MapItem = 179;

    /// <summary>부관이 이치를 알아듣는 눈금(<c>0x0049239F</c> 의 <c>cmp 0x46</c>, 값 <c>+1</c> 을 견준다).</summary>
    public const int MateWits = 70;

    /// <summary>
    /// 관문을 다 넘었는지 본다(<c>0x0047D460</c> · <c>0x00492040</c> 머리).
    /// </summary>
    public static bool Due(Engine.Game game, int cityId, bool hasMate)
    {
        var player = game.Player;
        if (player.Contract is not { } contract) return false;
        if (player.Laps == 0) return false;
        if (contract.City != game.CityName(cityId)) return false;
        if (player.HasAnnounced(Discovery)) return false;
        if (!hasMate) return false;

        // 맡은 이야기가 세계일주항로를 가리키는가 — 힌트의 유적 번호로 견준다(0x00493E60).
        if (game.Hints?.Find(contract.Hint) is not { } hint) return false;
        return game.Discoveries?.Table?.Find(Discovery)?.Hint == hint.Discovery;
    }

    /// <summary>
    /// 장면을 돌린다(<c>0x00492040</c>) — 대사는 후원자와 부관이 주고받는다.
    /// </summary>
    /// <remarks>
    /// 반말 쪽은 계약을 맺은 후원자의 인물 기록이 말하는데(<c>계약+0x0C</c>), 우리말은
    /// 제독이 하는 말처럼 읽힌다(「제, 제독···이상한데요」에 대꾸한다). 원본 코드대로
    /// <b>후원자 얼굴</b>을 쓴다.
    /// </remarks>
    public static void Play(Window owner, Engine.Game game, uint[]? sponsorFace, uint[]? mateFace)
    {
        var player = game.Player;
        int laps = player.Laps, turns = Math.Abs(laps);

        void Lord(string words) => TalkDialog.Say(owner, sponsorFace, "", words);
        void Mate(string words) => TalkDialog.Say(owner, mateFace, "", words);

        Lord("···돌아왔구나.");

        if (turns > 2)
        {
            Mate("예. 이렇게 해도가 지구를 한 바퀴 돌아···");
            Lord("응? 이곳과 이곳은 같은 모양을 하고 있군···이곳도 같잖아?");
            Mate("흠-. 아무래도 지구를 몇 번씩이나 돌았던 것 같군요.");
        }
        else if (turns == 2)
        {
            Mate("예. 보시는 바와 같이 해도가 지구를 빙 돌아···");
            Lord("응? 이곳과 이곳은 같은 모양을 하고 있군···");
            Mate("흠-. 아마도 힘이 남아돌아 지구를 두 번씩이나 돈 것 같군요.");
        }
        else
        {
            Mate("예. 보시는대로 해도가 지구를 빙 돌아···완전히 한 바퀴 돌았지요.");
        }

        // 지도를 준다 — 소지품이 꽉 찼으면 못 든다고만 이르고 넘어간다(0x00492192).
        string map = game.Items?.Find(MapItem)?.Name ?? "세계 일주 지도";
        NoticeDialog.Show(owner, player.Take(MapItem)
            ? $"[{map}]{GameUi.Josa(map, "을", "를")} 손에 넣었다"
            : $"소유 아이템이 너무 많아서 [{map}]{GameUi.Josa(map, "을", "를")} 손에 넣는 것을 단념했습니다");

        // 여기서 발견물 7번이 <b>발견</b>으로 적힌다(0x004AAC10) — 발표는 아직이다.
        player.Discover(Discovery);

        Mate("제, 제독···이상한데요.");
        Lord("응? 무슨 일인가?");
        Mate($"오늘은 {player.Date.Month}월 {player.Date.Day}일입니다.");
        Lord("그게 어떻다는건가?");
        Mate(turns == 1 ? "이 기록에 의하면 하루씩 어긋나 있습니다."
                        : $"이 기록에 의하면 {turns}일 어긋나 있습니다.");
        Lord("설마 그런 일이! 기록이 잘못 되어 있겠지?");
        Mate("하루도 빠뜨리지 않고 일지를 적고 있었으니 틀림없습니다.");
        Lord("설마 그런일이···?");
        Mate("글쎄? 지구를 한 바퀴 돌았기 때문일까요?");
        Lord("·····!! 지금 뭐라고 했나?");
        Mate("예? 그러니까 지구를 한 바퀴···");
        Lord("이런! 그게 이유란 말이냐!");

        if (laps < 0)
        {
            Lord("우리들은 태양의 움직임을 따라 지구를 돌았기 때문에 하루 하루가 조금씩 길어진 것이다.");
            Mate("??");
            Lord(turns == 1 ? "과연 그래서 하루가 모자랐구나."
                            : $"과연 그래서 {turns}일이 모자랐구나.");
        }
        else
        {
            Lord("우리들은 태양의 움직임에 거슬러서 지구를 한 바퀴 돌았기 때문에 하루 하루가 조금씩 짧아진 것이다.");
            Mate("??");
            // 원본 문구에 빈칸이 둘 있다(0x0053B948) — 그대로 둔다.
            Lord(turns == 1 ? "과연 그래서 하루의 시간 차이가 생겼구나."
                            : $"과연 그래서  {turns}일의 시간 차이가 생겼구나.");
        }

        Mate(MateMind(game) + 1 > MateWits
            ? "흠-. 이론으로는 알겠습니다만, 실감이 나지 않는데요."
            : "저는 무슨 말인지 전혀 모르겠는데요···??");

        Lord("참으로 희한한 경험을 했군. 자, 빨리 보고하러 가자.");
    }

    /// <summary>부관의 지력(<c>부관+0x24</c>) — 부관이 없으면 제독 것을 본다.</summary>
    private static int MateMind(Engine.Game game)
    {
        string mate = game.Player.MateAt(0);
        if (mate.Length > 0 && game.MateInfo(mate) is { } who) return who.Mind;
        return game.Player.AbilityOf(Ability.Mind);
    }
}
