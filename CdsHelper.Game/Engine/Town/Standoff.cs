using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 적대 도시 앞에서 벌어지는 일 — 공격 · 잠입 · 교섭 · 떠난다.
/// </summary>
/// <remarks>
/// 게임은 입항을 시도할 때(<c>0x00468790</c>) 그 도시가 딸린 나라의 <b>적대도</b>를 먼저
/// 본다. 나라마다 열여섯 바이트짜리 형편 레코드가 <c>0x005859C0</c> 에 있고 적대도는
/// <c>+0x0C</c> 다(<c>0x00429D90</c>).
/// <code>
///   468796  적대도 = 형편(그 도시의 나라)[+0x0C]
///   468804  적대도 &lt;= 0  →  여느 입항 흐름
///           적대도 &gt;  0  →  적대 차림표 0x004A56F0(항구면 1, 마을이면 0)
/// </code>
/// 차림표는 열두 바이트짜리 칸 넷을 쌓아 <c>0x00469A70(칸, 4, 1, …)</c> 로 낸다.
/// <b>꺼진 칸도 자리를 안 비우므로 고른 값은 언제나 붙박이 번호</b>이고, 고른 뒤
/// <c>0x004A5840</c> 뜀표로 갈린다.
///
/// 볼트 <c>65.분석-육상전</c> 의 「문 — 도시 상태와 적대 차림표」 를 옮긴 것이다.
/// </remarks>
public static class Standoff
{
    /// <summary>
    /// 그 나라의 <b>출입여부</b> — 0 자유 · 1 배로는 못 들어감 · 2 아주 막힘.
    /// </summary>
    /// <remarks>
    /// 게임은 판을 열 때 나라 표 <c>+0x14</c> 를 형편 칸으로 뜨고(<c>0x0041B320</c>), 그 뒤로는
    /// 형편 칸만 본다. 여기서는 <b>주인공이 아직 그 나라를 건드린 적이 없으면 표 값을 그대로
    /// 쓴다</b> — 결과가 같으면서, 표를 고치면 놀이에 곧장 먹는다.
    /// </remarks>
    public static int EntryOf(Player player, NationTable? nations, int nation)
    {
        if (nation < 0) return 0;
        if (player.Hostility.TryGetValue(nation, out int moved)) return moved;
        return nations?.Find(nation)?.Entry ?? 0;
    }

    /// <summary>
    /// 그 문이 막혔는가 — <b>마을과 항구의 잣대가 다르다</b>.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004687fd  출입여부 &gt; 0   → 적대 차림표 0x004A56F0(1)   ; 1 = 마을
    ///   004770bd  출입여부 == 2  → 적대 차림표 0x004A56F0(0)   ; 0 = 항구
    /// </code>
    /// <b>인자가 1 이면 마을이다.</b> 차림표 앞머리 <c>0x004A5210</c> 이 그 인자로 문구를
    /// 갈라 내는 데서 드러난다 — 인자가 0 이 아니면 <c>0x00551D48</c>
    /// "…어쩐지 <b>마을</b>에는 들여보내어 주지 않을 것 같습니다", 0 이면
    /// <c>0x00551D90</c> "…들여보내어 주지 않을 것 같습니다"(마을이라는 말이 없다)다.
    ///
    /// 그래서 <b>1 은 마을만 막고 항구는 연다. 2 라야 항구까지 막힌다.</b> 출입여부가 1 인
    /// 나라 열다섯이 죄다 이슬람권과 명인 것이 이 읽기와 맞는다 — 배로 항구에 대고 교역은
    /// 하되 내륙 마을에는 못 들어간다. 2 인 그라나다와 오스만·투르크만 항구까지 막힌다.
    /// </remarks>
    public static bool Barred(int entry, bool byLand) => byLand ? entry > 0 : entry >= 2;

