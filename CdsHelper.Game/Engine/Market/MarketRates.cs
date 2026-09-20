using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 도시별 시세. 아이템 값이 도시마다 다른 것은 이 값 하나로 갈린다.
/// </summary>
/// <remarks>
/// 시세는 <b>백분율</b>이다 — 100 이 정가고, 130 이면 정가의 1.3 배를 부른다.
/// 첫값은 도시 표 <c>+0x2C</c> 로 어디나 100 이고, 거래가 밀고(<see cref="TradePost.Apply"/>)
/// 매달 1일에 흔들린다(<see cref="Drift"/>). 아즈텍왕국이 발견되면 목표가 130 으로 올라
/// 게임 실물의 125~134(리스본 128 · 바르셀로나 125 · 빌바오 134)가 그 뒤의 모습이다.
///
/// 게임 실물에서 재어 본 것:
/// <code>
///   바스타드소드 정가 12000, 시세 130 인 도시 -> 15600   (12000 * 130 / 100)
/// </code>
/// 곱하는 코드를 EXE 에서 짚지는 못했다. 도시 구조체를 절대주소가 아니라 포인터로 잡아서
/// 정적으로는 안 걸린다 — 셈이 딱 떨어지는 것과 시세가 백분율 꼴인 것으로 세운 것이다.
/// </remarks>
public sealed class MarketRates
{
    /// <summary>기준 시세. 이 값이면 정가 그대로다.</summary>
    public const int Par = 100;

    /// <summary>
    /// 정가에서 벗어날 수 있는 폭. 게임 실물이 125~134 라 넉넉히 잡았다 —
    /// 표에 엉뚱한 값이 들어와도 값이 터무니없어지지 않게 막는 자리다.
    /// </summary>
    public const int MinRate = 1, MaxRate = 1000;

    /// <summary>값은 주인공(<see cref="Player.CityRates"/>)이 든다 — 세이브에 같이 적히고, 새 판이면 새로 비운다.</summary>
    private readonly Game _game;

    internal MarketRates(Game game) => _game = game;

    /// <summary>그 도시의 시세. 모르는 도시는 100. 묻기 전에 밀린 달을 먼저 센다(<see cref="Game.CatchUpMonths"/>).</summary>
    public int Of(int cityId)
    {
        _game.CatchUpMonths();
        return _game.Player.CityRateOf(cityId);
    }

    /// <summary>시세를 적어 넣는다. 100 이면 표에서 지운다 — 기본값과 같으니 들 까닭이 없다.</summary>
    public void Set(int cityId, int rate) =>
        _game.Player.SetCityRate(cityId, Math.Clamp(rate, MinRate, MaxRate));

    /// <summary>그 도시 상태(<see cref="CityState"/>). 시세처럼 밀린 달을 먼저 센다.</summary>
    public int StateOf(int cityId)
    {
        _game.CatchUpMonths();
        return _game.Player.CityStateOf(cityId);
    }

    /// <summary>정가에서 벗어나 있는 도시들.</summary>
    public IReadOnlyDictionary<int, int> Adjusted => _game.Player.CityRates;

    /// <summary>살 때 내는 값 — 정가에 그 도시 시세를 먹인 것이다.</summary>
    /// <remarks>
    /// 시세 곱하기(<c>0x00429DC0</c>)는 <c>정가 x 시세 / 100</c> 이고 정가가 있으면 최소 1 이다.
    /// <b>100 단위로 내리지 않는다</b> — 내림은 시장 매각 본체(<c>0x004B3D1D</c>)에만 있다.
    /// </remarks>
    public int BuyPrice(int listPrice, int cityId) => Scale(listPrice, Of(cityId));

    /// <summary>팔 때 받는 값.</summary>
    public int SellPrice(int listPrice, int cityId) => Apply(listPrice, Of(cityId));

    /// <summary>
    /// <c>item.json</c> 의 아이템으로 셈하는 길.
    /// </summary>
    /// <remarks>
    /// <see cref="Item.SellPrice"/> 가 <b>가게가 파는</b> 정가다 — 이름이 반대로 읽히지만
    /// 가게 쪽에서 붙인 이름이다. 시장은 EXE 표(<c>ItemTable</c>)를 쓰는 쪽이 원본이라
    /// 이 두 줄은 옛 부르는 곳을 위해 남겨 둔 것이다.
    /// </remarks>
    public int BuyPrice(Item item, int cityId) => Scale(item.SellPrice, Of(cityId));

