using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 모항의 시설에서 <b>아내·아이와 마주치는</b> 장면(<c>0x004A1EB0</c>).
/// </summary>
/// <remarks>
/// 건물에 들어서면 게임은 인사(시설마다의 <c>vtbl+0x08</c>)를 내고, 후원자가 앉아 있지
/// 않으면 이 자리를 본다.
/// <code>
///   004a1eba  도시 +0x1D 비트 8 — <b>모항</b>이라야 한다
///   004a1ec2  rand(5) == 0
///   004a1edf  0x004AB800(0, 5, 9) 아들 · 0x004AB800(1, 5, 9) 딸   ; 둘 다 있으면 rand(2)
///   004a1f1b  아내(0x005B61B0). 없으면 0 이고, 그래도 대개 아이는 말한다
///   004a1f3d  시설마다의 vtbl+0x10(아내, 아이)
/// </code>
/// <b>다섯~아홉 살만 나온다.</b> 자택 인사(<see cref="Home.WelcomerOf"/>)가 다섯~열넷인
/// 것과 달라서, 열 살이 되면 시설에서는 이 대사를 영영 못 본다 — 원본 그대로 둔다.
///
/// 말하는 시설은 <b>교역소 · 여관 · 조선소 · 왕궁 · 시장</b> 다섯뿐이다. 항구·술집·도서관·
/// 조합·교회·성문·자택은 <c>vtbl+0x10</c> 이 기본 구현이라 아무 일도 안 난다.
/// </remarks>
public static class FamilyVisit
{
    /// <summary>마주칠 굴림 — 다섯에 하나다(<c>0x004A1EC2</c>).</summary>
    public const int Odds = 5;

    /// <summary>시설에서 말하는 아이의 나이 — 다섯~아홉(<c>0x004A1EDF</c>).</summary>
    public const int FromAge = 5, ToAge = 9;

    /// <summary>
    /// 이 도시에서 가족과 마주치는가 — <b>모항</b>이고 굴림에 걸려야 한다.
    /// </summary>
    public static bool Due(Player player, int cityId, Random random) =>
        player.HomePort >= 0 && player.HomePort == cityId && random.Next(Odds) == 0;

    /// <summary>
    /// 마주치는 아이. 아들과 딸을 따로 뽑아 둘 다 있으면 굴려 고른다(<c>0x004A1EFF</c>).
    /// </summary>
    public static Player.Child? MetChild(Player player, Random random)
    {
        var able = player.Children
            .Where(c => c.IsBornBy(player.Date)
                        && c.AgeOn(player.Date) >= FromAge && c.AgeOn(player.Date) <= ToAge)
            .ToList();
        if (able.Count == 0) return null;

        var son = able.FirstOrDefault(c => !c.Daughter);
        var girl = able.FirstOrDefault(c => c.Daughter);
        if (son is null) return girl;
        if (girl is null) return son;
        return random.Next(2) == 0 ? girl : son;
    }

    /// <summary>아들·딸 셋씩 든 말 묶음에서 하나 고른다.</summary>
    private static string Pick(string[] boy, string[] girl, bool daughter, Random random) =>
        daughter ? girl[random.Next(girl.Length)] : boy[random.Next(boy.Length)];

    /// <summary>조선소에서 아이가 하는 말(<c>0x0044B500</c>).</summary>
    public static string ShipyardWord(bool daughter, Random random) =>
        Pick(ShipyardBoy, ShipyardGirl, daughter, random);

    private static readonly string[] ShipyardBoy =           // 0x00530F60~
    [
        "이렇게 큰 배 타고 있어? 멋있어~!",
        "나도 아버지처럼 [모험가]가 될 거야. 헤헤.",
        "큰 배로군~ 굉장하군...",
    ];

    private static readonly string[] ShipyardGirl =          // 0x00530FD0~
    [
        "있잖아, 내가 크면 배 태워줘. 약속이야!",
        "아버지도 배타고 바다 건너편에 가지요. 나도 가고 싶어~",
        "아버지 배는 여기에 있어요? 나도 태워주면 안돼요?",
    ];