    // ── 성문 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 성문에서 막는 이의 <b>화자 갈래</b> — 마을이면 문지기(10), 항구면 항구 사람(0)이다.
    /// </summary>
    /// <remarks>
    /// <c>0x004A2500</c> 이 그 자리에서 임시 인물 하나를 세워 <c>[적대도시+0x84]</c> 에 넣는데,
    /// 갈래를 <c>call [vtable+0x48]</c> 로 받아 <c>0x00477C20</c> 에 넘긴다. 그쪽이 화자표에서
    /// 얼굴을 떠 임시 인물 칸(<c>0x005B3A60</c> + 갈래*32)에 박는다.
    /// <code>
    ///   00477c68  edx = 갈래*13
    ///   00477c70  ecx = [(문화권 + 갈래*13)*4 + 0x0056823C]     ; 얼굴 번호
    ///   00477c7a  [0x005B3A68 + 갈래*32] = ecx
    /// </code>
    /// 그 <c>+0x48</c> 은 참·거짓이 아니라 <b>갈래 그 자체</b>다 — 마을에서 10 이 나온다.
    /// 화자표의 이름 칸이 한 줄 밀려 있어 갈래 10 의 이름이 <b>"문지기"</b> 이고, 문화권 4
    /// (이슬람)에서 얼굴 331 이 나오는 것이 게임 화면과 같다. 밀린 것은 다른 줄로도 맞는다 —
    /// 갈래 3 이 얼굴 292(십자가 건 사제)이고, 갈래 5 는 성별 칸이 1(여자)인 「여관 주인」이며,
    /// 6 · 7 · 8 이 조선소 · 시장 · 도서관으로 우리가 이미 쓰던 코드와 그대로 맞물린다.
    ///
    /// 얼굴을 뒤에서 그리는 것은 <c>0x00478280</c> 이다 — 화자가 일행이면 기본 얼굴
    /// 299(<c>0x12B</c>)를 쓰고, 아니면 그 인물의 <c>vtable +0x08</c> 을 부른다.
    /// </remarks>
    public static int GateSpeaker(bool byLand) => byLand ? 10 : 0;

    /// <summary>성문 문지기가 하는 말(<c>0x00551D28</c>).</summary>
    public const string GateWord = "외국인은 들어올 수 없다.";

    /// <summary>
    /// 그 말을 아무도 못 알아들었을 때 대원이 덧붙이는 소리. <b>마을과 항구가 다르다.</b>
    /// </summary>
    /// <remarks>
    /// <c>0x004A526E</c> 가 차림표 인자로 갈라 고른다 — 인자가 0 이 아니면(=마을)
    /// <c>0x00551D48</c>, 0 이면(=항구) <c>0x00551D90</c> 이다. 마을 쪽에만 「마을」이라는
    /// 말이 들어가는 이 갈림이 곧 인자의 뜻을 밝혀 준다.
    /// </remarks>
    public const string GateLostVillage =
        "전혀 모르겠습니다만, 어쩐지 마을에는 들여보내어 주지 않을 것 같습니다.";
    public const string GateLostPort =
        "무슨 말을 하는 건지 전혀 모르겠습니다만, 들여보내어 주지 않을 것 같습니다.";

    /// <summary>
    /// 부관이 덧붙이는 소리는 <b>세 갈래</b>다(<c>0x004A5266</c> ~ <c>0x004A52C5</c>).
    /// </summary>
    /// <remarks>
    /// 제독과 문지기의 공유 언어(<c>0x004A5236</c>)를 <c>esi</c>, 부관과 문지기의 것
    /// (<c>0x004A5261</c>)을 <c>eax</c> 라 할 때
    /// <code>
    ///   esi == 0 &amp;&amp; eax == 0   전혀 모르겠습니다만…           0x00551D48 · 0x00551D90
    ///   eax &lt;= esi              들여보내 주지 않을 것 같군요.   0x00551DE0 · 0x00551E10
    ///   eax &gt;  esi              제독, … 말을 하더군요.          0x00551E38 · 0x00551E78
    /// </code>
    /// 셋째 갈래는 <b>부관이 나보다 그 말을 잘할 때</b>다 — 그가 알아듣고 옮겨 준다.
    /// <b>부관이 없으면 아예 아무 말도 없다</b>(<c>0x004A523D</c> 가 먼저 막는다).
    /// </remarks>
    public static string GateAideWord(int mine, int aide, bool byLand) =>
        mine == 0 && aide == 0 ? byLand ? GateLostVillage : GateLostPort
        : aide <= mine ? byLand ? GateDoubtVillage : GateDoubtPort
        : byLand ? GateRelayVillage : GateRelayPort;