    /// <inheritdoc cref="BuyPrice(Item, int)"/>
    public int SellPrice(Item item, int cityId) => Apply(item.BuyPrice, Of(cityId));

    /// <summary>
    /// 이 값부터는 100 단위로 내린다. 그 밑은 한 닢까지 그대로 부른다
    /// (수수 경단은 정가가 2닢이다).
    /// </summary>
    private const int RoundFrom = 1000;

    /// <summary>
    /// 정가에 시세를 먹인다. 값이 커도 넘치지 않게 <see cref="long"/> 으로 셈한다 —
    /// 가장 비싼 것이 50만이라 시세를 곱하면 int 한 줄로는 아슬아슬하다.
    /// </summary>
    /// <summary>시세만 먹인다(<c>0x00429DC0</c>) — 정가가 있으면 최소 1.</summary>
    private static int Scale(int listPrice, int rate) =>
        listPrice <= 0 ? 0
        : Math.Max(1, (int)Math.Min(int.MaxValue, (long)listPrice * rate / Par));

    /// <summary>
    /// 시세를 먹이고 100닢 단위로 내린다 — <b>정가가 있으면 최소 1</b> 이다
    /// (<c>0x00429DC0</c> 이 <c>eax &lt; 1</c> 이면 1 로 올린다).
    /// </summary>
    private static int Apply(int listPrice, int rate) =>
        listPrice <= 0 ? 0
        : Math.Max(1, Round((int)Math.Min(int.MaxValue, (long)listPrice * rate / Par)));

    /// <summary>
    /// 1000 닢부터는 100 단위로 <b>내린다</b>. 게임 매각 본체(<c>0x004B3D1C</c>)가 그렇게 한다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   cmp  eax, 1000
    ///   jl   그대로
    ///   cdq
    ///   mov  ecx, 100
    ///   idiv ecx          ; / 100
    ///   shl  eax, 2       ; x4
    ///   lea  edx, [eax + eax*4]   ; x5  -> x20
    ///   lea  eax, [edx + edx*4]   ; x5  -> x100
    /// </code>
    /// 파는 쪽에서 눈으로 확인한 규칙이다. 사는 쪽도 같은 함수를 거치는 것으로 보고 함께
    /// 걸었다 — 지금까지 본 값(바스타드소드 15600)은 이미 100 의 배수라 이 규칙이
    /// 걸리든 안 걸리든 같다. 100 의 배수가 아닌 값이 나오면 그때 갈라 주면 된다.
    /// </remarks>
    private static int Round(int price) =>
        price >= RoundFrom ? price / 100 * 100 : price;

    /// <summary>
    /// 매달 1일의 흔들림(<c>0x0042A280</c> 끝) — 도시마다 ±4 굴린 뒤 목표 쪽으로 1~4 당긴다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   T = 100 · 아즈텍왕국(192)을 누가 찾았으면 130 · 잉카제국(193)까지면 170     (0x004AAD80)
    ///   v = clamp(시세 + rand%9 - 4, 1, 250)
    ///   v &gt; T 이면 v -= rand%4+1 · v &lt; T 이면 v += rand%4+1
    /// </code>
    /// </remarks>
    public static void Drift(Player player, Random random, int cityCount, bool aztec, bool inca)
    {
        int target = aztec ? (inca ? 170 : 130) : Par;
        for (int city = 0; city < cityCount; city++)
        {
            int v = Math.Clamp(player.CityRateOf(city) + random.Next(9) - 4, 1, 250);
            if (v > target) v -= random.Next(4) + 1;
            else if (v < target) v += random.Next(4) + 1;
            player.SetCityRate(city, v);
        }
    }

    /// <summary>아즈텍왕국 · 잉카제국 발견물 번호 — 시세 목표를 올린다.</summary>
    public const int Aztec = 192, Inca = 193;
}