    /// <summary>교역소에서 아이가 하는 말(<c>0x00480B90</c>).</summary>
    public static string TradePostWord(bool daughter, Random random) =>
        Pick(TradePostBoy, TradePostGirl, daughter, random);

    private static readonly string[] TradePostBoy =          // 0x00532678~
    [
        "여러가지 물건이 있네. 이거 뭐하는 거예요?",
        "아버지는 여기서 물건 산 일 있어요?",
        "바다 건너온 물건이란 걸 보고 싶네~.",
    ];

    private static readonly string[] TradePostGirl =         // 0x005326F8~
    [
        "여기에서 팔고 있는 물건이란 전부 이상한 물건 뿐이야.",
        "와아, 여기 큰 시장 같아.",
        "바다 건너온 물건은 어느것이야?",
    ];

    /// <summary>여관에서 아이가 하는 말(<c>0x0047FA40</c>).</summary>
    public static string InnWord(bool daughter, Random random) =>
        Pick(InnBoy, InnGirl, daughter, random);

    private static readonly string[] InnBoy =                // 0x00544288~
    [
        "앗, 아버지! 지금 여관 아주머니가 과자 주셨어요.",
        "여관 창문을 깨버렸다! 들키기 전에 도망가야지.",
        "가끔은 여관에서 머물고 싶군~",
    ];

    private static readonly string[] InnGirl =               // 0x00544308~
    [
        "여관 아주머니가, 자기 마을의 여관에 머물면 좋은 일이 있을지도 모른대요.",
        "아버지는 이 여관에 머문 적 있어요?",
        "여관 아주머니가, 아버지가 자주 여기서 머물렀다고 그러던데요.",
    ];

    /// <summary>왕궁에서 아이가 하는 말(<c>0x00470B10</c>).</summary>
    public static string PalaceWord(bool daughter, Random random) =>
        Pick(PalaceBoy, PalaceGirl, daughter, random);

    private static readonly string[] PalaceBoy =             // 0x00544C08~
    [
        "큰 집이로군! 나도 이런 집에서 살고 싶군.",
        "여기에는 영주님이 계시지, 좋겠군.",
        "어린애는 들어오면 안된대. 그렇지만 들어왔는데 뭐.",
    ];

    private static readonly string[] PalaceGirl =            // 0x00544C98~
    [
        "난 알고 있어. 공주가 되면 성에서 살 수 있지요.",
        "영주님께 귀엽다는 소릴 들었어.",
        "나, 장래 공주님이 되고 싶어.",
    ];

    /// <summary>
    /// 시장에서는 <b>아내가 먼저 말하고 아이가 받는다</b>(<c>0x004B36E0</c>).
    /// </summary>
    /// <remarks>
    /// 세 벌 가운데 하나를 굴려 고르고, 받는 말만 아들·딸로 갈린다. 다섯 시설 가운데
    /// <b>여기만 아내가 있어야</b> 난다 — 없으면 아이가 있어도 한 줄도 안 나온다
    /// (<c>0x004B36F8</c> 의 <c>je</c>).
    /// </remarks>
    public static (string Wife, string Child) MarketWords(bool daughter, Random random)
    {
        int i = random.Next(MarketWife.Length);
        return (MarketWife[i], daughter ? MarketGirl[i] : MarketBoy[i]);
    }

    private static readonly string[] MarketWife =            // 0x00544598 · 0x00544618 · 0x00544680
    [
        "오늘은 좋아하는 시튜예요.",
        "아, 당신. 오늘은 무엇이 좋겠어요?",
        "아, 여기에 온다는 걸 알았으면 사올 물건을 부탁하는 건데. 짓궂어.",
    ];

    private static readonly string[] MarketBoy =             // 0x005445B8 · 0x00544640 · 0x005446C8
    [
        "와~아, 시튜다, 시튜다! 와~아, 와~아.",
        "난 햄이 좋아! 햄햄~.",
        "짓궂어~.",
    ];

    private static readonly string[] MarketGirl =            // 0x005445E0 · 0x00544658 · 0x005446D8
    [
        "아, 좋아라! 아버지 들었어요? 오늘은 빨리 돌아오세요.",
        "난 아무거나 잘 먹어요. 대단하죠.",
        "「짓궂어.」 어때, 닮았어?",
    ];
}