    /// <summary>제독이 알아들었을 때(<c>0x00551DE0</c> · <c>0x00551E10</c>).</summary>
    public const string GateDoubtVillage = "마을 안에는 들여보내 주지 않을 것 같군요.";
    public const string GateDoubtPort = "웬지 들여보내 주지 않을 것 같군요.";

    /// <summary>부관이 나보다 잘 알아들어 옮겨 줄 때(<c>0x00551E38</c> · <c>0x00551E78</c>).</summary>
    public const string GateRelayVillage = "제독, 마을 안에는 들여보내지 않을 것 같은 말을 하더군요.";
    public const string GateRelayPort = "제독, 어쩐지 외국인은 들여보내지 않을 것 같은 말을 하더군요.";

    /// <summary>고른 값. 꺼진 칸도 자리를 지키므로 붙박이 번호다.</summary>
    public const int Attack = 0, Sneak = 1, Talk = 2, Leave = 3;

    /// <summary>차림표 넉 줄(<c>0x00552198</c> 부터).</summary>
    public static readonly string[] Choices = ["공격한다", "잠입한다", "교섭한다", "떠난다"];

    /// <summary>「떠난다」를 골랐을 때의 말(<c>0x005521D0</c>).</summary>
    public const string GiveUpWord = "할 수 없군요. 포기합시다.";

    // ── 모항이 등을 돌린다 — 0x0046B6F0 ─────────────────────────────────────

    /// <summary>
    /// 악명이 이만큼을 넘으면 <b>모항</b>의 항구·성문에서 병사가 막아선다
    /// (<c>0x0046B6F0</c> 의 <c>cmp 0xBB8</c>).
    /// </summary>
    /// <remarks>
    /// 도시 <c>+0x1D</c> 비트 8(모항)까지 맞아야 한다 — <b>제 고향만 등을 돌린다</b>.
    /// 시설에 들어서는 첫머리에서 걸리고(<c>0x0046885D</c>), 걸리면 그 시설의 여느
    /// 인사는 아예 없다.
    /// </remarks>
    public const int VillainInfamy = 3000;

    /// <summary>막아서며 하는 말 둘(<c>0x005525F8</c> · <c>0x00552628</c>) — <c>rand(2)</c> 다.</summary>
    public static readonly string[] VillainWords =
    [
        "너 같은 악당을 마을에 들여보낼 수는 없다!!",
        "나타났군 원수! 우리들이 결판을 내 주겠다.",
    ];

    /// <summary>덤비는 병사의 이름 — 항구면 「항구의 병사」, 성문이면 「수위의 병사」다.</summary>
    public static string SoldierName(bool harbor) => harbor ? "항구의 병사" : "수위의 병사";

    /// <summary>
    /// 그 병사의 능력치(<c>0x0046B7DD</c> 벌) — 그 자리에서 지어낸다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   +0x20 체력 = rand(16) + 0x45      ; 69~84
    ///   +0x24 지력 = 0x27                  ; 39
    ///   +0x28 무력 = rand(16) + 0x45      ; 69~84
    ///   +0x2C 매력 = 0x27
    ///   +0x30 운   = rand(16) + 0x27      ; 39~54
    ///   +0x34 신앙심 = 0x31
    ///   +0x48 검술 = rand(2) + 2          ; 2~3
    /// </code>
    /// 일기토는 갈래 5 로 열린다(<c>0x004A2D80(0x113, 5, …)</c>). <b>지면 게임이 끝나고</b>
    /// (<c>0x0044AF40</c> 상태 4), 이기면 <b>악명이 500 오른다</b>(<c>0x0046B944</c>).
    /// </remarks>
    public static (int Body, int Might, int Sword, int Luck) SoldierOf(Random dice) =>
        (dice.Next(16) + 0x45, dice.Next(16) + 0x45, dice.Next(2) + 2, dice.Next(16) + 0x27);

    /// <summary>이기고 나면 오르는 악명(<c>0x0046B944</c> 의 <c>push 0x1F4</c>).</summary>
    public const int VillainInfamyUp = 500;

