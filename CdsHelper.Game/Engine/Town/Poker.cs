namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 술집 카드 도박 「포카」의 규칙 — 덱·족보·상대 셈·돈 셈. 화면은 <c>PokerDialog</c> 가 맡는다.
/// </summary>
/// <remarks>
/// 볼트 <c>67.분석-미니 게임(포카·카드 도박)</c> §14~§22 를 옮겼다. 살아 있는 갈래만이다 —
/// 성미(<c>+0x6C4</c>)가 끼는 곳은 <c>+0x6B4</c> 가 늘 1 이라(<c>0x00405CBC</c>) 죽은 코드다.
///
/// <b>카드 한 장은 1바이트</b>다.
/// <code>
///   카드 = (무늬 &lt;&lt; 4) | 끗수      무늬 0 클럽 · 1 다이아 · 2 하트 · 3 스페이드
///                                끗수 0(2) ~ 12(A)
/// </code>
/// 술집 물건 자리와 여기 이름의 짝은 이렇다.
/// <code>
///   +0xA1  상대 패       Theirs      +0x5CC 상대 주머니   Wallet
///   +0xAC  사람 패       Mine        +0x5D0 내기돈        Stake
///   +0x5C8 소지금        Purse       +0x5D4 내 판돈       MyPot
///   +0x5D8 상대 판돈     TheirPot    +0x5DC 이번에 건 돈  LastBet
///   +0x5E4 판 수         Hands       +0x6C0 선이 상대인가  TheyDeal
/// </code>
///
/// <b>한 가지만 원본과 다르다 — 부호 버그를 고쳤다.</b> 선이 받는 쪽의 레이즈를 떠안을 때와
/// 네 바퀴 뒤 맞추기에서 원본은 모자란 돈을 <b>빼지 않고 더한다</b>(<c>0x004042E9</c>
/// <c>add [+0x5CC], (5D4-5D8)-amt</c>). 판돈은 바르게 늘면서 소지금·상대 주머니만 거꾸로 움직여
/// 모자란 돈의 두 배만큼 이득이 난다. 여기서는 치르는 쪽에서 뺀다.
///
/// 쓰리카드 교환에서 <b>높은</b> 곁패를 버리는 버그(<c>0x0040497E</c>)는 원본대로 둔다.
/// </remarks>
public sealed class Poker
{
    /// <summary>카드 수 · 한 벌 장수 · 베팅 바퀴 수.</summary>
    public const int DeckSize = 52, HandSize = 5, Rounds = 4;

    /// <summary>빈 칸. 교환에서 버린 자리가 이 값이 된다(<c>0x004025C0</c>).</summary>
    public const byte NoCard = 0xFF;

    // ── 족보(0x0045ABD0 의 돌려주는 값) ───────────────────────────────────
    public const int NoPair = 0, OnePair = 1, TwoPair = 2, ThreeOfAKind = 3, Straight = 4,
                     Flush = 5, FullHouse = 6, FourOfAKind = 7, StraightFlush = 8, RoyalFlush = 9;

    /// <summary>끗수 몇 개. 10 은 끗수 8, Q 는 10, A 는 12 다.</summary>
    private const int Ten = 8, Queen = 10, Ace = 12, Deuce = 0;

    /// <summary>소지금 위쪽 끝(<c>0x004059B0</c> 의 <c>0xF4240</c>).</summary>
    public const int GoldCap = 1_000_000;

    /// <summary>이보다 적게 가졌으면 한 번 더 묻는다(<c>0x0040655E</c> 의 <c>0x2710</c>).</summary>
    public const int PoorGold = 10_000;

    /// <summary>내기돈의 바닥.</summary>
    private const int MinStake = 5;

    /// <summary>끗수 이름표(<c>0x00569430</c>).</summary>
    public static readonly string[] RankNames =
        ["２", "３", "４", "５", "６", "７", "８", "９", "10", "Ｊ", "Ｑ", "Ｋ", "Ａ"];

    /// <summary>족보 이름표(<c>0x00569470</c>).</summary>
    public static readonly string[] HandNames =
    [
        "노 페어", "원 페어", "투 페어", "쓰리카드", "스트레이트", "플러시", "풀하우스",
        "포 카드", "스트레이트 플러시", "로얄스트레이트 플러시",
    ];

    private readonly byte[] _deck = new byte[DeckSize];
    private int _next;

    /// <summary>
    /// 판을 차린다 — 상대 주머니와 내기돈을 매긴다(<c>0x00406470</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   상한   = (5 * (10*rand(4) + rand(5)) + 55) * 200     ; 11000 ~ 45000
    ///   내기돈 = ((min(소지금, 상한) / 2000 + 2) / 5) * 5     ; 5 아래면 5
    ///   선은 상대(+0x6C0 = 1), 판 수 0
    /// </code>
    /// 덱은 여기서 한 번 짓는다(<c>0x0045A540</c>). 판마다 섞기만 한다.
    /// </remarks>
    public Poker(GameRandom dice, int gold)
    {
        Dice = dice;
        Purse = gold;
        int tens = 10 * dice.Next(4);
        Wallet = (5 * (tens + dice.Next(5)) + 55) * 200;
        Stake = Math.Max(MinStake, (Math.Min(gold, Wallet) / 2000 + 2) / 5 * 5);
        TheyDeal = true;

        int at = 0;
        for (int suit = 0; suit < 4; suit++)
            for (int rank = 0; rank < 13; rank++)
                _deck[at++] = (byte)(suit << 4 | rank);
    }

    /// <summary>이 판의 주사위(<c>0x004B7C0F</c>).</summary>
    public GameRandom Dice { get; }

    /// <summary>소지금(<c>+0x5C8</c>). 판 중에 모자라면 잠깐 음수가 될 수 있다.</summary>
    public int Purse { get; private set; }

    /// <summary>상대 주머니(<c>+0x5CC</c>).</summary>
    public int Wallet { get; private set; }

