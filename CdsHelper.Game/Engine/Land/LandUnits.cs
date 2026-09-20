namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전 병종 스물넷 — 이름과 갈래, 그리고 공격력·방어력 셈.
/// </summary>
/// <remarks>
/// 이름표는 게임의 <c>0x00549AB8</c>, 설명표는 <c>0x00549B20</c> 이다(둘 다 스물넷).
/// <b>설명글이 그대로 셈에 있다</b> — 「경보병은 공격력과 방어력이 기병보다 낮다」는
/// 검술 배수 6 대 9, 4 대 5 로 나타나고, 「무적제독은 전 부대중 가장 강하다」는 검술
/// 배수 18 이 그 증거다.
///
/// 공격력은 <c>0x00444AB0</c>(뜀표 <c>0x00444D7C</c>), 방어력은 <c>0x00444DD0</c>
/// (뜀표 <c>0x00445134</c>) 이다. 무력·지력은 <c>0x00446FF0</c> 이 <b>능력에 1 을 더해</b>
/// 준다.
/// </remarks>
public static class LandUnits
{
    /// <summary>병종 수.</summary>
    public const int Count = 24;

    /// <summary>병종 갈래(<c>0x00446F00</c>).</summary>
    public enum Kind
    {
        /// <summary>근접 — 앞열 한 부대를 친다(창병만 그 뒤까지 둘).</summary>
        Melee = 0,
        /// <summary>사격 — 앞열 전부대(궁병만 아무나 하나).</summary>
        Shot = 1,
        /// <summary>포 — 한 번에 전부대.</summary>
        Cannon = 2,
        /// <summary>지원 — 비 · 회복 · 춤.</summary>
        Support = 3,
    }

    /// <summary>이름 스물넷(<c>0x00549AB8</c>).</summary>
    public static readonly string[] Names =
    [
        "기병", "중장기병", "제독", "무적제독", "창병", "경보병", "낙타병", "코끼리병",
        "사무라이", "하타모토", "닌자", "인디오", "족장", "영주", "장군",
        "화승총대", "머스켓총대", "궁병", "포병", "캐논포병", "화포병",
        "주술사", "고승", "표범",
    ];

    /// <summary>병종 번호. 셈에서 자주 부르는 것만 이름을 붙였다.</summary>
    public const int Horse = 0, HeavyHorse = 1, Admiral = 2, GreatAdmiral = 3,
                     Spear = 4, Light = 5, Camel = 6, Elephant = 7,
                     Samurai = 8, Hatamoto = 9, Ninja = 10, Indio = 11,
                     Chief = 12, Lord = 13, General = 14,
                     Matchlock = 15, Musket = 16, Bow = 17,
                     Gunner = 18, Cannon = 19, Bombard = 20,
                     Shaman = 21, Monk = 22, Leopard = 23;

    /// <summary>총대장 부대인 병종 다섯(<c>0x00447340</c> 어름의 <c>+0x18</c>).</summary>
    public static bool IsLeader(int unit) =>
        unit is Admiral or GreatAdmiral or Chief or Lord or General;

    /// <summary>그 병종의 갈래.</summary>
    public static Kind KindOf(int unit) => unit switch
    {
        >= Matchlock and <= Bow => Kind.Shot,
        >= Gunner and <= Bombard => Kind.Cannon,
        >= Shaman and <= Leopard => Kind.Support,
        _ => Kind.Melee,
    };

    /// <summary>
    /// 병종별 공격력(<c>0x00444AB0</c>).
    /// </summary>
    /// <remarks>
    /// 지원 병종 셋(주술사·고승·표범)은 뜀표 밖으로 떨어져 <b>0</b> 이다 — 「공격력은
    /// 없으나」라는 설명 그대로다.
    /// </remarks>
    /// <param name="might">무력 + 1.</param>
    /// <param name="sword">검술.</param>
    /// <param name="gunnery">포술.</param>
    /// <param name="shooting">사격술.</param>
    public static int Attack(int unit, int might, int sword, int gunnery, int shooting) =>
        unit switch
        {
            Horse or Samurai => might * 8 / 10 + sword * 9,
            HeavyHorse or Hatamoto => might * 8 / 10 + sword * 12 + 10,
            Admiral or Chief or Lord or General => might + sword * 14,
            GreatAdmiral => might * 12 / 10 + sword * 18 + 12,
            Spear => might * 7 / 10 + sword * 6,
            Light => might * 6 / 10 + sword * 6,
            Camel => might * 8 / 10 + sword * 9 + 10,
            Elephant => might * 8 / 10 + sword * 10 + 12,
            Ninja => might * 8 / 10 + sword * 8,
            Indio => might * 8 / 10 + sword * 11,
            Matchlock => might * 6 / 10 + shooting * 6,
            Musket => might * 6 / 10 + shooting * 8 + 10,
            Bow => might * 6 / 10 + shooting * 5,
            Gunner => might * 6 / 10 + gunnery * 4,
            Cannon => might * 6 / 10 + gunnery * 6 + 10,
            Bombard => might * 6 / 10 + gunnery * 5 + 9,
            _ => 0,
        };