    // ── 성문 화면 두 벌 ──────────────────────────────────────────────────────

    /// <summary>
    /// 성문 화면의 말 한 벌 — <b>적대도로 막힌 문</b>과 <b>조약으로 막힌 문</b>이 딴 벌이다.
    /// </summary>
    /// <param name="TalkRoll">교섭 주사위 폭. 조약 쪽이 훨씬 잘 된다.</param>
    /// <remarks>
    /// 게임은 화면을 통째로 둘 들고 있다 — 적대도 쪽이 <c>0x004A5xxx</c>, 조약 쪽이
    /// <c>0x0046Axxx</c> 다. 차림표 둘째 줄부터 다르다(잠입한다 ↔ <b>침입한다</b>).
    /// 조약 쪽은 성문에서 「외국인은 들어올 수 없다」 대신 <b>조약 문구</b>를 내고,
    /// 부관이 덧붙이는 세 갈래도 없다(<c>0x0046ABC9</c> 가 <c>0x0046A6C0</c> 하나만 부른다).
    /// </remarks>
    public sealed record Script(
        string[] Rows, int TalkRoll, string GiveUp, string Paid,
        string TalkWonWord, string TalkWonNews, string TalkLostWord, string TalkLostNews,
        string TongueThin, string Care, string Spotted, string GotAway,
        string Caught, string Banished, string Fined, string Robbed, string GiveUpHere,
        string Villain)
    {
        /// <summary>조약 쪽 화면인지(<c>0x0046Axxx</c>) — 굴림 몇 가지가 적대도 쪽과 다르다.</summary>
        public bool IsTreaty => ReferenceEquals(this, Treaty);
    }

    /// <summary>적대도로 막힌 문(<c>0x004A5xxx</c>).</summary>
    public static readonly Script Hostile = new(
        Choices, 200, GiveUpWord, PaidWord,
        TalkWonWord, TalkWonNews, TalkLostWord, TalkLostNews,
        TongueTooThin, TakeCare, Spotted, GotAwaySafe,
        Caught, Banished, Fined, Robbed, GiveUpHere,
        "거기는 악명 높은 {0}{1}군. 죽음으로서 속죄하라!");   // 0x00552070 — {1} 은 조사 로/으로(0x004A5566 의 0x004281B0(이름, 10))

    /// <summary>조약으로 막힌 문(<c>0x0046Axxx</c>) — 「침입한다」 쪽이다.</summary>
    public static readonly Script Treaty = new(
        ["공격한다", "침입한다", "교섭한다", "떠난다"], 150,
        "어쩔 수 없군요. 포기합시다.",                       // 0x005525C0
        "금화 {0}닢을 건넸습니다",                            // 0x00552490
        "잘 되었군요. 이것으로 {0}에 들어갈 수 있습니다.",      // 0x005524A8
        "교섭에 성공했습니다. {0}에 들어갈 수 있습니다",        // 0x005524D8
        "교섭이 되지 않습니다... 제독, 어떻게 할까요?",         // 0x00552518
        "교섭에 실패했습니다. {0}에 들어갈 수 없습니다",        // 0x00552548
        "제독, 소용없습니다! 말이 통하지 않는 것이 알려지면 잡히고 맙니다.",   // 0x005522A8
        "제독, 조심하십시오.",                                // 0x005522F0
        "침입자다! 잡아라!!",                                 // 0x00552308
        "제독, 무사하셨습니까! ? 여기서부터는 안전합니다.",      // 0x00552320
        "침입자를 잡았다! 재판소에 세워라!!",                   // 0x00552358
        "마을에서 추방을 명한다. 목숨만이라도 구한걸 신에게 감사해라.",        // 0x00552380
        "벌금형 또는 추방을 명한다. 목숨을 구한걸 신에게 감사해라.",          // 0x005523C0
        "소지금을 전부 빼앗겼습니다!",                          // 0x00552400
        "제독, 무사하셨습니까! 여기는 위험하니 포기합시다.",      // 0x00552420
        "거기는 악명 높은 {0}{1} 아닌가. 죽음으로서 속죄해라.");  // 0x00552458 — {1} 은 조사 이/가(0x0046AA20 의 0x004281B0(이름, 0))