    /// <summary>내기돈(<c>+0x5D0</c>). 판 내내 그대로다.</summary>
    public int Stake { get; }

    /// <summary>내가 이번 판에 낸 돈(<c>+0x5D4</c>).</summary>
    public int MyPot { get; private set; }

    /// <summary>상대가 이번 판에 낸 돈(<c>+0x5D8</c>).</summary>
    public int TheirPot { get; private set; }

    /// <summary>이번에 건 돈(<c>+0x5DC</c>).</summary>
    public int LastBet { get; private set; }

    /// <summary>사람이 선을 잡은 판 수(<c>+0x5E4</c>). 상대가 자리를 뜰지 가를 때 본다.</summary>
    public int Hands { get; private set; }

    /// <summary>선이 상대인지(<c>+0x6C0</c>). 이긴 쪽이 다음 판 선이다.</summary>
    public bool TheyDeal { get; private set; }

    /// <summary>사람 패(<c>+0xAC</c>). 앞에서부터 연 차례로 선다.</summary>
    public byte[] Mine { get; } = new byte[HandSize];

    /// <summary>상대 패(<c>+0xA1</c>). 앞에서부터 연 차례로 선다.</summary>
    public byte[] Theirs { get; } = new byte[HandSize];

    /// <summary>사람이 받는 쪽 콜에 모자란 돈.</summary>
    public int YouOwe => TheirPot - MyPot;

    /// <summary>포카를 할 수 있는 문화권인지 — 이베리아·북유럽·지중해만이다(<c>0x0042FB00</c>).</summary>
    public static bool CanPlayIn(int culture) => culture is >= 0 and <= 2;

    // ── 카드 ────────────────────────────────────────────────────────────────

    public static int SuitOf(byte card) => card >> 4;
    public static int RankOf(byte card) => card & 0x0F;

    /// <summary>그림 번호 — TRAMP.CDS 파트 2·3 은 무늬 x 13 + 끗수 차례다.</summary>
    public static int PictureOf(byte card) => SuitOf(card) * 13 + RankOf(card);

    /// <summary>
    /// 섞는다(<c>0x0045A580</c>) — <c>rand(52)</c> 두 자리를 208번 맞바꾸고 커서를 맨 앞에 둔다.
    /// </summary>
    private void Shuffle()
    {
        for (int k = 0; k < 208; k++)
        {
            int i = Dice.Next(DeckSize);
            int j = Dice.Next(DeckSize);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }
        _next = 0;
    }

    /// <summary>한 장 뽑는다(<c>0x0045A5D0</c>). 한 판에 많아야 스물다섯 장이다.</summary>
    public byte Draw() => _deck[_next++ % DeckSize];

    /// <summary>끗수만 보고 오름차순(<c>0x0045A630</c>, 선택 정렬).</summary>
    private static void SortByRank(Span<byte> cards)
    {
        for (int i = 0; i < cards.Length - 1; i++)
        {
            int low = i;
            for (int j = i + 1; j < cards.Length; j++)
                if (RankOf(cards[j]) < RankOf(cards[low])) low = j;
            (cards[i], cards[low]) = (cards[low], cards[i]);
        }
    }

    /// <summary>
    /// <c>(끗수&lt;&lt;4)|무늬</c> 로 오름차순(<c>0x0045A720</c>). 끗수가 먼저, 같으면 무늬다.
    /// </summary>
    public static void SortByKey(Span<byte> cards)
    {
        static int Key(byte c) => RankOf(c) << 4 | SuitOf(c);
        for (int i = 0; i < cards.Length - 1; i++)
        {
            int low = i;
            for (int j = i + 1; j < cards.Length; j++)
                if (Key(cards[j]) < Key(cards[low])) low = j;
            (cards[i], cards[low]) = (cards[low], cards[i]);
        }
    }

    /// <summary>카드 바이트 오름차순 — 무늬가 먼저다(<c>0x0045A5E0</c>, 교환 창 「슈트소트」).</summary>
    public static void SortBySuit(Span<byte> cards)
    {
        for (int i = 0; i < cards.Length - 1; i++)
        {
            int low = i;
            for (int j = i + 1; j < cards.Length; j++)
                if (cards[j] < cards[low]) low = j;
            (cards[i], cards[low]) = (cards[low], cards[i]);
        }
    }

    // ── 족보 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 족보(<c>0x0045ABD0</c>). 패는 베껴 보므로 차례를 흩뜨리지 않는다.
    /// </summary>
    /// <remarks>
    /// 위에서부터 훑어 처음 맞는 것이다. 백 스트레이트(A-2-3-4-5)도 스트레이트로 친다.
    /// </remarks>
    public static int Evaluate(ReadOnlySpan<byte> hand)
    {
        byte[] h = hand[..HandSize].ToArray();
        SortByRank(h);

        bool flush = true;
        for (int i = 1; i < HandSize; i++)
            if (((h[i] ^ h[0]) & 0xF0) != 0) flush = false;
        bool straight = IsStraight(h);

        int R(int i) => RankOf(h[i]);

        if (straight && flush)
            return R(0) == Ten && R(4) == Ace ? RoyalFlush : StraightFlush;
        if (R(1) == R(2) && R(2) == R(3) && (R(0) == R(1) || R(4) == R(1))) return FourOfAKind;
        if ((R(0) == R(1) && R(1) == R(2) && R(3) == R(4))
            || (R(2) == R(3) && R(3) == R(4) && R(0) == R(1))) return FullHouse;
        if (flush) return Flush;
        if (straight) return Straight;
        for (int i = 0; i + 2 < HandSize; i++)
            if (R(i) == R(i + 1) && R(i + 1) == R(i + 2)) return ThreeOfAKind;

        int pairs = 0;
        for (int i = 0; i + 1 < HandSize; i++)
            if (R(i) == R(i + 1)) pairs++;
        return pairs switch { 2 => TwoPair, 1 => OnePair, _ => NoPair };
    }

