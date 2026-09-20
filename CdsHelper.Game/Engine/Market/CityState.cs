using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 도시 상태 — 통상 · 전염병 · 전쟁 · 대조선 따위. 도시 레코드(<c>0x005863A8</c> + 도시 x 92)의 <c>+0x40</c> 이다.
/// </summary>
/// <remarks>
/// <b>기한이 없다.</b> 바꾸는 곳은 역사 대본 명령 <c>21 08 [도시] 18 [상태]</c>(<c>0x00409BAC</c>)와
/// 전염병 걸린 함대가 입항할 때(<c>0x00477124</c>)뿐이고, 무작위로 바뀌는 일은 없다.
/// 효과는 교역소 값(<see cref="Adjust"/>) · 교역소 「회화」의 돈벌이 이야기 · 도시정보의 「상태」 세 곳이다.
/// </remarks>
public static class CityState
{
    public const int Normal = 0, Plague = 1, Famine = 2, GreatFamine = 3, Harvest = 4, GreatHarvest = 5,
                     ColdWave = 6, HeatWave = 7, LaborShortage = 8, War = 9, Festival = 10,
                     Boom = 11, Slump = 12, Shipbuilding = 13;

    /// <summary>상태 이름 열넷(<c>0x0053CE60</c>, 게터 <c>0x00429D70</c>).</summary>
    public static readonly string[] Names =
    [
        "통상", "전염병", "기근", "대기근", "풍작", "대풍작", "대한파",
        "혹서", "노동력부족", "전쟁", "축제", "호경기", "불경기", "대조선",
    ];

    /// <summary>상태 이름. 모르는 번호면 통상.</summary>
    public static string NameOf(int state) =>
        state >= 0 && state < Names.Length ? Names[state] : Names[Normal];

    /// <summary>전염병 걸린 함대가 통상인 마을에 들었을 때(<c>0x00544D88</c>).</summary>
    public const string SpreadWord = "병이 마을에 전염되었습니다.";

    /// <summary>
    /// 상태가 교역품 값을 비튼다(<c>0x00480290</c>) — 매각가 셈(<c>0x00480890</c>)의 <b>맨 끝</b>에 건다.
    /// </summary>
    /// <remarks>
    /// 분류(교역품 표 <c>+0x08</c>)로 보는 것과 <b>이름</b>으로 보는 것(약재·노예·말·견직물·목재)이 섞여 있다.
    /// <code>
    ///   1 전염병      약재 x2
    ///   2 기근 3 대기근  분류 0(식료품) x3/2
    ///   4 풍작        분류 0 x70/100 · 분류 6(기호품) x13/10
    ///   5 대풍작      분류 0 x50/100 · 분류 6 x15/10
    ///   6 대한파      분류 8(직물) +30
    ///   7 혹서        분류 5(향신료) x13/10
    ///   8 노동력부족  노예 x3/2
    ///   9 전쟁        분류 4(무기) · 분류 9(광석) · 말 x3/2
    ///  10 축제        분류 1(주류) x3/2
    ///  11 호경기      분류 11 · 분류 10 · 견직물 x13/10
    ///  12 불경기      같은 것 x7/10
    ///  13 대조선      목재 x3/2
    /// </code>
    /// </remarks>
    public static int Adjust(int state, GoodsTable.Goods goods, int price)
    {
        int cat = goods.Category;
        string name = goods.Name;
        return state switch
        {
            Plague when name == "약재" => price * 2,
            Famine or GreatFamine when cat == 0 => price * 3 / 2,
            Harvest when cat == 0 => price * 70 / 100,
            Harvest when cat == 6 => price * 13 / 10,
            GreatHarvest when cat == 0 => price * 50 / 100,
            GreatHarvest when cat == 6 => price * 15 / 10,
            ColdWave when cat == 8 => price + 30,
            HeatWave when cat == 5 => price * 13 / 10,
            LaborShortage when name == "노예" => price * 3 / 2,
            War when cat is 4 or 9 || name == "말" => price * 3 / 2,
            Festival when cat == 1 => price * 3 / 2,
            Boom when cat is 11 or 10 || name == "견직물" => price * 13 / 10,
            Slump when cat is 11 or 10 || name == "견직물" => price * 7 / 10,
            Shipbuilding when name == "목재" => price * 3 / 2,
            _ => price,
        };
    }
}