    /// <summary>공격 전에 두 번 묻는 말(<c>0x00551BF0</c> · <c>0x00551C00</c>).</summary>
    /// <summary>
    /// 쳐들어가기 전에 <b>한 번</b> 묻는 말 — <b>부관이 있으면</b> 이것이다.
    /// </summary>
    /// <remarks>
    /// 게임은 물음을 <b>두 번 하지 않는다</b> — <c>0x00468970</c> 이 글 둘을 함께 넘기면
    /// <c>0x00469680</c> 이 <c>0x00468EF0</c>(부하 첫 자리가 찼나)의 답에 따라 <b>하나만</b>
    /// 골라 띄운다. 앞엣것이 부관 있을 때, 뒤엣것이 없을 때다.
    /// <code>
    ///   00468970  push 0x551C00      ; 부관 없음 "육상 전투에 들어가겠습니다. 좋습니까?"
    ///             push 0x551BF0      ; 부관 있음 "진심이십니까!?"
    ///             push 2
    ///             call 0x469680
    /// </code>
    /// </remarks>
    public const string SureWord = "진심이십니까!?";

    /// <summary>부관이 없을 때의 물음(<c>0x00551C00</c>).</summary>
    public const string AttackWord = "육상 전투에 들어가겠습니다. 좋습니까?";

    // ── 잠입 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 나라에 숨어들 수 있는지 — <b>갈래가 3 이나 4</b> 라야 한다.
    /// </summary>
    /// <remarks>
    /// <c>0x004A56D0</c> → <c>0x004A1800</c> 이 그 도시의 나라를 찾아
    /// <c>0x004CA37C + 나라*24</c>(나라 표 <c>+0x0C</c>)를 읽고 3 이나 4 일 때만 켠다.
    /// 이슬람권이 3(사파비만 4)이고 유럽은 0, 명·조선은 7, 일본은 6 이라 죄다 꺼진다 —
    /// <b>명에 잠입이 없는 까닭이 여기 있다</b>.
    /// </remarks>
    public static bool CanSneak(int sect) => sect is 3 or 4;

    /// <summary>잠입에 보태 주는 물건(<c>0x00551F18</c>). 들고 있으면 백 점이다.</summary>
    public const string TurbanName = "터번";

    /// <summary>말이 이만큼은 돼야 겁을 안 낸다(<c>0x004A5315</c>).</summary>
    public const int SafeTongue = 3;

    /// <summary>말이 서툴 때 대원이 말리는 소리(<c>0x00551EB8</c>).</summary>
    public const string TongueTooThin =
        "제독, 소용없습니다! 말이 통하지 않는 게 알려지면, 잡히고 맙니다.";

    /// <summary>말이 통할 때(<c>0x00551F00</c>).</summary>
    public const string TakeCare = "제독, 조심하십시오.";

    /// <summary>들킨 순간(<c>0x00551F30</c>).</summary>
    public const string Spotted = "침입자다! 잡아라!!";

    /// <summary>
    /// <b>달아났을 때</b>(<c>0x00551F48</c>) — 부관이 있을 때만 나온다(<c>0x004A5425</c>).
    /// </summary>
    /// <remarks>
    /// 예전에 이 줄을 「잠입 성공」에 붙여 두었는데 게임은 그 자리에서 <b>아무 말도 안 한다</b>
    /// — 굴림에 이기면 <c>0x004A53DC</c> 가 곧장 1 을 돌려주고 도시로 들어간다.
    /// </remarks>
    public const string GotAwaySafe = "제독, 무사하셨습니까!? 여기서부터는 안전합니다.";

    /// <summary>잡혔을 때(<c>0x00551F78</c> 부터).</summary>
    public const string Caught = "침입자를 잡았다! 재판소에 세워라!!";
    public const string Banished =
        "마을에서 추방을 명한다. 목숨만이라도 구한걸 알라신에게 감사해라.";
    public const string Fined =
        "벌금형 또는 추방을 명한다. 목숨을 구한걸 알라신에게 감사해라.";
    public const string Robbed = "소지금 전부를 빼앗겼다!";