    /// <summary>끗수 오름차순으로 놓인 다섯 장이 스트레이트인지(<c>0x0045AAD0</c>).</summary>
    private static bool IsStraight(ReadOnlySpan<byte> sorted)
    {
        bool run = true;
        for (int i = 1; i < HandSize; i++)
            if (RankOf(sorted[i]) - RankOf(sorted[i - 1]) != 1) run = false;
        if (run) return true;

        // 백 스트레이트 — 꼭대기가 A, 바닥이 2, 앞 넷이 이어진다.
        if (RankOf(sorted[4]) != Ace || RankOf(sorted[0]) != Deuce) return false;
        for (int i = 1; i < 4; i++)
            if (RankOf(sorted[i]) - RankOf(sorted[i - 1]) != 1) return false;
        return true;
    }

    /// <summary>
    /// 앞 <paramref name="k"/> 장 가운데 <b>가장 높은 짝의 카드</b>(<c>0x0045A880</c>·<c>0x0045A8B0</c>).
    /// 없으면 <see cref="NoCard"/>.
    /// </summary>
    /// <remarks>
    /// 오름차순으로 놓고 꼭대기부터 짝을 찾는다. 짝 둘 중 <b>무늬가 높은 쪽</b>을 낸다 —
    /// 원본이 어느 장을 내는지는 확인하지 못했다(무승부 가르기의 무늬 비교에만 영향이 있다).
    /// </remarks>
    public static byte HighestPair(ReadOnlySpan<byte> cards, int k)
    {
        Span<byte> c = stackalloc byte[HandSize];
        cards[..k].CopyTo(c);
        var s = c[..k];
        SortByKey(s);
        for (int i = k - 1; i > 0; i--)
            if (RankOf(s[i]) == RankOf(s[i - 1])) return s[i];
        return NoCard;
    }

    /// <summary>쓰리카드·포카드를 이루는 카드(<c>0x0045AA40</c>). 없으면 <see cref="NoCard"/>.</summary>
    private static byte SetCard(ReadOnlySpan<byte> cards)
    {
        Span<byte> s = stackalloc byte[HandSize];
        cards[..HandSize].CopyTo(s);
        SortByKey(s);
        for (int i = HandSize - 3; i >= 0; i--)
            if (RankOf(s[i]) == RankOf(s[i + 1]) && RankOf(s[i + 1]) == RankOf(s[i + 2])) return s[i + 2];
        return NoCard;
    }

    /// <summary>
    /// <b>열린 k 장만으로 짐작하는 족보</b>(<c>0x0045B360</c>). 상대가 사람 패를 어림할 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   k &gt; 3 이고 넷이 같음 → 7 · k &gt; 2 이고 이웃 셋 같음 → 3
    ///   k &gt; 3 이고 투 페어 → 2 · 페어 → 1 · 아니면 0
    /// </code>
    /// 셈 안에서 끗수로 줄 세운 뒤 이웃을 본다.
    /// </remarks>
    public static int Estimate(ReadOnlySpan<byte> cards, int k)
    {
        if (k <= 1) return NoPair;
        byte[] s = cards[..k].ToArray();
        SortByRank(s);

        int R(int i) => RankOf(s[i]);
        if (k > 3)
            for (int i = 0; i + 3 < k; i++)
                if (R(i) == R(i + 1) && R(i + 1) == R(i + 2) && R(i + 2) == R(i + 3)) return FourOfAKind;
        if (k > 2)
            for (int i = 0; i + 2 < k; i++)
                if (R(i) == R(i + 1) && R(i + 1) == R(i + 2)) return ThreeOfAKind;

        int pairs = 0;
        for (int i = 0; i + 1 < k; i++)
            if (R(i) == R(i + 1)) pairs++;
        if (k > 3 && pairs >= 2) return TwoPair;
        return pairs > 0 ? OnePair : NoPair;
    }

    /// <summary>
    /// <paramref name="a"/> 가 <paramref name="b"/> 를 이기는지(<c>0x00403050</c>).
    /// </summary>
    /// <remarks>
    /// 족보 먼저, 같으면 이렇게 가른다.
    /// <code>
    ///   0        오름차순 [4]·[3]·[2]·[1] 끗수, 그래도 같으면 [4] 무늬
    ///   1·2      가장 높은 짝 카드의 끗수, 같으면 무늬 (곁패·둘째 페어 무시)
    ///   3·7      쓰리/포 카드의 끗수, 같으면 무늬
    ///   그 밖    오름차순 [4] 끗수, 같으면 무늬 (백 스트레이트는 [3] 이 꼭대기)
    /// </code>
    /// <b>완전히 같으면 false</b> — 무승부는 상대 승이다. 무늬 차례는 클럽 &lt; 다이아 &lt; 하트 &lt; 스페이드.
    /// </remarks>
    public static bool Beats(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int ra = Evaluate(a), rb = Evaluate(b);
        if (ra != rb) return ra > rb;

        Span<byte> sa = stackalloc byte[HandSize], sb = stackalloc byte[HandSize];
        a[..HandSize].CopyTo(sa);
        b[..HandSize].CopyTo(sb);
        SortByKey(sa);
        SortByKey(sb);

        switch (ra)
        {
            case NoPair:
                for (int i = 4; i >= 1; i--)
                    if (RankOf(sa[i]) != RankOf(sb[i])) return RankOf(sa[i]) > RankOf(sb[i]);
                return SuitOf(sa[4]) > SuitOf(sb[4]);

            case OnePair:
            case TwoPair:
                return Higher(HighestPair(a, HandSize), HighestPair(b, HandSize));

            case ThreeOfAKind:
            case FourOfAKind:
                return Higher(SetCard(a), SetCard(b));

            default:
                bool straight = ra is Straight or StraightFlush;
                int ta = straight && IsWheel(sa) ? 3 : 4;
                int tb = straight && IsWheel(sb) ? 3 : 4;
                return Higher(sa[ta], sb[tb]);
        }

        static bool IsWheel(ReadOnlySpan<byte> s) => RankOf(s[4]) == Ace && RankOf(s[0]) == Deuce;

        static bool Higher(byte x, byte y) =>
            RankOf(x) != RankOf(y) ? RankOf(x) > RankOf(y) : SuitOf(x) > SuitOf(y);
    }