    /// <summary>병종별 방어력(<c>0x00444DD0</c>).</summary>
    /// <param name="mind">지력 + 1.</param>
    public static int Defence(int unit, int mind, int sword, int gunnery, int shooting,
                              int theology) =>
        unit switch
        {
            Horse or Samurai or Ninja => sword * 5 + mind * 4 / 10,
            HeavyHorse or Hatamoto => sword * 7 + mind * 4 / 10 + 5,
            Admiral or Lord or General => sword * 7 + mind * 6 / 10,
            GreatAdmiral => sword * 8 + mind * 6 / 10 + 9,
            Spear or Light => sword * 4 + mind * 3 / 10,
            Camel => sword * 5 + mind * 4 / 10 + 5,
            Elephant => sword * 7 + mind * 7 / 10 + 5,
            Indio => sword * 3 + mind * 3 / 10,
            Chief => sword * 5 + mind * 3 / 10 + 5,
            Matchlock => shooting * 4 + mind * 3 / 10,
            Musket => shooting * 6 + mind * 3 / 10 + 5,
            Bow => shooting * 3 + mind * 3 / 10,
            Gunner => gunnery * 4 + mind * 2 / 10,
            Cannon => gunnery * 4 + mind * 4 / 10 + 5,
            Bombard => gunnery * 5 + mind * 2 / 10 + 4,
            Shaman => theology * 2 + mind * 3 / 10,
            Monk => theology * 4 + mind * 2 / 10,
            Leopard => theology * 3 + mind * 4 / 10,
            _ => 0,
        };

    /// <summary>
    /// 갈래끼리의 상성(<c>0x004482E0</c>) — <b>근접 &gt; 포 &gt; 사격 &gt; 근접</b>.
    /// </summary>
    /// <remarks>지원은 늘 보통이다. 0 유리 · 1 보통 · 2 불리.</remarks>
    public static int Match(Kind attacker, Kind target)
    {
        if (attacker == Kind.Support || target == Kind.Support) return 1;
        if (attacker == Kind.Melee && target == Kind.Cannon) return 0;
        if (attacker == Kind.Cannon && target == Kind.Shot) return 0;
        if (attacker == Kind.Shot && target == Kind.Melee) return 0;
        if (attacker == target) return 1;
        return 2;
    }

    /// <summary>
    /// 육상전이 내는 효과음 — <b>WAVES.CDS 파트</b>다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004225A0(사운드 ID, 0)</c> 으로 낸다. 사운드 ID 0~27 은 CD 트랙이고
    /// 28~77 이 WAVE 파트라 <b>파트 = ID − 28</b> 이다
    /// (<c>WaveBank.FirstSoundId</c>).
    /// </remarks>
    public static class Sound
    {
        /// <summary>총(화승총·머스켓) — ID 0x1E.</summary>
        public const int Gun = 2;

        /// <summary>포 — ID 0x1F.</summary>
        public const int Cannon = 3;

        /// <summary>맞붙어 치기 — ID 0x20.</summary>
        public const int Melee = 4;

        /// <summary>「돌격」을 고른 턴의 첫머리 — ID 0x21 (<c>0x0044932E</c>).</summary>
        public const int Charge = 5;

        /// <summary>활 — ID 0x22.</summary>
        public const int Bow = 6;

        /// <summary>코끼리병 — ID 0x23.</summary>
        public const int Elephant = 7;

        /// <summary>고승의 회복 — ID 0x24 (<c>0x00446510</c>).</summary>
        public const int Heal = 8;

        /// <summary>표범의 춤 — ID 0x25 (<c>0x00448DAD</c>).</summary>
        public const int Dance = 9;

        /// <summary>닌자의 변신술 — ID 0x26 (<c>0x004492E1</c>).</summary>
        public const int Ninja = 10;

        /// <summary>묘책이 먹혔을 때 — ID 0x27 (<c>0x004490A9</c>).</summary>
        public const int RuseWon = 11;

        /// <summary>묘책이 어그러졌을 때 — ID 0x28 (<c>0x004490BC</c>).</summary>
        public const int RuseLost = 12;

        /// <summary>주술사가 부른 비 — ID 0x3F (<c>0x004459D9</c>).</summary>
        public const int Rain = 35;

        /// <summary>물러날 때 — ID 0x4A (<c>0x004499FF</c>).</summary>
        public const int Retreat = 46;

        /// <summary>이겼을 때 — ID 0x4D (<c>0x004499B5</c>).</summary>
        public const int Won = 49;
    }

    /// <summary>
    /// 그 병종이 <b>칠 때</b> 나는 효과음 파트(<c>0x00446B60</c>). 소리가 없으면 −1 이다.
    /// </summary>
    /// <remarks>
    /// 갈래로 가르되 코끼리병과 궁병만 제 소리를 따로 쓴다. <b>비가 오면 총·포는 불발</b>
    /// 이라 소리도 안 난다 — 궁병은 갈래가 사격이라도 비를 안 탄다. 지원 갈래는 치는
    /// 소리가 없고 저마다 제 소리를 낸다(<see cref="Sound.Rain"/> 따위).
    /// </remarks>
    public static int SoundOf(int unit, bool raining) => KindOf(unit) switch
    {
        Kind.Melee => unit == Elephant ? Sound.Elephant : Sound.Melee,
        Kind.Shot => unit == Bow ? Sound.Bow : raining ? -1 : Sound.Gun,
        Kind.Cannon => raining ? -1 : Sound.Cannon,
        _ => -1,
    };

    /// <summary>부대 자리 여섯의 이름(<c>0x00559444</c> 부터 열여섯 바이트씩).</summary>
    public static readonly string[] Places =
    [
        "전열 왼측", "전열 중앙", "전열 우측", "후열 왼측", "후열 중앙", "후열 우측",
    ];

    /// <summary>앞열인 자리인지 — 0·1·2 가 앞열이다.</summary>
    public static bool IsFront(int place) => place % 6 < 3;
}