    /// <summary>
    /// <b>재판이 끝난 뒤</b>(<c>0x00552040</c>) — 부관이 있을 때만 나온다(<c>0x004A5557</c>).
    /// </summary>
    public const string GiveUpHere = "제독, 무사합니까! 여기는 위험하니 포기합시다.";

    /// <summary>잠입이 되는지 굴린다(<c>0x004A539A</c> ~ <c>0x004A53CC</c>).</summary>
    /// <remarks>
    /// <code>
    ///   4a539a  점수 = 그 도시 말 수준 x 33
    ///   4a53a4  점수 += 터번을 들었으면 100
    ///   4a53af  점수 += (운 + 1) / 2
    ///   4a53ca  점수 &gt;= rand(250) 이면 숨어들었다
    /// </code>
    /// </remarks>
    public static bool Sneaks(Player player, int tongue, bool turban, GameRandom dice) =>
        tongue * 33 + (turban ? 100 : 0) + (player.AbilityOf(Ability.Luck) + 1) / 2
        >= dice.Next(250);

    /// <summary>
    /// 조약 문 「침입한다」가 되는지(<c>0x0046A867</c>) — <c>말*20 + (운+1)/2 ≥ rand(100)</c>. 터번은 안 본다.
    /// </summary>
    public static bool Intrudes(Player player, int tongue, GameRandom dice) =>
        tongue * 20 + (player.AbilityOf(Ability.Luck) + 1) / 2 >= dice.Next(100);

    /// <summary>들킨 뒤에 달아나는지(<c>0x004A5401</c>). 못 달아나면 재판이다.</summary>
    /// <remarks>
    /// <code>
    ///   4a5401  eax = rand(90)
    ///   4a540b  ecx = [0x005B60C0] + 1        ; 능력 여섯의 첫 칸
    ///   4a5412  eax &lt;= ecx 라야 빠져나온다
    /// </code>
    /// <b>무력이 아니라 체력이다.</b> <c>0x005B60C0</c> 이 능력 배열의 머리이고
    /// (<see cref="Ability.Body"/>), 같은 배열의 <c>+0xC</c> 를 교섭이 매력으로,
    /// <c>+0x10</c> 을 재판이 운으로 쓰는 것과 앞뒤가 맞는다. 예전에 무력으로 적어
    /// 두었던 것을 바로잡았다.
    /// </remarks>
    public static bool Escapes(Player player, GameRandom dice) =>
        dice.Next(90) <= player.AbilityOf(Ability.Body) + 1;

    // ── 교섭 ──────────────────────────────────────────────────────────────

    /// <summary>교섭이 되는지 굴린다(<c>0x004A55C6</c> ~ <c>0x004A55EB</c>).</summary>
    /// <remarks>
    /// <code>
    ///   4a55ce  점수 = 웅변 x 33
    ///   4a55da  점수 += 매력
    ///   4a55e5  점수 += 1
    ///   4a55e9  점수 &gt;= rand(200) 이면 교섭이 됐다
    /// </code>
    /// </remarks>
    /// <remarks>
    /// 같은 식을 쓰는 <b>쌍둥이 루틴</b>이 <c>0x0046AA70</c> 에 하나 더 있다 — <b>조약으로
    /// 막힌 문</b>(<c>0x0046ABB0</c>)이 그것을 쓰고, 주사위가 <c>rand(150)</c> 이라 훨씬 잘 된다.
    /// 그 벌은 <see cref="Treaty"/> 가 든다.
    /// </remarks>
    public static bool Talks(Player player, GameRandom dice, int roll = 200) =>
        player.LevelOf(Skill.Names[Skill.Rhetoric]) * 33
        + player.AbilityOf(Ability.Charm) + 1 >= dice.Next(roll);

    /// <summary>
    /// 교섭이 되면 얼마를 건네는지(<c>0x004A55FB</c> ~ <c>0x004A5624</c>).
    /// </summary>
    /// <remarks>
    /// <c>rand(500) + (5 - 웅변) x 100</c> 이고 <b>소지금에서 잘린다</b> — 모자라면
    /// 있는 만큼만 낸다. 그러니 웅변이 오르면 굴림도 잘 되고 값도 싸진다.
    /// </remarks>
    public static int Price(Player player, GameRandom dice) =>
        dice.Next(500) + (5 - player.LevelOf(Skill.Names[Skill.Rhetoric])) * 100;