    // ── 한 판 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 새 판 — 덱을 섞고 <b>선만</b> 밑돈을 낸다(<c>0x00405CF7</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   [+0x5DC] = 내기돈 ; 덱 섞기
    ///   선이 상대 : 상대주머니 -= 내기돈 ; 5D8 = 내기돈 ; 5D4 = 0
    ///   선이 사람 : 소지금 -= 내기돈 ; 5D4 = 내기돈 ; 5D8 = 0 ; 판 수++
    /// </code>
    /// </remarks>
    public void BeginHand()
    {
        LastBet = Stake;
        Shuffle();
        Array.Fill(Mine, NoCard);
        Array.Fill(Theirs, NoCard);

        if (TheyDeal)
        {
            Wallet -= Stake;
            TheirPot = Stake;
            MyPot = 0;
        }
        else
        {
            Purse -= Stake;
            MyPot = Stake;
            TheirPot = 0;
            Hands++;
        }
    }

    /// <summary>
    /// 버릴 칸을 걷고 남은 카드를 앞으로 당긴다(<c>0x004025C0</c>). 뒤는 빈 칸이 된다.
    /// </summary>
    /// <returns>버린 장수.</returns>
    public static int Discard(byte[] hand, bool[] marks)
    {
        int kept = 0;
        for (int i = 0; i < HandSize; i++)
            if (!marks[i]) hand[kept++] = hand[i];
        int thrown = HandSize - kept;
        for (; kept < HandSize; kept++) hand[kept] = NoCard;
        return thrown;
    }

    /// <summary>
    /// 빈 칸 하나를 덱에서 채운다. 상대 쪽은 <b>한 장을 버리고</b> 또 뽑는다 — 원본 버릇이다
    /// (<c>0x00402B35</c>~<c>0x00402BEF</c>).
    /// </summary>
    public void DrawInto(byte[] hand, int at, bool burn)
    {
        if (burn) Draw();
        hand[at] = Draw();
    }

    /// <summary>
    /// 상대가 버릴 카드(<c>0x004048C0</c>). 상대 패를 오름차순으로 두고 표시를 낸다. 난수는 없다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   노 페어    끗수 &lt; 8 (2~9) 전부
    ///   원 페어    짝 없는 카드 중 끗수 ≤ 8 (2~10)
    ///   투 페어    곁패 한 장
    ///   쓰리카드   곁패 둘 중 <b>높은 것</b> (둘째 찾기가 자기 자신과 견주는 버그 — 그대로 둔다)
    ///   그 위      안 바꾼다
    /// </code>
    /// </remarks>
    public bool[] TheirDiscards()
    {
        SortByKey(Theirs);
        int hand = Evaluate(Theirs);
        var marks = new bool[HandSize];

        // 0x00404880 — 끗수가 한 번만 나오는 카드.
        var single = new bool[HandSize];
        for (int i = 0; i < HandSize; i++)
        {
            int same = 0;
            for (int j = 0; j < HandSize; j++)
                if (RankOf(Theirs[j]) == RankOf(Theirs[i])) same++;
            single[i] = same == 1;
        }

        switch (hand)
        {
            case NoPair:
                for (int i = 0; i < HandSize; i++) marks[i] = RankOf(Theirs[i]) < Ten;
                break;
            case OnePair:
                for (int i = 0; i < HandSize; i++) marks[i] = single[i] && RankOf(Theirs[i]) <= Ten;
                break;
            case TwoPair:
                for (int i = 0; i < HandSize; i++) marks[i] = single[i];
                break;
            case ThreeOfAKind:
                // 오름차순이라 뒤에 선 곁패가 높다 — 그것을 버린다.
                for (int i = HandSize - 1; i >= 0; i--)
                    if (single[i]) { marks[i] = true; break; }
                break;
        }
        return marks;
    }

    /// <summary>
    /// 상대가 카드를 여는 차례를 정한다(<c>0x00404C50</c> → <c>0x00404D70</c>). 교환 뒤 한 번이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0        50% 로 rand(5) 두 칸 맞바꾸기 6번(0x404C10)
    ///   1        짝 카드를 끝으로 — 짝 없는 카드부터 연다
    ///   2        카드 바이트 오름차순(0x45A5E0)
    ///   3·5·6·7  맞바꾸기 20번 (포카드는 곁패를 칸 0 으로 옮긴 뒤)
    ///   4·8·9    그대로
    /// </code>
    /// 원페어 줄은 「i=0..4 에서 짝 카드면 칸 4 와 맞바꾼다」로 적혀 있는데, 뜻(짝이 끝으로)을
    /// 따라 짝 없는 카드를 앞에, 짝을 뒤에 차례를 지켜 세운다.
    /// </remarks>
    public void ArrangeOpenOrder()
    {
        SortByKey(Theirs);
        int hand = Evaluate(Theirs);

        switch (hand)
        {
            case NoPair:
                if (Dice.Next(2) == 0) Scramble(6);
                break;

            case OnePair:
            {
                var loose = new List<byte>(HandSize);
                var paired = new List<byte>(2);
                foreach (byte c in Theirs)
                {
                    int same = 0;
                    foreach (byte d in Theirs)
                        if (RankOf(d) == RankOf(c)) same++;
                    (same > 1 ? paired : loose).Add(c);
                }
                int at = 0;
                foreach (byte c in loose) Theirs[at++] = c;
                foreach (byte c in paired) Theirs[at++] = c;
                break;
            }

            case TwoPair:
                SortBySuit(Theirs);
                break;

            case FourOfAKind:
                // 곁패는 오름차순의 맨 앞이나 맨 뒤다.
                int kicker = RankOf(Theirs[0]) != RankOf(Theirs[1]) ? 0 : 4;
                (Theirs[0], Theirs[kicker]) = (Theirs[kicker], Theirs[0]);
                Scramble(20);
                break;

            case ThreeOfAKind:
            case Flush:
            case FullHouse:
                Scramble(20);
                break;
        }
    }

    /// <summary>상대 패 두 칸을 <c>rand(5)</c> 로 골라 몇 번 맞바꾼다(<c>0x00404C10</c>).</summary>
    private void Scramble(int times)
    {
        for (int k = 0; k < times; k++)
        {
            int i = Dice.Next(HandSize);
            int j = Dice.Next(HandSize);
            (Theirs[i], Theirs[j]) = (Theirs[j], Theirs[i]);
        }
    }

    /// <summary>
    /// 상대가 걸 돈(<c>0x00403D40</c>). 선일 때도 받는 쪽일 때도 이 셈이다. −1 이면 접는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   노 페어 : rand(5)==0 → 접음 ; rand(2*내기돈)+1
    ///   그 밖   : 원 페어이고 짝이 Q 아래면 rand(10n+10)==0 → 접음
    ///             사람이 연 k 장으로 어림(0x45B360) — 내 족보보다 높고 rand(2) → 접음
    ///             둘 다 원 페어이고 사람 짝이 더 높고 rand(2) → 접음
    ///             원 페어 rand(내기돈)+1 / 그 위 내기돈 + rand(내기돈)
    ///   상대 주머니를 넘지 않는다
    /// </code>
    /// 돌려주는 값이 1 이상이거나 −1 이라 <b>상대는 콜 없이 레이즈하거나 접는다</b>. 콜(0)은
    /// 주머니가 0 일 때만 나온다.
    /// </remarks>
    /// <param name="round">베팅 바퀴(0~3).</param>
    public int TheirBet(int round)
    {
        int mine = Evaluate(Theirs);
        int amount;

        if (mine == NoPair)
        {
            if (Dice.Next(5) == 0) return -1;
            amount = Dice.Next(2 * Stake) + 1;
        }
        else
        {
            int pair = RankOf(HighestPair(Theirs, HandSize));
            if (mine == OnePair && pair < Queen && Dice.Next(10 * round + 10) == 0) return -1;

            int shown = round + (TheyDeal ? 0 : 1);        // 사람이 이미 연 장수
            int guess = Estimate(Mine, shown);
            if (guess > mine && Dice.Next(2) != 0) return -1;
            if (mine == OnePair && guess == OnePair
                && RankOf(HighestPair(Mine, shown)) > pair && Dice.Next(2) != 0) return -1;

            amount = mine == OnePair ? Dice.Next(Stake) + 1 : Stake + Dice.Next(Stake);
        }
        return Math.Min(amount, Wallet);
    }

    /// <summary>
    /// 네 바퀴 뒤 선인 상대가 접을지(<c>0x00405810</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   노 페어 : 내 최고 끗수 &lt; 사람이 연 넉 장의 최고 끗수 면 접는다(0x4058F0)
    ///   원 페어이고 짝이 Q 아래면 rand(50)==0 → 접는다   (「2% 허세」가 아니라 2% 로 접는다)
    ///   사람 넉 장 어림 &gt; 내 족보 → 접는다
    ///   둘 다 원 페어이고 내 짝이 낮으면 접는다
    /// </code>
    /// </remarks>
    public bool TheyFoldAtShowdown()
    {
        int mine = Evaluate(Theirs);

        if (mine == NoPair)
        {
            Span<byte> s = stackalloc byte[HandSize];
            Theirs.CopyTo(s);
            SortByKey(s);
            int top = 0;
            for (int i = 0; i < 4; i++) top = Math.Max(top, RankOf(Mine[i]));
            return RankOf(s[4]) < top;
        }

        int pair = RankOf(HighestPair(Theirs, HandSize));
        if (mine == OnePair && pair < Queen && Dice.Next(50) == 0) return true;

        int guess = Estimate(Mine, 4);
        if (guess > mine) return true;
        return mine == OnePair && guess == OnePair && pair < RankOf(HighestPair(Mine, 4));
    }

    // ── 돈 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 선인 상대가 건다(<c>0x00404290</c>). 둘째 바퀴부터는 받는 쪽 레이즈의 모자란 돈도 떠안는다.
    /// </summary>
    /// <remarks>원본은 모자란 돈을 주머니에 <b>더했다</b>(부호 버그). 여기서는 뺀다.</remarks>
    public void TheyLead(int round, int amount)
    {
        if (round > 0)
        {
            Wallet -= (MyPot - TheirPot) + amount;
            TheirPot = MyPot + amount;
        }
        else
        {
            Wallet -= amount;
            TheirPot += amount;
        }
        LastBet = amount;
    }

    /// <summary>선인 사람이 건다(<c>0x00404290</c> 사람 갈래). 부호 버그는 고쳤다.</summary>
    public void YouLead(int round, int amount)
    {
        if (round > 0)
        {
            Purse -= (TheirPot - MyPot) + amount;
            MyPot = TheirPot + amount;
        }
        else
        {
            Purse -= amount;
            MyPot += amount;
        }
        LastBet = amount;
    }

    /// <summary>받는 쪽 상대가 콜(0)이나 레이즈한다(<c>0x00404540</c>).</summary>
    public void TheyAnswer(int amount)
    {
        Wallet -= (MyPot - TheirPot) + amount;
        TheirPot = MyPot + amount;
        LastBet = amount;
    }

    /// <summary>받는 쪽 사람이 콜(0)이나 레이즈한다(<c>0x00404540</c> 사람 갈래).</summary>
    public void YouAnswer(int amount)
    {
        Purse -= (TheirPot - MyPot) + amount;
        MyPot = TheirPot + amount;
        LastBet = amount;
    }

    /// <summary>
    /// 까기 전에 선이 받는 쪽의 마지막 레이즈를 맞춘다(<c>0x00405D38</c> 어름). 부호 버그는 고쳤다.
    /// </summary>
    public void CatchUp()
    {
        if (TheyDeal)
        {
            Wallet -= MyPot - TheirPot;
            TheirPot = MyPot;
        }
        else
        {
            Purse -= TheirPot - MyPot;
            MyPot = TheirPot;
        }
    }