    /// <summary>조약 문의 뇌물 — 같은 셈에 <b>적어도 100</b>이다(<c>0x0046AACF</c>).</summary>
    public const int TreatyMinPrice = 100;

    /// <summary>
    /// <b>부관이 있는가.</b> 성문의 말은 부관이 하느냐 그냥 서술하느냐로 갈린다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   00468ef0  0x0047CC60(주인공, 0, 0)  ; 부하 칸 [주인공+0x100] 의 첫 자리
    ///   00468efe  cmp eax,1; sbb eax,eax; inc eax   ; 있으면 1
    /// </code>
    /// 그 첫 자리가 <b>부관</b>이다(<see cref="Player.MateRoles"/>). 이 값이
    /// <c>0x00469680</c>(둘 중 하나를 고른다)과 <c>0x004696B0</c>(있을 때만 말한다)을
    /// 가르고, 잠입의 말 수준 조언(<c>0x004A52F6</c>)도 이것으로 막힌다.
    ///
    /// <b>부관이 없으면 대원이 말을 안 건다.</b> 그래서 「제독, 조심하십시오」 같은 줄이
    /// 아예 안 나오고, 교섭 결과도 서술 쪽 문구로 나온다.
    /// </remarks>
    public static bool HasAide(Player player) =>
        player.Mates.Count > 0 && player.Mates[0].Length > 0;

    /// <summary>교섭이 됐을 때(<c>0x005220A0</c> · <c>0x005220C0</c> · <c>0x005220F0</c>).</summary>
    /// <remarks>돈 이야기는 <c>0x00469060</c> 이라 부관이 없어도 늘 나온다.</remarks>
    public const string PaidWord = "금화 {0}닢을 건네었습니다.";

    /// <summary>
    /// 교섭 결과. <b>둘 중 하나만</b> 나온다 — <c>0x00469680</c> 이 부관 여부로 고른다.
    /// </summary>
    public const string TalkWonWord = "잘됐습니다. 이것으로 {0}에 들어갈 수 있습니다";
    public const string TalkWonNews = "교섭에 성공했습니다. {0}에 들어갈 수 있습니다";

    /// <summary>
    /// <b>공략</b> 결과(<c>0x004689BA</c> · <c>0x00468A17</c>) — 교섭과 문구가 다르다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   이김   0x00551C28 (부관) · 0x00551C58 (부관 없음)
    ///   물러남 0x00551C90 (부관) · 0x00551CB8 (부관 없음)
    /// </code>
    /// 마을 이름이 안 들어간다 — 교섭 문구와 달리 서식 자리가 없다.
    /// </remarks>
    public const string RaidWonWord = "제독, 이것으로 마을에 들어갈 수 있습니다.";
    public const string RaidWonNews = "마을을 공략했습니다. 이것으로 마을에 들어갈 수 있습니다";
    public const string RaidLostWord = "만만치 않군요. 제독, 일단 퇴각합시다.";
    public const string RaidLostNews = "공략에 실패했습니다";

    /// <summary>
    /// 어그러졌을 때(<c>0x00552130</c> · <c>0x00552158</c>). 이것도 <b>하나만</b> 나온다.
    /// </summary>
    public const string TalkLostWord = "교섭할 수 없군요. 제독, 어떻게 할까요?";
    public const string TalkLostNews = "교섭에 실패했습니다. {0}에 들어갈 수 없습니다";

    /// <summary>돈이 없을 때(<c>0x00551CE0</c>). 검사는 <c>0x00468BF0</c> 이 한다.</summary>
    public const string TooPoorWord = "소지금이 모자랍니다!";