    /// <summary>
    /// 접은 쪽이 상대인지. <paramref name="drop"/> 1 은 선이, 2 는 받는 쪽이 접은 것이다.
    /// </summary>
    public bool TheyFolded(int drop) => TheyDeal ? drop == 1 : drop == 2;

    /// <summary>
    /// 사람이 판 중에 모자랐으면 있는 돈을 다 뺏긴다(<c>0x00405FC1</c>). 그랬으면 true.
    /// </summary>
    public bool SeizeDebt()
    {
        if (Purse >= 0) return false;
        Purse = 0;
        return true;
    }

    /// <summary>
    /// 판돈을 이긴 쪽에 준다. 이긴 쪽이 다음 판 선이 된다(<c>0x0040606A</c>·<c>0x004060C5</c>).
    /// </summary>
    /// <returns>사람 소지금이 백만 닢에서 잘렸는지(<c>0x004059B0</c>).</returns>
    public bool Award(bool youWon)
    {
        TheyDeal = !youWon;
        long pot = (long)MyPot + TheirPot;
        if (!youWon)
        {
            Wallet += (int)pot;
            return false;
        }

        long sum = Purse + pot;
        Purse = (int)Math.Min(GoldCap, sum);
        return sum > GoldCap;
    }

    /// <summary>판과 판 사이에 갈리는 것(<c>0x00406430</c>).</summary>
    public enum Parting
    {
        /// <summary>사람 소지금이 내기돈 아래 — 「빈털터리에게는 볼 일없네」.</summary>
        YouBroke,

        /// <summary>상대가 지난 판을 이겼다 — 사람에게 계속할지 묻는다.</summary>
        AskYou,

        /// <summary>상대 주머니가 내기돈 이하 — 「이미 빈털터리네」.</summary>
        TheyBroke,

        /// <summary>상대 주머니가 내기돈 네 배 아래 — 「뭐··· 돈이 없다구」.</summary>
        TheyShort,

        /// <summary>상대가 핑계를 대고 자리를 뜬다 — 「미안하네. 일이 생각났네」.</summary>
        TheyLeave,

        /// <summary>이어 간다 — 「자. 승부를 계속하지」.</summary>
        GoOn,
    }

    /// <summary>
    /// 다음 판을 할지 가른다(<c>0x00406430</c> · <c>0x00406340</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   소지금 &lt; 내기돈                         → 끝
    ///   상대가 이겼다                            → 사람에게 묻는다
    ///   상대 주머니 ≤ 내기돈                    → 끝
    ///   내기돈*4 &gt; 상대 주머니                   → 끝
    ///   판 수 ≥ 6                               → 끝
    ///   판 수 ≥ 4 이고 판수*10 - 운 + 99 &lt; rand(100) → 끝
    ///   그 밖                                    → 이어 감
    /// </code>
    /// </remarks>
    /// <param name="luck">제독의 운(<c>0x005B60D0</c>).</param>
    public Parting NextHand(int luck)
    {
        if (Purse < Stake) return Parting.YouBroke;
        if (TheyDeal) return Parting.AskYou;
        if (Wallet <= Stake) return Parting.TheyBroke;
        if (Stake * 4 > Wallet) return Parting.TheyShort;
        if (Hands >= 6) return Parting.TheyLeave;
        if (Hands >= 4 && Hands * 10 - luck + 99 < Dice.Next(100)) return Parting.TheyLeave;
        return Parting.GoOn;
    }

    /// <summary>
    /// 판 안팎의 대사. 게임 EXE 의 표를 그대로 옮겼다 — 한 벌에서 <c>rand(개수)</c> 로 하나를 집는다.
    /// </summary>
    /// <remarks>
    /// 줄바꿈(<c>\n</c>)이 든 것은 카드판 말풍선(<c>0x00402F70</c>)에, 없는 것은 술집 주인 얼굴을
    /// 건 대사 창(<c>0x00478280</c>, 빈 제목)에 뜬다. <c>%d</c> 는 금액이다.
    /// </remarks>
    public static class Lines
    {
        /// <summary>내기돈을 묻는다(<c>0x00535B00</c>, 예/아니오).</summary>
        public static readonly string[] AskStake =
        [
            "내기돈은 금화 %d닢이네. 승부하겠나?",
            "금화 %d닢이 내기돈이네만, 승부하겠나?",
        ];

        /// <summary>물음에 아니오(<c>0x00535B20</c>).</summary>
        public static readonly string[] Refused =
        [
            "쳇, 겁주는 거냐.",
            "뭐야, 안하는 건가.",
            "아? 승부하러 온 것이 아닌가?",
            "한 번 정도 승부해 보세.",
            "할 마음도 없으면서, 하자고 하지 말게.",
        ];

        /// <summary>소지금이 만 닢 아래일 때 한 번 더 묻는다(<c>0x00535B50</c>, 예/아니오).</summary>
        public static readonly string[] Poor =
        [
            "그다지 돈을 가지고 있지 않는 것 같은데, 괜찮은가.",
            "그 정도 돈으로 나와 승부를. 진심인가?",
            "적은 돈으로는 지고 말걸세, 그래도 하겠나?",
            "이보게, 그 돈으로 승부하자고, 정말인가?",
            "도박을 우습게 보고 있는 듯하군. 각오는 돼 있겠지?",
        ];

        /// <summary>소지금이 내기돈 아래(<c>0x00535B08</c>).</summary>
        public static readonly string[] NoMoney =
        [
            "돈이 모자라지 않나. 다시 오게.",
            "돈이 없는 녀석은 썩 나가라!",
            "내기돈도 없는 녀석이 올 곳이 아니네.",
            "내기돈도 없다고, 썩 꺼져버려!",
            "돈이 없는 녀석은 빨리 돌아가게.",
        ];

        /// <summary>선이 상대 — 딜(<c>0x005359F8</c>).</summary>
        public static readonly string[] TheyDeal =
        [
            "그러면\n카드를 돌리겠네.",
            "카드 돌리겠네.\n좋게 들어오면\n좋을텐데.",
            "자 돌리겠네.\n안심하게\n사기 치진\n않을테니.",
        ];

        /// <summary>선이 사람 — 딜(<c>0x00535A08</c>).</summary>
        public static readonly string[] YouDeal =
        [
            "빨리\n카드 돌리게.",
            "빨리 돌리게!\n난 성질이 급하네.",
            "사기 치지 말고\n카드 돌리게.\n만약, 들통나면\n잘 알겠지.",
        ];

        /// <summary>딜 뒤 혼잣말 — 족보 4 이상(<c>0x00535910</c>).</summary>
        public static readonly string[] MoodGreat =
        [
            "자네도\n운이 없군.",
            "이것이야 말로\n이긴 것이나\n다름없네.",
            "아, 이것\n카드가 잘왔군.",
            "이 승부는\n이긴 것이나\n다름없네.",
            "왔군, 왔어.\n좋은 것만\n들어왔군.",
        ];

        /// <summary>딜 뒤 혼잣말 — 투 페어·쓰리카드(<c>0x00535928</c>).</summary>
        public static readonly string[] MoodGood =
        [
            "포기할거라면\n지금 하게.",
            "헤헤, 이겼다.",
            "어, 좋군.",
            "웃, 처음부터\n페어로군.\n운이 좋군.\n헤헷.",
        ];

        /// <summary>딜 뒤 혼잣말 — 원 페어 첫 벌(<c>0x00535938</c>).</summary>
        public static readonly string[] MoodPairA =
        [
            "자, 어느것을\n버릴까.",
            "뭐야, 이것\n재수없군.",
            "이것 곤란하게\n되었군.\n어떻게 해야\n이길 수 있나...",
        ];

        /// <summary>딜 뒤 혼잣말 — 원 페어 둘째 벌(<c>0x00535948</c>).</summary>
        public static readonly string[] MoodPairB =
        [
            "흐~음, 어느것을\n버릴까.",
            "위험하군.\n정말···",
            "흐으음...\n어떻게 할까.",
        ];

        /// <summary>딜 뒤 혼잣말 — 노 페어 첫 벌(<c>0x00535958</c>).</summary>
        public static readonly string[] MoodNoneA =
        [
            "그냥, 이쯤 할까.",
            "흠,흠...\n어떻게 안되려나.",
        ];

        /// <summary>딜 뒤 혼잣말 — 노 페어 둘째 벌(<c>0x00535960</c>).</summary>
        public static readonly string[] MoodNoneB =
        [
            "곤란하군.\n어느것을 버릴까.",
            "자, 그럼\n지금부터\n실력을 보여주지.",
            "으~음, 다시\n나눠 줄 수 없겠나.\n응, 안되겠나.\n곤란하게 되었군.",
        ];

        /// <summary>선이 상대 — 사람이 먼저 교환(<c>0x005359B8</c>).</summary>
        public static readonly string[] YouSwapFirst =
        [
            "자, 몇 장\n교환할텐가?",
            "...그래.\n몇 장 교환할건가?",
            "우선 자네부터\n교환하게.\n몇 장인가?",
        ];

        /// <summary>선이 사람 — 상대가 먼저 교환(<c>0x005359C8</c>).</summary>
        public static readonly string[] TheySwapFirst =
        [
            "몇 장 교환\n할까.",
            "나부터 교환하지.\n몇 장 교환할까.\n으~음.",
            "자, 어느것을\n교환할까···\n망설여지는군.",
        ];

        /// <summary>선이 상대 — 상대가 뒤에 교환(<c>0x005359D8</c>).</summary>
        public static readonly string[] TheySwapAfter =
        [
            "자, 몇 장 교환\n할까.",
            "흠, 자네는\n그렇게 들어왔나.\n나는 몇 장 교환\n할까···",
            "곤란하군.\n어느것을 교환하면\n좋을까.",
        ];

        /// <summary>선이 사람 — 사람이 뒤에 교환(<c>0x005359E8</c>).</summary>
        public static readonly string[] YouSwapAfter =
        [
            "자네는 몇 장\n교환할텐가?",
            "다음은 자네\n차례다.\n빨리 교환하게.",
            "빨리 교환하게.\n나는 기다리는 것이\n딱 질색이네.",
        ];

        /// <summary>상대 레이즈(<c>0x00535970</c>, %d 금액).</summary>
        public static readonly string[] TheyRaise =
        [
            "금화 %d닢\n레이즈다.",
            "자아 그럼\n걸어 볼까.\n금화 %d닢.",
            "%d닢 걸겠다!\n빨리 드롭해라.\n어쩔 수 없을껄.",
            "흐음, 걸어야겠군.\n금화 %d닢···",
            "%d닢 걸겠다.\n미안하지만\n자네가 이길\n승산은 없군.",
        ];

        /// <summary>상대 드롭(<c>0x00535988</c>).</summary>
        public static readonly string[] TheyDrop =
        [
            "안됐지만\n드롭이다.",
            "드롭하겠네.\n남자는 포기하는\n것도 중요하니\n말일세.",
            "어쩔 수 없군.\n스톱이다.",
            "어떻게 된거야.\n드롭할 수 밖에\n없군.",
            "어쩔 수 없군.\n드롭이다.\n자네 꽤 하는구만.",
        ];

        /// <summary>상대 콜(<c>0x005359A0</c>).</summary>
        public static readonly string[] TheyCall =
        [
            "콜이다.",
            "모양을 보아하니\n콜이다.",
            "으음, 콜.",
            "이건 콜이다.\n자, 빨리 다음\n카드를 넘기게.",
            "콜로 가 볼까.\n후후후···",
        ];