    /// <summary>
    /// 성문 차림표의 제목 — <b>「그라나다에 들어간다」</b> 처럼 도시 이름에 붙인다.
    /// </summary>
    /// <remarks>
    /// <c>0x00468B80</c> 이 도시 이름 뒤에 <c>0x00551CD0</c>("에 들어간다")을 이어 붙여
    /// <c>0x004A5798</c> 에서 차림표에 넘긴다.
    /// </remarks>
    public static string GateTitle(string cityName) => cityName + "에 들어간다";

    /// <summary>
    /// 성문에서 저쪽이 하는 말. <b>못 알아들으면 ×로 뭉개진다.</b>
    /// </summary>
    /// <remarks>
    /// 문지기의 말은 죄다 얼굴 대사 창(<c>0x004692E0</c>)으로 나오고, 그 창이
    /// <c>0x00469540</c> 으로 글자를 뭉갠다 — 인사만이 아니라 「침입자다! 잡아라!!」 도
    /// 그렇다. 대사 창에는 <b>제목이 없다</b>.
    /// </remarks>
    /// <param name="tongue">그 말을 아는 수준(0~3) — 두 바이트 글자마다 rand(10) &lt; {10,8,4,0}[수준] 이면 ×다.</param>
    public static string Heard(string words, int tongue) => StrangerTalk.Garble(words, tongue, System.Random.Shared);

    /// <summary>배로 왔으면 「항구」, 말로 왔으면 「마을」(<c>0x00552120</c>).</summary>
    public static string Where(bool byLand) => byLand ? "마을" : "항구";

    // ── 트루데시야스 조약 ─────────────────────────────────────────────────

    /// <summary>조약이 서는 해(<c>0x00469880</c> 의 <c>cmp [0x005A4D20], 0x5D6</c>).</summary>
    public const int TreatyYear = 1494;

    /// <summary>조약 이름과 문구(<c>0x005521F0</c> · <c>0x00552208</c> · <c>0x00552240</c>).</summary>
    /// <remarks>
    /// <b>마을과 항구의 글이 다르다</b>(<c>0x0046A6D9</c> 가 가른다). 항구 쪽은 그 나라
    /// 이름을 먼저 대고 「함대」를 막는다. 둘 다 <b>문지기가 얼굴을 걸고</b> 말한다
    /// (<c>0x004692E0</c>, 화자는 <c>[this+0x84]</c>) — 알림 상자가 아니다.
    /// </remarks>
    public const string TreatyName = "트루데시야스 조약";
    public const string TreatyWord = "{0}에 의해 {1}의 선원을 마을에 들여보낼 수는 없다.";
    public const string TreatyPortWord =
        "여기는 {0}령이다. {1}에 의해 {2}의 함대를 항구에 들여보낼 수 없다!";

    /// <summary>
    /// 조약을 무릅쓰고 들어섰을 때 부관이 하는 말(<c>0x00552280</c>).
    /// </summary>
    /// <remarks>
    /// <c>0x0046A787</c> 이 도시 객체 <c>+0x08</c> 에 1 을 박아 <b>조약을 깬 것</b>으로 적고
    /// 이 말을 낸다. <c>0x004696B0</c> 이라 <b>부관이 없으면 아무 말도 없다</b>.
    /// </remarks>
    public const string TreatyBrokenWord = "조약을 깨뜨려, 곤란하게 되었군요...";

    /// <summary>
    /// 조약에 막히는지 — <b>1494년부터 포르투갈과 에스파니아가 서로를 막는다</b>.
    /// </summary>
    /// <remarks>
    /// <c>0x00469880</c> 이 해를 보고, 이어서 내 나라(<c>0x005B394C</c>: 0 포르투갈 ·
    /// 1 에스파니아)와 그 도시 나라를 견준다. 막히면 적대도가 0 이어도 같은 차림표가
    /// 뜬다(<c>0x0046ABB0</c>) — 적대도만으로는 안 열리는 문이 하나 더 있는 셈이다.
    /// </remarks>
    /// <param name="year">지금 해.</param>
    /// <param name="mine">내 나라 번호(0 포르투갈 · 1 에스파니아).</param>
    /// <param name="theirs">그 도시가 딸린 나라 번호.</param>
    public static bool TreatyBars(int year, int mine, int theirs) =>
        year >= TreatyYear && theirs is 0 or 1 && theirs != mine;
}