        /// <summary>까기(<c>0x00535B70</c>).</summary>
        public static readonly string[] Showdown =
        [
            "자, 승부다.",
            "각오는 돼 있겠지?\n자, 승부다.",
            "그만두지 않은걸\n후회하지나 말게.\n승부다.",
            "행운의 여신이여.\n웃음지어 주세요.\n승부다.",
            "승부다.\n자!",
        ];

        /// <summary>상대가 접었는데 잘 접었다(<c>0x00535A18</c>).</summary>
        public static readonly string[] FoldedWisely =
        [
            "역시\n자네 승리네.\n꽤 하는구만.\n포기하길 잘했어.",
            "후... \n큰일날 뻔했다.\n그만두길 잘했군.",
            "역시 나보다\n좋지 않았나.\n그만두길 잘했군.",
            "불길한 예감이\n들었네.\n판단을 잘했군.",
            "위험했군.\n그렇게 좋게 들어\n올지는 몰랐군.",
        ];

        /// <summary>상대가 접었는데 잘못 접었다(<c>0x00535A30</c>).</summary>
        public static readonly string[] FoldedBadly =
        [
            "그만두지 말았어야\n했는데.\n감쪽같이\n속고 말았군.",
            "한방 먹었나.\n꽤 하는군, 자네.",
            "나를 제치리라\n고는...\n마음에 안드는\n녀석이군.",
            "아차!\n잘못 읽었군.\n그만두지 말았어야\n했는데.",
            "곤란하게 되었군.\n이렇다면 내 카드\n덕에 이긴 것이\n아닌가!",
        ];

        /// <summary>결판 — 사람 승리(<c>0x00535A48</c>).</summary>
        public static readonly string[] YouWon =
        [
            "자네 승리네.\n잘 하는군.",
            "아니!\n지고 말았군.",
            "굉장하군, 자네.\n자, 이름있는\n노름꾼인가?",
            "정말인가?\n운이 좋군,\n자네.",
            "믿을 수 없군.\n그렇게 올 줄은...\n읽지 못했군.",
        ];

        /// <summary>결판 — 상대 승리(<c>0x00535A60</c>, 여섯).</summary>
        public static readonly string[] TheyWon =
        [
            "하하,\n체면이 말이\n아니군.",
            "헤헤,\n내가 이겼다.",
            "좋아, 이겼다.\n오늘 난\n운이 좋군.",
            "이겼다!\n자, 어서\n하세.",
            "이겼다.\n빈털터리 되기\n전에 돌아가는\n것이 좋을 걸세.",
            "내가 이겼네.\n도망칠거라면\n지금하게.",
        ];

        /// <summary>판 중에 모자란 사람의 돈을 다 가져간다(<c>0x00535B68</c>).</summary>
        public const string Seized = "뭐야, 돈이\n없나? 있는 돈\n전부 내가\n받겠네.";

        /// <summary>판 사이 — 사람 소지금이 내기돈 아래(<c>0x00535B64</c>).</summary>
        public const string YouBroke = "빈털터리에게는 볼 일없네, 빨리 돌아가게.";

        /// <summary>판 사이 — 계속할지 묻는다(<c>0x00535A78</c>, 예/아니오).</summary>
        public static readonly string[] Again =
        [
            "한번 더 승부할텐가?",
            "계속할텐가?",
            "자, 물론 계속할테지?",
            "계속할텐가?",
            "자네가 벌인 일이다. 계속할테지?",
        ];

        /// <summary>계속한다고 했을 때(<c>0x00535A90</c>).</summary>
        public static readonly string[] AgainYes =
        [
            "그럼, 그래야지.",
            "역시, 대단한 녀석이로군.",
            "남자라면 그래야지.",
            "좋아, 마음에 들었네. 자네.",
            "좋아, 승부하세.",
        ];

        /// <summary>그만둔다고 했을 때(<c>0x00535AA8</c>).</summary>
        public static readonly string[] AgainNo =
        [
            "또 보세.",
            "그거 안됐군, 또 오게나.",
            "벌써 끝내긴가, 재미없군.",
            "왜, 그 정도뿐인가.",
            "벌써 돌아가나. 또 보세.",
        ];

        /// <summary>상대 주머니가 바닥(<c>0x00535AC0</c>).</summary>
        public static readonly string[] TheyBroke =
        [
            "이미 빈털터리네.\n봐 주게.",
            "그만두세.\n빈털터리네.",
            "아, 어쩌지.\n더 이상 돈이\n없네.",
            "미안하네만\n이 정도로\n봐 주게.",
            "더 이상 걸\n돈도 없네.\n내가 졌네.",
        ];

        /// <summary>상대 주머니가 모자람(<c>0x00535AD8</c>, 넷).</summary>
        public static readonly string[] TheyShort =
        [
            "뭐···\n돈이 없다구.\n썩 꺼지게!",
            "하하하,\n빈털터리가\n되었군.",
            "한심한 녀석이로군.",
            "안됐구만.\n돈이 생기면\n또 오게나.",
        ];

        /// <summary>상대가 자리를 뜬다(<c>0x00535B38</c>).</summary>
        public static readonly string[] TheyLeave =
        [
            "미안하네.\n일이 생각났네.\n승부는 다음번\n으로 하세.",
            "앗, 약속이···\n계속하는 것은\n다음으로 미루세.",
            "미안하네, 여자를\n기다리게 했네.\n실례하네.",
            "아야야…갑자기\n배가, 오늘은\n그만두세.",
            "앗, 식사 시간이다.\n다음에 계속하세.\n그럼, 실례.",
        ];

        /// <summary>상대가 이어 간다(<c>0x00535AE8</c>).</summary>
        public static readonly string[] GoOn =
        [
            "자.\n승부를 계속하지.",
            "다음이다.\n다음 승부를\n하자.",
            "빨리 돌리게.\n다음번엔\n마음대로 안될걸.",
            "잘하는군.\n다음 카드는\n잘 좀 돌려 주게.",
            "에이, 다음이다.\n더 이상 용납\n하지 않겠다.",
        ];
    }
}
