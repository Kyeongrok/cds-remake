using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 술집·여관에 앉은 사람들 — 사진 앞에 세우고, 말을 걸고, 한잔 사고, 부하로 삼는다.
/// </summary>
/// <remarks>
/// 값은 <see cref="Tavern"/> 가 알고, 여기서는 묻고 알리는 차례만 맡는다. 사람은
/// 게임 세이브의 인물표에서 오고(<see cref="Engine.Game.Roster"/>), 그림은
/// 손님 그림(<see cref="Engine.Game.Guests"/>)에서 온다.
///
/// 명령 창이 아니라 <b>건물 사진 창</b>에 붙는다 — 게임도 술집에 들어서면 사진 앞에
/// 손님이 서고, 그 사람을 눌러 말을 건다.
/// </remarks>
/// <param name="view">이 술집을 낸 도시 창. 대사 창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 인물표가 여기서 온다.</param>
/// <param name="cityId">이 마을 번호. 그 마을 그 건물에 앉은 사람을 찾는다.</param>
/// <param name="culture">이 마을 문화권 이름. 손님 그림을 고르는 데 쓴다.</param>
/// <param name="cultureNo">이 마을 문화권 번호. 주인 얼굴이 여기 따라 갈린다.</param>
/// <param name="hideMenu">
/// 손님과 이야기하는 동안 시설 명령 창을 접어 두라는 부탁. 게임은 손님을 누르면
/// <b>명령 창을 지우고 그 자리에</b> 고르는 줄을 낸다.
/// </param>
/// <param name="leave">술집을 나가라는 부탁 — 설득을 한 번 하고 나면 게임이 그렇게 한다.</param>
internal sealed class TavernMenu(Window view, Engine.Game game, int cityId, string culture,
                                 int cultureNo, Action<bool>? hideMenu = null,
                                 Action? leave = null)
{
    /// <summary>술집의 건물 코드. 화자표에서 주인을 찾을 때 쓴다.</summary>
    private const int BuildingCode = 4;

    /// <summary>
    /// 들어설 때 손님이 건네는 말. 게임 표(<c>0x005473C0</c>) 다섯 줄 그대로다 —
    /// 하나를 집어 내므로 <b>들어갈 때마다 갈린다</b>.
    /// </summary>
    private static readonly string[] Greetings =
    [
        "여어! 당신, 음...누구였더라? 자, 이쪽으로 오게나.",
        "헤헤, 오늘, 좋은 일이 있었는데 기분좋으니 함께 마시자구.",
        "여어, 함께 마시자구. 오늘 밤은 실컷 마시고 싶은 기분이라네.",
        "으음, 기분좋군. 여, 거기, 자네 말일세, 자네. 이쪽으로 오게 해 둘 말이 있네.",
        "헤에, 너무 마셨나. 거기 자네, 좀더 마시고 싶으니 같이 마십시다.",
    ];

    /// <summary>
    /// 인사 대신 <b>포카를 걸어 오는</b> 말 셋. 같은 표(<c>0x005473D8</c>)의 뒤쪽이다.
    /// </summary>
    /// <remarks>
    /// 게임은 굴림 하나로 인사와 이 권유를 함께 뽑는다(<c>0x0042E997</c>) — 포카를 할 수
    /// 있는 마을이면 <c>rand(8)</c> 로 굴려 <b>5~7 이 이 셋</b>이고, 아니면 <c>rand(5)</c>
    /// 라 영영 안 나온다. 그래서 <b>여덟 번에 세 번</b>이 권유다.
    /// </remarks>
    private static readonly string[] PokerCalls =
    [
        "오우, 자네 꽤 운이 있을 것 같은데, 어때, 포카로 내기하지 않겠나?",
        "여, 포카로 나와 내기하세. 도저히 따분해서 말이지.",
        "거기, 이쪽으로 오게 포카나 하세.",
    ];

    /// <summary>권유를 마다했을 때(<c>0x0054A438</c>).</summary>
    private const string PokerTurnedDown = "쳇, 재미없군.";

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _cityId = cityId;
    private readonly string _culture = culture;
    private readonly int _cultureNo = cultureNo;

    private readonly Action<bool>? _hideMenu = hideMenu;
    private readonly Action? _leave = leave;

    /// <summary>명령 창을 접어 두고 한 가지를 치른 뒤 도로 편다.</summary>
    private void Alone(Action run)
    {
        _hideMenu?.Invoke(true);
        try { run(); }
        finally { _hideMenu?.Invoke(false); }
    }

    private Player _player => _game.Player;

    /// <summary>
    /// 들어설 때 건네는 한마디. 다섯 줄 가운데 하나라 올 때마다 다르다.
    /// </summary>
    /// <remarks>
    /// <b>말하는 이는 술집 주인이 아니라 앉아 있는 손님</b>이다. 말이 "여어, 함께
    /// 마시자구" 인 것부터가 취객의 말이고, 게임도 그렇게 짓는다 — <c>0x0042E976</c> 이
    /// <c>0x004A1D10</c> 으로 <b>손님 자리를 하나 집어</b> 그 자리의 화자로 창을 낸다
    /// (자리 배열은 화면 객체 <c>+0x18</c>, 한 칸 <c>0x18</c> 바이트다).
    ///
    /// 그래서 얼굴도 그 손님 것이다. 우리는 이 술집에 앉은 사람 가운데 첫 사람의 얼굴을
    /// 쓰고, 아무도 안 앉았으면 얼굴 없이 낸다 — 지나가는 손님은 서 있는 그림만 있고
    /// 초상화가 따로 없다.
    ///
    /// 예전에는 화자표의 <b>술집 주인</b> 얼굴을 썼는데, 주인이 할 말이 아니다.
    /// </remarks>
    /// <remarks>
    /// <b>들어설 때마다 말을 걸지는 않는다.</b> 게임은 <c>0x0042E985</c> 에서
    /// <c>rand(3) != 0</c> 이면 그냥 나간다 — <b>세 번에 한 번</b>만 누가 말을 건다.
    /// 그래서 들락거려도 말이 잇달아 나오지 않는다.
    /// </remarks>
    public void Greet()
    {
        // 들어설 때마다 새 술집 객체다 — 취기는 여기서만 0 이 되고, 들려줄 소문도 새로 고른다(생성자 0x0042E870).
        _tipsy = 0;
        _drank = false;
        PickRumor();

        // 들어서면 부관이 먼저 한마디 한다(0x0042E940) — 부관이 없으면 이 줄은 통째로 없다.
        if (_game.AideFace is { } aide)
            TalkDialog.Say(_view, aide, "", "제독, 적당히 하고 있겠습니다.");

        if (_game.Random.Next(GreetDice) != 0) return;

        // 자리에서 얼굴을 못 구하면 그 마을 술집 화자로 물러선다 — 게임은 늘
        // 얼굴을 걸고 말하므로 얼굴 없는 창이 뜨는 것이 더 어긋난다.
        var face = DrinkerFace() ?? _game.SpeakerFace(BuildingCode, _cultureNo);

        // 포카를 할 수 있는 마을이면 굴림이 여덟이고, 5~7 이면 인사 대신 판을 걸어 온다
        // (0x0042E9AE). 물음 창에서 「예」면 곧바로 포카다(0x0042EA02).
        bool poker = Engine.Town.Poker.CanPlayIn(_cultureNo);
        int at = _game.Random.Next(poker ? Greetings.Length + PokerCalls.Length : Greetings.Length);
        if (at >= Greetings.Length)
        {
            // 판을 거는 말과 「쳇, 재미없군.」은 <b>술집 주인</b> 얼굴이다(0x0042E9DD 의 [+0x80]).
            var host = HostFace();
            if (ConfirmDialog.Ask(_view, PokerCalls[at - Greetings.Length], face: host)) PlayPoker();
            else ConfirmDialog.Tell(_view, PokerTurnedDown, face: host);
            return;
        }

        // 술꾼의 인사도 그 도시 나라의 말이다 — 아랍어 1레벨이면 손님 소문처럼
        // 글자가 뭉개져야 한다(0x004780E0 → 0x004252F0).
        ConfirmDialog.Tell(_view, StrangerTalk.Garble(Greetings[at], TongueLevelOfCity(), _game.Random),
                           face: face);
    }

    /// <summary>
    /// 술집에서 누가 결투를 걸어 온다(<c>0x0042FB60</c>) — <b>악명이 높을수록</b> 자주 붙는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0042fb69  rand(5) != 0 이면 아무 일 없다
    ///   0042fb89  rand(악명 / 500) &gt; rand(운 + 1) + 5 라야 걸린다
    ///   0042fbc7  rand(3) 으로 말 한 벌씩을 고른다(도전 · 부관 · 받음 · 무시)
    ///   0042fd1e  결과 0(이겨서 처형)      명성 +100 · 악명 +1000
    ///   0042fd3b  결과 1(이겨서 놓아 줌·뺏음) 악명 += rand(100) + 100
    ///             결과 2(져도 살았다)       아무 일 없다
    ///   0042fd55  결과 3(베였다)            놀이 끝
    /// </code>
    /// 판 결과는 <c>[일기토+0x1C0]</c> 이다(<c>0x004AA6F0</c>) — 처형이 0(<c>0x004AA362</c>), 그 밖의 승리가 1,
    /// 도망·용서가 2, 죽음이 3 이다. 술집 판(갈래 0)은 이기면 처형·놓아 준다·모두 뺏는다를 고른다.
    /// 「술집을 나온다」를 눌렀을 때만 굴린다(<c>0x0042FFBC</c> — 부르는 곳은 여기 하나다).
    /// 원본 함수는 늘 1 을 내 어떻게 되든 술집을 나선다.
    /// </remarks>
    /// <returns>결투가 벌어졌으면 true.</returns>
    public bool Challenged()
    {
        var dice = _game.Random;
        if (dice.Next(5) != 0) return false;

        int infamy = _player.Infamy / 500;
        if (infamy <= 0) return false;
        if (dice.Next(infamy) <= dice.Next(_player.AbilityOf(Ability.Luck) + 1) + 5) return false;

        if (PersonTable.Open().Find(BrawlPerson) is not { } row || row.Stats.Length < 5) return false;

        int k = dice.Next(3);
        var face = _game.PersonTemplates?.Find(BrawlPerson) is { } t
            ? _game.Faces?.TryGetBgra(t.Face, female: false) : null;
        var mate = _game.AideFace;
        bool hasMate = _player.MateAt(0).Length > 0;

        TalkDialog.Say(_view, face, "", Challenges[k]);
        if (hasMate) TalkDialog.Say(_view, mate, "", ChallengeAdvice[k]);

        if (ChoiceDialog.Ask(_view, "", ["도전을 받는다", "무시한다"]) != 0)
        {
            TalkDialog.Say(_view, face, "", Jeers[k]);
            return true;
        }

        TalkDialog.Say(_view, hasMate ? mate : face, "", (hasMate ? TakeUpWithMate : TakeUpAlone)[k]);

        var roll = new GameRandom(Environment.TickCount);
        int sword = row.Skills.Length > Skill.Sword ? row.Skills[Skill.Sword] : 0;
        // 상대도 무기·방어구를 굴려 든다(0x004A89D4) — 복장 갈래는 그 사람 나라의 수도 문화권이다.
        var ids = Engine.Town.Duel.GearOf(FoeSet(BrawlPerson), row.Stats[2], roll);
        var gear = (Weapon: EffectOf(ids.Weapon), Armor: EffectOf(ids.Armor));
        var foe = new Engine.Town.Duel.Fighter(BrawlName, row.Stats[0], row.Stats[2], sword,
                                               row.Stats[4], gear.Weapon, gear.Armor);
        var duel = new Engine.Town.Duel(Mine(), foe, Shielded(), Environment.TickCount);
        DuelDialog.Show(_view, duel, roll, face, _game.Fighters,
                        FighterSprites.SetForCulture(_cultureNo), arena: DuelArt.TavernFor(_cultureNo), bgm: _game.Bgm);
        if (duel.Won == true)
        {
            _player.Hurt(duel.BodyLost);
            if (Triumph(BrawlPerson, face, roll, gear: ids) == 0)
            {
                _player.Fame += BrawlFame;
                _player.Infamy += ChallengeWinInfamy;
            }
            else _player.Infamy += roll.Next(100) + 100;
            return true;
        }
        if (LostDuel(duel, face, roll, mateFought: false))
        {
            EndGame();   // 0x0042FD55
            return true;
        }
        _player.Hurt(duel.BodyLost);
        return true;
    }

    /// <summary>도전을 받아 이겼을 때 오르는 악명(<c>0x0042FD2A</c>).</summary>
    private const int ChallengeWinInfamy = 1000;

    private static readonly string[] Challenges =
    [
        "거기 자네! 마음에 안 드는군, 나랑 결투하자.",
        "어이, 거기 겁장이! 바다의 사나이라면 검을 뽑아라.",
        "어이, 나보다 강한 놈을 찾고 있다네. 우선 나와 결투해 주겠나?",
    ];

    private static readonly string[] ChallengeAdvice =
    [
        "제독, 상대하지 않는 편이 좋습니다.",
        "그런 말을 듣고 가만히 있을 수 없다. 제독! 해치웁시다.",
        "[나보다 강한자] ? 자네 머리 이상한 것 아닌가?",
    ];

    private static readonly string[] TakeUpWithMate =
    [
        "어쩔 수 없군요. 일단 손 좀 볼까요.",
        "제독! 부탁합니다.",
        "제독! 가볍게 손 봐 드리지요?",
    ];

    private static readonly string[] TakeUpAlone =
    [
        "멍청이, 지옥에서나 후회해라.",
        "그럼 그래야지! 그래야 바다의 사나이다.",
        "죽더라도 원망하지 말게.",
    ];

    private static readonly string[] Jeers =
    [
        "흠, 꼬리를 감추고 도망치긴가!",
        "겁장이! 너는 바다의 사나이가 아니다!",
        "싫다면 어쩔 수 없군.",
    ];

    // ── 술 ──────────────────────────────────────────────────────────────────

    /// <summary>마신 술의 도수를 쌓아 둔다. 이 값이 주량을 넘으면 취한다.</summary>
    private int _tipsy;

    /// <summary>주량 한 칸의 크기(<c>0x0042F027</c> 의 <c>(주량 + 1) x 50</c>).</summary>
    private const int TipsyStep = 50;

    /// <summary>
    /// 취하는 문턱 — <c>(주량 + 1) x 50</c> 이다(<c>0x0042F021</c>). 주량은 세대를 넘어야
    /// 바뀌므로 첫 대에서는 늘 50 이다.
    /// </summary>
    private int TipsyLimit => (_player.Drinking + 1) * TipsyStep;

    /// <summary>
    /// 술 한 잔. 값을 이르고 좋다면 받아 마신다(<c>0x0042F580</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0042F5CC  값 = 시세 x 표값 / 100, 적어도 1
    ///   0042F5FC  "%s%s 금화 %1d닢이네."  (조사 1 = 은/는) · YES/NO
    ///   0042F611  소지금이 모자라면 "돈 먼저 지불하게."
    ///   0042EFF8  값을 물고
    ///   0042F015  취기 += 도수
    ///   0042F030  취기가 (주량 + 1) x 50 을 넘으면 취한다
    ///   0042F0ED  안 취했으면 다섯 마디 가운데 하나
    /// </code>
    /// <b>피로도는 안 건드린다.</b> "피로가 풀렸다!" 는 그 다섯 마디 중 하나일 뿐이다.
    /// 대신 <b>컨디션이 치른 값만큼 오른다</b>(<c>0x0042EFFE</c> → <c>0x00469820(값)</c>, 0~2000 으로 자름).
    /// 취해 벌어진 일로 컨디션이 0 밑으로 가면 끝에 1 로 올려 둔다(<c>0x0042F12E</c>).
    /// </remarks>
    /// <param name="drink">표에서 고른 술.</param>
    /// <param name="shown">줄에 적힌 이름. 그 고장 말을 모르면 별칭이다.</param>
    public void Drink(DrinkTable.Drink drink, string shown)
    {
        _leaveAfter = false;
        Alone(() => DrinkOnce(drink, shown));
        // 뻗거나·싸우거나·토하거나·아내가 데리러 오면 술집을 나선다(0x0042FF32 → 0x004A2740).
        if (_leaveAfter) _leave?.Invoke();
    }

    /// <summary>취해 벌어진 일 끝에 술집을 나서야 하는지(<c>0x0042EFE0</c> 가 내는 1).</summary>
    private bool _leaveAfter;

    private void DrinkOnce(DrinkTable.Drink drink, string shown)
    {
        int price = Math.Max(_game.Rates.Of(_cityId) * drink.Price / 100, 1);
        // 값을 이르고 돈을 받는 것은 <b>술집 주인</b>이다 — 지나가는 손님(무명 손님 얼굴)이 아니다.
        var face = HostFace();

        if (!ConfirmDialog.Ask(_view, $"{shown}{GameUi.Josa(shown, "은", "는")} 금화 {price}닢이네.",
                               face: face))
            return;

        if (_player.Gold < price)
        {
            ConfirmDialog.Tell(_view, "돈 먼저 지불하게.", face: face);
            return;
        }
        PickRumor();   // 술을 시킬 때마다 소문을 새로 고른다(0x0042F61B)
        _drank = true;

        _player.Pay(price);
        _player.SetCondition(_player.Condition + price);   // 0x00469820
        try { Sip(drink); }
        finally { if (_player.Condition <= 0) _player.SetCondition(1); }   // 0x0042F137
    }

    /// <summary>마신 뒤 — 취기를 쌓고 취했는지 본다(<c>0x0042F00A</c>).</summary>
    private void Sip(DrinkTable.Drink drink)
    {
        if (drink.Proof <= 0) return;

        // 마신 뒤에 뜨는 말은 <b>얼굴이 없다</b> — 값을 이르는 창은 얼굴을 걸고 부르지만
        // (0x4692E0), 이쪽은 얼굴 없는 알림이다(0x469060, 인자가 둘뿐이다).
        _tipsy += drink.Proof;
        if (_tipsy > TipsyLimit)
        {
            ConfirmDialog.Tell(_view, "기분이 좋아졌다.........");
            Drunk();
            return;
        }

        ConfirmDialog.Tell(_view, Sips[_game.Random.Next(Sips.Length)]);
    }

    /// <summary>
    /// 취하고 나서 벌어지는 일(<c>0x0042F046</c>).
    /// </summary>
    /// <remarks>
    /// 차례가 이렇다.
    /// <list type="number">
    ///   <item><b>부관이 깨운다</b>(<c>0x0042EA40</c>) — 부관이 있고 굴림에 걸릴 때.</item>
    ///   <item><b>부인이 깨운다</b>(<c>0x0042EAA0</c>) — 제독 나라의 수도인 모항에서 아내가 있고 운·신앙 굴림에 걸릴 때.</item>
    ///   <item>아니면 <b>다섯 가지 가운데 하나</b>(<c>0x0042F07F</c>). 소지금이 100닢
    ///         이하면 넷 중에서 뽑는다 — 한턱은 낼 돈이 있어야 낸다.</item>
    /// </list>
    /// 게임은 취한 뒤 화면을 어둡게 했다 밝히는데(<c>0x004A59F0</c>), 우리는 말 창만 낸다.
    /// </remarks>
    private void Drunk()
    {
        // 취기는 안 지운다 — 게임은 술집에 들어설 때(생성자)만 0 으로 둔다. 그래서 한 번 취하면
        // 그 뒤로는 한 잔마다 또 취한다.

        string first = _player.MateAt(0);
        bool hasMate = first.Length > 0;
        var mate = hasMate && _player.MateInfoOf(first) is { } who ? MateFace(who) : null;

        // 부관이 깨운다 — <c>부관 지력 + 1 + 제독 운 + 1 &gt;= rand(150)</c> 이라야 한다
        // (0x0042F191 · 0x0042F1A7). 지력은 0x00468F10(부관, 1) 이 사람 칸 +0x24 에서 꺼낸다.
        if (hasMate && MateGuards())
        {
            ConfirmDialog.Tell(_view, "제독! 이봐요, 제독! 괜찮습니까?", face: mate);
            ConfirmDialog.Tell(_view, "부관의 목소리에 정신이 들었다");
            _player.SetCondition(_player.Condition - (_game.Random.Next(5) + 5));   // 0x0042EA74 → 0x00469850
            _player.Infamy += _game.Random.Next(5);
            return;
        }

        // <b>아내가 데리러 온다</b>(0x0042F1D0) — 여기가 제독 나라의 수도(나라 표 +0)이면서 모항이고,
        // 아내가 있고, rand(150) ≤ 운 + 신앙심 + 2 일 때다. 못 걸리면 아래 다섯 가지로 간다.
        if (_game.Nations?.Find(_player.Nation) is { } home && home.Capital == _cityId
            && _cityId == _player.HomePort && _player.Spouse.Length > 0
            && _game.Random.Next(150) <= _player.AbilityOf(Ability.Luck) + _player.AbilityOf(Ability.Faith) + 2)
        {
            var her = _game.Barmaids?.Find(_player.SpouseId) is { } wife
                ? _game.Faces?.TryGetBgra(wife.Face, female: true) : null;
            ConfirmDialog.Tell(_view, "여보, 여보, 괜찮아요?", face: her);
            ConfirmDialog.Tell(_view, "부인 목소리에 정신이 들었다");
            _leaveAfter = true;
            _player.Hurt(_game.Random.Next(5));              // 0x00469850(rand(5)) — 컨디션
            _player.Infamy += _game.Random.Next(5) + 10;     // 0x0042EB40
            return;
        }

        int pick = _game.Random.Next(_player.Gold > TreatFloor ? DrunkKinds : DrunkKinds - 1);
        switch (pick)
        {
            // 뻗음·싸움·토함은 끝나면 술집을 나선다(0x0042F0B3 · 0x0042F0C1 · 0x0042F0CF 의 esi=1).
            // 토함은 깨는 자리의 그림이 여관이다(0x0042EE0C) — 우리는 말로만 여관이라 한다.
            case 0: PassOut(hasMate, mate); _leaveAfter = true; break;
            case 1: PickFight(mate); _leaveAfter = true; break;
            case 2: ThrowUp(hasMate, mate); _leaveAfter = true; break;
            case 3: FoundMoney(hasMate, mate); break;
            default: BuyRound(mate); break;
        }
    }

    /// <summary>
    /// 뻗었을 때 부관이 지켜 주는지(<c>0x0042F180</c>).
    /// </summary>
    /// <remarks>
    /// <c>(부관 지력 + 1) + 제독 운 + 1 &gt;= rand(150)</c> 다 — 지력이 곧 눈치다.
    /// 부관이 없거나 신상을 못 찾으면 0 으로 본다.
    /// </remarks>
    private bool MateGuards()
    {
        string first = _player.MateAt(0);
        int mind = first.Length > 0 && _player.MateInfoOf(first) is { } who ? who.Mind : 0;
        return mind + 1 + _player.AbilityOf(Ability.Luck) + 1 >= _game.Random.Next(150);
    }

    /// <summary>취해서 벌어지는 가짓수와, 한턱이 나오려면 있어야 할 소지금.</summary>
    private const int DrunkKinds = 5, TreatFloor = 100;

    /// <summary>뻗는다 — 깨어 보니 돈이 없다(<c>0x0042EB50</c>).</summary>
    /// <remarks>부관이 지켜 주면 소지금의 <b>1/5</b>, 혼자면 <b>절반</b>을 잃는다.</remarks>
    private void PassOut(bool hasMate, uint[]? mate)
    {
        int lost = hasMate ? _player.Gold / 5 : _player.Gold / 2;
        _player.Pay(lost);

        if (hasMate)
        {
            ConfirmDialog.Tell(_view, "제독! 이봐요. 제독! 괜찮습니까?", face: mate);
            ConfirmDialog.Tell(_view, "부관 목소리에 정신이 들었다...");
        }
        else
        {
            ConfirmDialog.Tell(_view, "손님, 손님! 일어나세요. 벌써 아침이에요.", face: HostFace());
            ConfirmDialog.Tell(_view, "가게 주인이 깨웠다...");
        }
        if (lost > 0) ConfirmDialog.Tell(_view, "돈을 도둑 맞았다!!");
    }

    /// <summary>취해서 시비가 붙어 일기토가 벌어진다(<c>0x0042EC10</c>).</summary>
    /// <remarks>
    /// 걸리는 상대는 <b>인물 275</b>로 박혀 있다(<c>0x0042EC19</c> 의 <c>push 0x113</c>).
    /// 말은 굴림 하나로 둘 중 한 벌씩 — 제독이 걸고, 상대가 받고, 부관이 말린다.
    /// <code>
    ///   0042ec6e  제독   0x0054A6A0 · 0x0054A6D0
    ///   0042ec86  상대   0x0054A6F8 · 0x0054A718
    ///   0042ec9f  부관   0x0054A740 · 0x0054A770
    ///   0042ecd3  결과 0(이겨서 처형)         명성 +100 · 악명 +500
    ///   0042ecf6  결과 1(놓아 줌·뺏음)         악명 += rand(100) + 100
    ///             결과 2(져도 살았다)          아무 일 없다
    ///   0042ed16  결과 3(베였다)               게임 오버
    /// </code>
    /// </remarks>
    private void PickFight(uint[]? mate)
    {
        // 시비는 제독 얼굴(0x0042EC75), 받는 말은 인물 275 얼굴(0x0042EC8D)이다.
        var face = _game.PersonTemplates?.Find(BrawlPerson) is { } t
            ? _game.Faces?.TryGetBgra(t.Face, female: false) : null;
        int k = _game.Random.Next(Taunts.Length);
        ConfirmDialog.Tell(_view, Taunts[k], face: MyFace());
        ConfirmDialog.Tell(_view, Retorts[k], face: face);
        if (_player.MateAt(0).Length > 0) ConfirmDialog.Tell(_view, MateStops[k], face: mate);

        if (PersonTable.Open().Find(BrawlPerson) is not { } row || row.Stats.Length < 5) return;

        var dice = new GameRandom(Environment.TickCount);
        int sword = row.Skills.Length > Skill.Sword ? row.Skills[Skill.Sword] : 0;
        // 상대도 무기·방어구를 굴려 든다(0x004A89D4) — 복장 갈래는 그 사람 나라의 수도 문화권이다.
        var ids = Engine.Town.Duel.GearOf(FoeSet(BrawlPerson), row.Stats[2], dice);
        var gear = (Weapon: EffectOf(ids.Weapon), Armor: EffectOf(ids.Armor));
        var foe = new Engine.Town.Duel.Fighter(BrawlName, row.Stats[0], row.Stats[2], sword,
                                               row.Stats[4], gear.Weapon, gear.Armor);
        var duel = new Engine.Town.Duel(Mine(), foe, Shielded(), Environment.TickCount);

        DuelDialog.Show(_view, duel, dice, face, _game.Fighters,
                        FighterSprites.SetForCulture(_cultureNo), arena: DuelArt.TavernFor(_cultureNo), bgm: _game.Bgm);
        if (duel.Won == true)
        {
            _player.Hurt(duel.BodyLost);
            if (Triumph(BrawlPerson, face, dice, gear: ids) == 0)
            {
                _player.Fame += BrawlFame;
                _player.Infamy += BrawlWinInfamy;
            }
            else _player.Infamy += dice.Next(100) + 100;
            return;
        }
        if (LostDuel(duel, face, dice, mateFought: false))
        {
            EndGame();   // 0x0042ED16
            return;
        }
        _player.Hurt(duel.BodyLost);
    }

    /// <summary>취중에 시비가 붙는 상대(<c>0x0042EC19</c> 의 <c>0x113</c>).</summary>
    internal const int BrawlPerson = 275;

    /// <summary>
    /// 그 사람의 이름은 판이 열릴 때 <b>「술집의 술주정꾼」으로 덮인다</b>(<c>0x004A2C40</c> 이
    /// <c>0x00568E38</c> 을 인물 <c>+0xBC</c> 에 베낀다). 술집 도전·싸움·여급 연적 세 자리가
    /// 모두 같은 인물 <c>0x113</c> 을 넘기므로 셋 다 이 이름이다.
    /// </summary>
    internal const string BrawlName = "술집의 술주정꾼";

    /// <summary>이겼을 때 오르는 값(<c>0x0042ECD3</c>).</summary>
    private const int BrawlFame = 100, BrawlWinInfamy = 500;

    /// <summary>시비를 받아 주는 말(<c>0x0054A6F8</c> · <c>0x0054A718</c>).</summary>
    private static readonly string[] Retorts =
    [
        "얕보는 거냐, 이봐! 검을 빼라.",
        "무례한 놈, 지옥에 가서나 후회하거라.",
    ];

    /// <summary>토하고 뻗는다 — 여관에서 깨고 돈과 이름을 잃는다(<c>0x0042ED30</c>).</summary>
    private void ThrowUp(bool hasMate, uint[]? mate)
    {
        ConfirmDialog.Tell(_view, "기분이 나쁘다......눈이 도는군~ ~우웩~");

        int tire, lost;
        if (hasMate)
        {
            ConfirmDialog.Tell(_view, "제독, 괜찮습니까! 얼굴이 새파랗습니다. 제독, 제독!", face: mate);
            tire = _game.Random.Next(10) + 10;
            lost = 0;                                  // 부관이 있으면 돈은 안 털린다
        }
        else
        {
            ConfirmDialog.Tell(_view, "손님, 괜찮습니까! 얼굴이 새파랍니다, 손님, 손님!", face: HostFace());
            tire = _game.Random.Next(10) + 20;
            lost = Math.Min(_player.Gold, _game.Random.Next(10) + 20);
        }

        _player.SetCondition(_player.Condition - tire);   // 0x0042EDC9 → 0x00469850 — 컨디션이다
        _player.Pay(lost);
        _player.Infamy += _game.Random.Next(30) + 10;

        // 부관이 있으면 깨우는 목소리가 한 줄 먼저 든다(0x0042EE26, 부관 말 0x004695C0).
        if (hasMate) ConfirmDialog.Tell(_view, "제독! 이봐요 제독! 괜찮습니까?", face: mate);

        ConfirmDialog.Tell(_view, hasMate
            ? "부관 목소리에 정신이 들었다......어쩐지 여관같군."
            : "정신이 드는군.....아무래도 여관같군. 어떻게 여기까지 왔는지 전혀 생각이 나지 않는다.");
    }

    /// <summary>깨어 보니 모르는 돈을 쥐고 있다(<c>0x0042EF00</c>). 악명이 오른다.</summary>
    private void FoundMoney(bool hasMate, uint[]? mate)
    {
        int got = _game.Random.Next(100) + 100;

        if (hasMate)
        {
            ConfirmDialog.Tell(_view, "제독! 이봐요, 제독! 괜찮습니까?", face: mate);
            ConfirmDialog.Tell(_view, "부관 목소리에 정신이 들었다.....");
            ConfirmDialog.Tell(_view, "기억에 없는 돈을 쥐고 있었군!");
            ConfirmDialog.Tell(_view, "제독! 어떻게 된 것입니까? 그 돈...", face: mate);
        }
        else
        {
            ConfirmDialog.Tell(_view, "손님, 손님! 일어나세요. 벌써 아침이에요!", face: HostFace());
            ConfirmDialog.Tell(_view, "가게 주인이 깨웠다.");
            ConfirmDialog.Tell(_view, "기억에 없는 돈을 쥐고 있었군!");
        }

        ConfirmDialog.Tell(_view, $"금화 {got}닢을 손에 넣었다!");
        _player.Earn(got);
        _player.Infamy += hasMate ? FoundInfamyMate : FoundInfamyAlone;
    }

    /// <summary>모르는 돈을 쥐었을 때 오르는 악명 — 부관이 있으면 더 크다.</summary>
    private const int FoundInfamyMate = 100, FoundInfamyAlone = 50;

    /// <summary>한턱 낸다(<c>0x0042EE60</c>) — 돈을 쓰고 이름이 크게 오른다.</summary>
    private void BuyRound(uint[]? mate)
    {
        // 한턱 내는 말은 제독이 한다(0x0042EE6F 의 화자 0x005B60A0).
        ConfirmDialog.Tell(_view, "여~어, 주인! 여기에 있는 자들에게 한잔씩 돌리게.", face: MyFace());

        if (_player.MateAt(0).Length > 0)
            ConfirmDialog.Tell(_view, "역시 제독! 그럼 사양하지 않겠습니다.", face: mate);
        else
            ConfirmDialog.Tell(_view, "여어, 자네 마음에 들었어! 주인! 술 더 가지고 오게!",
                               face: DrinkerFace());

        _player.Pay(Math.Min(_player.Gold, _game.Random.Next(50) + 50));
        _player.Fame += _game.Random.Next(100) + 100;
        _player.Infamy += _game.Random.Next(6);
    }

    /// <summary>술집 주인 얼굴. 화자표에서 온다.</summary>
    private uint[]? HostFace() => _game.SpeakerFace(BuildingCode, _cultureNo);

    /// <summary>제독 얼굴 — 나이에 맞춘 초상화다.</summary>
    private uint[]? MyFace() =>
        _game.Faces?.TryGetBgra(PortraitAges.At(_player.Face, _player.Age, false, _game.Faces), female: false);

    /// <summary>시비 거는 말과, 부관이 말리는 말(<c>0x0042EC39</c>). 짝이 맞는 둘 중 하나다.</summary>
    private static readonly string[] Taunts =
    [
        "어이, 거기 너! 마음에 안드는군. 나와 결투하자.",
        "거기 겁장이! 남자라면 검을 뽑아라.",
    ];

    private static readonly string[] MateStops =
    [
        "제, 제독, 갑자기 무슨 말씀을 하시는 겁니까?",
        "잠깐, 잠깐만 제독! 농담이 지나치십니다.",
    ];

    /// <summary>
    /// 안 취했을 때 나오는 다섯 마디(<c>0x0042F0ED</c>). 하나를 집어 낸다.
    /// </summary>
    private static readonly string[] Sips =
    [
        "기분 좋군!",
        "꽤 맛있는 술이다!",
        "피로가 풀렸다!",
        "맛있다! 살 것 같다.",
        "몸이 따뜻해졌다!",
    ];

    /// <summary>말을 걸 낯을 가리는 주사위 — 셋에 하나다.</summary>
    private const int GreetDice = 3;

    /// <summary>
    /// 말을 거는 취객의 얼굴 — <b>이름 없는 손님</b> 가운데 맨 앞자리 사람이다.
    /// </summary>
    /// <remarks>
    /// 게임은 자리 배열을 앞에서부터 훑어 <b>인물이 안 앉은 첫 자리</b>를 집는다
    /// (<c>0x004A1D10</c>).
    /// <code>
    ///   자리 한 칸 24바이트, 배열은 화면 객체 +8
    ///   +0x00  얼굴 번호          +0x04  쓰는 말(언어)
    ///   +0x0C  인물 객체          0 이면 지나가는 손님
    ///   +0x10  서 있는 그림 번호  -1 이면 빈 자리
    ///
    ///   0x4A1D20  [자리+0x10] == -1 이면 건너뛴다
    ///   0x4A1D25  [자리+0x0C] == 0 인 첫 자리를 집는다
    /// </code>
    /// 그 자리를 <c>0x004690A0</c> 에 그대로 넘기면 <c>+0x00</c> 을 얼굴로 쓴다.
    ///
    /// <b>이름난 사람도 여급도 아니다.</b> 여급은 인물표에 있는 사람이라 <c>+0x0C</c> 가
    /// 차 있고, 앉아 있는 항해자들도 마찬가지다 — 그래서 말을 거는 것은 늘 지나가는
    /// 술꾼이다. 예전에는 여기서 <b>앉아 있는 첫 인물</b>의 얼굴을 써서 엉뚱한 사람이
    /// 말을 걸었다.
    ///
    /// 그 <c>+0x00</c> 은 무명 자리를 지을 때 <c>0x004A1530</c> 이 채운다 — 도시 문화권으로
    /// 고정 표 <c>0x004A2F80</c> 을 집은 값이라(<see cref="TavernRumors.StrangerFace"/>)
    /// 서 있는 그림과 상관없이 <b>한 마을의 무명 손님은 다 같은 얼굴</b>이다.
    /// </remarks>
    private uint[]? DrinkerFace() =>
        _game.Faces?.TryGetBgra(TavernRumors.StrangerFace(_cultureNo), female: false);

    /// <summary>
    /// 사진 앞에 세울 손님들. 술집·여관이 아니거나 그림을 못 읽었으면 빈 목록이다.
    /// </summary>
    /// <remarks>
    /// 세이브에 그 도시 그 건물로 적힌 인물을 자리에 앉히고(<see cref="TavernRoster"/>),
    /// 남는 자리는 지나가는 손님으로 채운다. 이름표는 인물이면 그 이름, 아니면 성별이다.
    /// </remarks>
    public IReadOnlyList<BuildingPhotoWindow.GuestArt> GuestArt(FacilityKind kind)
    {
        if (kind is not (FacilityKind.Tavern or FacilityKind.Inn)) return [];

        var book = _game.Guests;
        if (book == null) return [];

        byte building = kind == FacilityKind.Tavern ? TavernRoster.Tavern : TavernRoster.Inn;
        var people = Sitting(building);
        var keys = new List<int>(people.Count);
        foreach (var p in people) keys.Add(p.Index);

        var art = new List<BuildingPhotoWindow.GuestArt>(TavernGuests.MaxOnScreen);
        var maid = kind == FacilityKind.Tavern ? Standing() : null;
        bool maidSeated = false;

        foreach (var seat in book.Seat(_culture, _cityId, keys, withMaid: maid != null))
        {
            var bgra = book.TryGetBgra(seat.Art);
            if (bgra == null) continue;

            // 자리 짓는 쪽이 맨 앞에 여자를 세운다 — 술집이면 그 자리가 이 마을 여급이다.
            if (seat.Person < 0 && seat.Art.Female && !maidSeated && maid is { } her)
            {
                maidSeated = true;
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height,
                            _player.LikingOf(her.Id) > 0 ? her.Name : "여",
                            () => Alone(() => MeetBarmaid(her))));
                continue;
            }

            if (seat.Person < 0)
            {
                string label = seat.Art.Female ? "여" : "남";
                // 무명 남자 손님은 자리를 지을 때 어디서 왔는지와 할 이야기가 정해진다(0x004A15C0).
                var talk = seat.Art.Female ? null : SeatStranger();
                bool inn = kind == FacilityKind.Inn;
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height, label,
                            () => Alone(() => MeetStranger(seat.Art.Female, talk, inn))));
            }
            else
            {
                // 낯을 트기 전에는 이름이 안 보인다 — 이름표도 "남"·"여" 다.
                var who = people[seat.Person];
                bool known = Known(who);
                art.Add(new(bgra, seat.Art.Width, seat.Art.Height,
                            known ? who.ShortName : seat.Art.Female ? "여" : "남",
                            () => Alone(() => MeetPerson(who, seat.Art.Female,
                                                         inn: kind == FacilityKind.Inn))));
            }
        }
        return art;
    }

    // ── 여급 ────────────────────────────────────────────────────────────────

    /// <summary>이 마을 술집에 지금 서 있는 여급. 표를 못 읽었거나 없으면 null.</summary>
    private BarmaidTable.Barmaid? Standing() =>
        _game.Barmaids?.Standing(_cityId, _player.Date.Year);

    /// <summary>여급 얼굴. FEMALE.CDS 에서 낸다.</summary>
    private uint[]? FaceOfMaid(in BarmaidTable.Barmaid her) =>
        _game.Faces?.TryGetBgra(her.Face, female: true);

    /// <summary>
    /// 여급을 눌렀을 때.
    /// </summary>
    /// <remarks>
    /// 게임 차례 그대로다.
    /// <code>
    ///   낯 트기 전   "아름다운 여성이 있다" → 한잔 산다 · 무시한다
    ///   한잔 사면    그제야 얼굴을 내고 이름을 밝힌다 — 궁합대로 말투가 갈린다
    ///   그 뒤로      이야기한다 · 설득한다 · 떠난다
    /// </code>
    /// 낯 트기 전은 지나가는 여성과 <b>똑같이</b> 나온다 — 얼굴도 이름도 안 보인다.
    /// 게임의 선물 창(<c>0x00466AC9</c> 「무엇을 보내시겠습니까?」)은 차림표의 「선물을 보낸다」 줄이다
    /// (<see cref="Gift"/>).
    /// </remarks>
    private void MeetBarmaid(BarmaidTable.Barmaid her)
    {
        bool destined = Barmaids.Destined(_player, her);
        bool first = _player.LikingOf(her.Id) == 0;

        // 낯 트기 전에는 얼굴도 이름도 없다.
        if (first)
        {
            if (TalkDialog.Ask(_view, null, "", "아름다운 여성이 있다",
                               "한잔 산다", "무시한다") != 0) return;
            if (!BuyDrink()) return;

            _player.AddLiking(her.Id, Barmaids.FirstMeet(_player, her));
        }

        var face = FaceOfMaid(her);

        // 그 고장 말을 하나도 모르면 이야기가 안 된다(0x004664D1) — 한 마디만 듣고 끝난다.
        string cityTongue = TongueOfCity();
        if (cityTongue.Length > 0 && _player.TongueOf(cityTongue) <= 0)
        {
            TalkDialog.Say(_view, face, "", Barmaids.StrangerWord(first, destined));
            if (first && destined) _player.AddLiking(her.Id, Barmaids.StrangerLike);
            return;
        }

        // 첫 인사는 궁합과 술로 넷, 다시 왔을 때는 친밀도로 다섯이 갈린다
        // (0x00466730 · 0x004667B0). 우리는 늘 한잔을 사고 들어간다.
        string words = first
            ? Barmaids.FirstWord(destined, boughtDrink: true, her.Name)
            : Barmaids.AgainWord(_player.LikingOf(her.Id), _player.Name);

        while (true)
        {
            // 「프로포즈 한다」는 친밀도 80 이 넘고, 아내가 없고, 퇴짜를 안 맞았을 때만 선다(0x004668A0).
            bool canPropose = _player.LikingOf(her.Id) >= Barmaids.ProposeNeeded
                              && _player.Spouse.Length == 0 && !_player.WasRefusedBy(her.Id);
            var rows = canPropose
                ? (string[])["이야기한다", "설득한다", "선물을 보낸다", "프로포즈 한다", "떠난다"]
                : ["이야기한다", "설득한다", "선물을 보낸다", "떠난다"];

            int pick = TalkDialog.Ask(_view, face, "", words, rows);
            if (pick == 0) { Chat(her, destined); }
            else if (pick == 1)
            {
                // 설득은 한 번 하고 창을 접는다(0x0046664E 가 줄을 끄고 돌아간다).
                Persuade(her, face);
                _leave?.Invoke();
                return;
            }
            else if (pick == 2) Gift(her, face);
            else if (pick == 3 && canPropose)
            {
                Propose(her, face);
                _leave?.Invoke();
                return;
            }
            else
            {
                // 자리를 뜨면 친밀도에 맞는 인사를 한다(0x004668E0).
                TalkDialog.Say(_view, face, "", Barmaids.ByeWord(_player.LikingOf(her.Id)));
                return;
            }
            // 한 번 인사를 나눈 뒤로는 <b>줄만 다시 뜬다</b> — 게임은 "무슨 일이시죠?" 를
            // 되풀이하지 않는다. 빈 글이면 대사 창을 건너뛴다(TalkDialog.Ask).
            words = "";
        }
    }

    /// <summary>
    /// 「선물을 보낸다」(<c>0x00466A80</c>) — 소지품에서 선물 갈래(<see cref="Barmaids.GiftCategory"/>)만 골라 낸다.
    /// </summary>
    /// <remarks>
    /// 낼 것이 없으면 <b>아무 말 없이</b> 차림표로 돌아간다 — 게임도 목록이 비면 창을 안 띄운다.
    /// 고르면 그 물건이 소지품에서 빠지고 친밀도가 <c>친밀도 x (값/200) / 100</c> 만큼 오른 뒤,
    /// 오른 자리에 맞는 말이 나온다(<c>0x00466B70</c>).
    /// </remarks>
    private void Gift(in BarmaidTable.Barmaid her, uint[]? face)
    {
        if (_game.Items is not { } table) return;

        var slots = new List<int>();
        var names = new List<string>();
        foreach (int id in _player.Items)
        {
            if (table.Find(id) is not { } item || item.Category != Barmaids.GiftCategory) continue;
            slots.Add(id);
            names.Add(item.Name);
        }
        if (slots.Count == 0) return;

        NoticeDialog.Show(_view, "무엇을 보내시겠습니까?");
        int at = ChoiceDialog.Ask(_view, "선물 선택", names);
        if (at < 0 || at >= slots.Count) return;

        int price = table.Find(slots[at])?.BuyList ?? 0;
        _player.Drop(slots[at]);
        _player.AddLiking(her.Id, Barmaids.GiftGain(_player.LikingOf(her.Id), price));
        TalkDialog.Say(_view, face, "", Barmaids.GiftWord(_player.LikingOf(her.Id)));
    }

    /// <summary>
    /// 「이야기한다」(<c>0x00466950</c>) — 반은 그녀가 <b>제 취향</b>을 말하고(<c>0x004A3130</c>), 반은 잡담이다.
    /// </summary>
    /// <remarks>
    /// <b>친밀도는 안 오른다</b> — 원본은 이 줄에서 값을 건드리지 않는다(예전에는 우리가 +4·+2 를 얹고 있었다).
    /// 친밀도를 올리는 것은 한잔 사기·선물·설득뿐이다.
    /// </remarks>
    private void Chat(in BarmaidTable.Barmaid her, bool destined)
    {
        string words = _game.Random.Next(2) == 1
                       && StrangerTalk.OwnTasteOf(her.Personality) is { } taste
            ? taste
            : Chats[_game.Random.Next(Chats.Length)];
        TalkDialog.Say(_view, FaceOfMaid(her), "", words);
    }

    /// <summary>여급이 건네는 잡담 스물여덟(<c>0x00466972</c>, <c>0x0055B0A8</c>~<c>0x0055B530</c>).</summary>
    private static readonly string[] Chats =
    [
        "편안히 있으세요.",
        "취해서 괴롭히는 손님들이 있어요, 정말 싫어!",
        "옛, 애인? 비-밀.",
        "전에 취한 사람이 안으려고 해서 한바탕 혼쭐을 내 줬어.",
        "어디 멋있는 사람 없나~.",
        "안주라도 어떠세요.",
        "모험이 그렇게 재미있어요? 그럼 이야기 들려줘요.",
        "배는 가지고 있어요? 배가 없다면 멋이 없죠.",
        "당신 좋아하는 사람있죠? 얼굴에 씌어 있어요.",
        "응, 쉬는 날이 언제냐구? 으음, 언제냐면.",
        "응, 함께 마시자구요? 좋아요.",
        "이 가게 술은 맛있어요. 많이 드세요.",
        "바다의 사나이란, 전부 「여자보다 바다다!」 라고 말해요. 실례되는 말이야.",
        "내가 예쁘다구요? 그런말 한다해도 아무것도 안나와요.",
        "내가 예쁘다구요? 그런 뻔한 말해도 소용없어요.",
        "이 마을의 하늘은 별이 총총한게 매우 아름다워요!",
        "당신 술 잘해요? 잘하지 못하면 바다의 사나이 실격이에요!",
        "나도 배 타보고 싶어.",
        "역시 좋아하는 사람의 배에 타고 싶어.",
        "당신 도서관에 다니고 있다면서요? 학자예요?",
        "모험하는데 돈 들지요? 당신 부자예요?",
        "취해서 싸우는 사람 제일 싫어!",
        "나도 남자라면 모험을 할텐데.",
        "매일 같은 일 반복이야···나도 다른 마을에 가고 싶어~.",
        "여기는 선원들이 모이는 장소예요. 당신 어떤 배 타고 있어요?",
        "결혼? 그런 것 생각해 본 일도 없어요.",
        "어떤 여자를 좋아해요? 가르쳐 줘요.",
        "절 취하게 만들 작정이예요? 저 술 잘해요.",
    ];

    /// <summary>
    /// 「설득한다」(<c>0x00465A90</c>) — 친밀도 자리(30 · 60 · 90)마다 딴 말을 하고 그만큼만 오른다.
    /// </summary>
    /// <remarks>
    /// 문턱과 상승폭은 <see cref="Barmaids.Persuade"/> 가 든다. 90 을 넘으면 여급이 먼저 물어 오고,
    /// 거기서 아니오를 고르면 친밀도가 0 이 되고 프로포즈 줄도 영영 닫힌다.
    /// </remarks>
    private void Persuade(in BarmaidTable.Barmaid her, uint[]? face)
    {
        var dice = _game.Random;
        var talk = Barmaids.Persuade(_player, her, dice);

        if (!talk.Proposes)
        {
            _player.AddLiking(her.Id, talk.Liking - _player.LikingOf(her.Id));
            TalkDialog.Say(_view, face, "", talk.Words);
            return;
        }

        // 친밀도가 다 차면 여급이 먼저 물어 온다. 아내가 있으면 굴리지도 않고 떨어진다(0x00465AD8).
        bool ok = _player.Spouse.Length == 0
                  && Barmaids.Score(_player, her, Barmaids.Destined(_player, her),
                                    Barmaids.Suits(_player, her))
                     >= dice.Next(Barmaids.WooRoll);
        if (!ok)
        {
            TalkDialog.Say(_view, face, "", Barmaids.Fond);
            return;
        }

        if (TalkDialog.Ask(_view, face, "", Barmaids.Invitations[dice.Next(Barmaids.Invitations.Length)],
                           "그러겠소", "미안하오") == 0)
        {
            Wed(her, face);
            return;
        }

        // 물리면 그 여급과는 끝이다 — 친밀도가 0 이 되고 프로포즈 줄도 다시 안 선다(0x00465B9E).
        TalkDialog.Say(_view, face, "", Barmaids.Jilted[dice.Next(Barmaids.Jilted.Length)]);
        GameDialog.Show(_view, Barmaids.JiltedNotice);
        _player.MarkRefused(her.Id);
    }

    /// <summary>
    /// 「프로포즈 한다」(<c>0x00466150</c>) — 소지품의 유혹어를 골라 읊고 한 번에 판가름한다.
    /// </summary>
    /// <remarks>
    /// 점수는 밑점수(<see cref="Barmaids.Score"/>)에 유혹어 보너스를 더한 것이고 <c>rand(250)</c> 과 견준다.
    /// 떨어지면 <b>친밀도가 0 이 되고</b> 그 여급에게는 다시 프로포즈를 못 한다.
    /// </remarks>
    private void Propose(in BarmaidTable.Barmaid her, uint[]? face)
    {
        var dice = _game.Random;
        int bonus = 0;

        // 유혹어를 지녔으면 어느 것을 쓸지 고른다(0x00466250) — 안 쓰면 보너스도 말도 없다.
        var wooItems = _player.Items
            .Where(id => id >= Barmaids.FirstWooItem && id < Barmaids.FirstWooItem + Barmaids.WooItemCount)
            .Distinct().Order().ToList();
        if (wooItems.Count > 0)
        {
            var names = wooItems.Select(id => _game.Items?.Find(id)?.Name ?? $"유혹어 {id}").ToList();
            int at = ChoiceDialog.Ask(_view, "유혹어", names, Barmaids.NoWooItem);
            if (at >= 0 && at < wooItems.Count)
            {
                foreach (string line in Barmaids.WooWordsOf(wooItems[at]))
                    GameDialog.Show(_view, string.Format(line, her.Name));
                bonus = Barmaids.WooBonus(_cultureNo, dice);
            }
        }

        int score = bonus + Barmaids.Score(_player, her, Barmaids.Destined(_player, her),
                                           Barmaids.Suits(_player, her));
        if (score >= dice.Next(Barmaids.WooRoll))
        {
            Wed(her, face);
            return;
        }

        // 모항에서는 「이 마을을 떠날 수는 없어요」가 안 나온다(0x004661F6).
        int rows = _cityId == _player.HomePort ? 2 : Barmaids.Refusals.Length;
        TalkDialog.Say(_view, face, "", Barmaids.Refusals[dice.Next(rows)]);
        _player.MarkRefused(her.Id);
    }

    /// <summary>
    /// 맺어진다(<c>0x00465910</c>) — 넷에 한 번은 연적이 끼어들어 일기토가 붙는다(<c>0x004659C0</c>).
    /// </summary>
    private void Wed(in BarmaidTable.Barmaid her, uint[]? face)
    {
        var dice = _game.Random;
        if (dice.Next(4) == 0 && !RivalBeaten(new GameRandom(dice.Next()))) return;

        TalkDialog.Say(_view, face, "", Barmaids.Yeses[dice.Next(Barmaids.Yeses.Length)]);
        _player.Marry(her.Name, her.Id);
        DiscoveryDialog.Show(_view, _game.EventStills, Barmaids.WeddingStill,
                             string.Format(Barmaids.Married, _player.Name, her.Name));
    }

    /// <summary>
    /// 연적과의 일기토(<c>0x004659C0</c>) — 이기면 명성 +100 으로 혼인이 이어지고, 지면 악명 +500 으로 끝난다.
    /// </summary>
    /// <remarks>
    /// 연적은 <b>인물 275</b>다(<c>0x004659C8</c> 의 <c>push 0x113</c> — 술집 싸움 상대와 같은 사람). 말은 그 얼굴을
    /// 걸고 한다(<c>0x004659F0</c> · <c>0x00465A1B</c>). 판 갈래가 5 라 이겨도 처형 여부를 묻지 않는다.
    /// 판 결과 0·1 이면 이기고, 2(져도 살았다)면 악명 +500, 3(베였다)이면 놀이가 끝난다(<c>0x00465A64</c>).
    /// </remarks>
    /// <returns>혼인을 이어도 되면 참.</returns>
    private bool RivalBeaten(GameRandom dice)
    {
        var face = _game.PersonTemplates?.Find(BrawlPerson) is { } t
            ? _game.Faces?.TryGetBgra(t.Face, female: false) : null;
        TalkDialog.Say(_view, face, "", Barmaids.RivalWord);

        if (PersonTable.Open().Find(BrawlPerson) is not { } row || row.Stats.Length < 5) return true;
        int sword = row.Skills.Length > Skill.Sword ? row.Skills[Skill.Sword] : 0;
        // 상대도 무기·방어구를 굴려 든다(0x004A89D4) — 복장 갈래는 그 사람 나라의 수도 문화권이다.
        var ids = Engine.Town.Duel.GearOf(FoeSet(BrawlPerson), row.Stats[2], dice);
        var gear = (Weapon: EffectOf(ids.Weapon), Armor: EffectOf(ids.Armor));
        var foe = new Engine.Town.Duel.Fighter(BrawlName, row.Stats[0], row.Stats[2], sword,
                                               row.Stats[4], gear.Weapon, gear.Armor);
        var duel = new Engine.Town.Duel(Mine(), foe, Shielded(), dice.Next());
        DuelDialog.Show(_view, duel, dice, face, _game.Fighters,
                        FighterSprites.SetForCulture(_cultureNo), arena: DuelArt.TavernFor(_cultureNo), bgm: _game.Bgm);

        if (duel.Won == true)
        {
            _player.Hurt(duel.BodyLost);
            TalkDialog.Say(_view, face, "", Barmaids.RivalBeaten);
            GameDialog.Show(_view, Barmaids.RivalFame);
            _player.Fame += Barmaids.RivalFameUp;
            return true;
        }

        // 여급 연적은 판 종류 5 라 도망도 용서도 없다 — 지면 바로 죽는다(0x004A9EDE).
        if (LostDuel(duel, face, dice, mateFought: false, canFlee: false, canSpare: false))
        {
            EndGame();   // 0x00465A64 → 0x0044AF40(4)
            return false;
        }
        _player.Hurt(duel.BodyLost);
        _player.Infamy += Barmaids.RivalInfamyUp;
        return false;
    }

    /// <summary>이 도시 나라의 말. 모르면 빈 글이다.</summary>
    private string TongueOfCity()
    {
        int nation = _game.CityRows?.NationOf(_cityId) ?? -1;
        int language = _game.Nations?.Find(nation)?.Language ?? -1;
        return language >= 0 && language < Skill.Languages.Length ? Skill.Languages[language] : "";
    }

    /// <summary>그 도시 나라 말을 제독이 알아듣는 수준.</summary>
    private int TongueLevelOfCity()
    {
        int nation = _game.CityRows?.NationOf(_cityId) ?? -1;
        int language = _game.Nations?.Find(nation)?.Language ?? -1;
        return language >= 0 && language < Skill.Languages.Length
            ? _player.TongueOf(Skill.Languages[language]) : Skill.MaxLevel;
    }

    /// <summary>
    /// 이름 없는 손님을 눌렀을 때. 게임 문구를 그대로 옮겼다(<c>0x0054AC40</c>·<c>0x0054AB98</c>).
    /// </summary>
    /// <remarks>
    /// 남자 손님도 먼저 눈에 띈 것을 알리고 묻는다 — 술집은 「술을 마시고 있는 남자가 있다」
    /// [한잔 산다 · 무시한다](<c>0x0042F4F0</c>)이고 <b>술을 사야</b>(<c>0x0042F250</c>) 입을 연다.
    /// 여관은 「머무는 손님이 있다」 [말을 건다 · 무시한다](<c>0x004A5070</c>, 건물 코드 5)로 술 없이 듣는다.
    /// </remarks>
    private void MeetStranger(bool female, StrangerSeat? seat, bool inn = false)
    {
        // 여자 손님은 예전 그대로 — 한잔 사서 낯을 트는 자리다.
        if (female)
        {
            if (TalkDialog.Ask(_view, null, "", "아름다운 여성이 있다",
                               "한잔 산다", "무시한다") == 0) BuyDrink();
            return;
        }

        if (inn)
        {
            if (TalkDialog.Ask(_view, null, "", "머무는 손님이 있다", "말을 건다", "무시한다") != 0) return;
        }
        else if (TalkDialog.Ask(_view, null, "", "술을 마시고 있는 남자가 있다",
                                "한잔 산다", "무시한다") != 0 || !BuyDrink()) return;

        // 무명 손님은 <b>이야기만</b> 건넨다 — 고용도 결투도 없다(0x004A4E60).
        seat ??= SeatStranger();
        var face = _game.Faces?.TryGetBgra(TavernRumors.StrangerFace(seat.Culture), female: false)
                   ?? _game.SpeakerFace(BuildingCode, _cultureNo);
        var dice = _game.Random;

        // 손님 말은 그 도시 나라의 말이다(0x004A1530). 제독·부관(자리 0·3) 누구도 모르면 못 듣는다.
        var rows = _game.CityRows;
        int nationId = rows?.NationOf(_cityId) ?? -1;
        int language = _game.Nations?.Find(nationId)?.Language ?? -1;
        int mine = language is >= 0 and < 14 ? _player.TongueOf(Skill.Languages[language]) : FluentTongue;
        int best = mine;
        string relayer = "";
        if (language is >= 0 and < 14)
            foreach (int slot in (int[])[FirstMateSlot, InterpreterSlot])
                if (RowOf(_player.MateAt(slot)) is { } mate && mate.Languages[language] > best)
                {
                    best = mate.Languages[language];
                    relayer = _player.MateAt(slot);
                }
        if (best <= 0)
        {
            ConfirmDialog.Tell(_view, StrangerTalk.Lost, face: face);
            return;
        }

        string? line = seat.Line;
        if (line == null) return;

        // 제독 수준으로 뭉개 들려주고, 부관이 더 잘하면 부관이 옮긴다(0x004690A0).
        ConfirmDialog.Tell(_view, StrangerTalk.Garble(line, mine, dice), face: face);
        if (relayer.Length > 0 && _player.MateInfoOf(relayer) is { } who)
            TalkDialog.Say(_view, MateFace(who), "", StrangerTalk.Relay(StrangerTalk.Garble(line, best, dice), false));
    }

    /// <summary>무명 손님 자리 — 온 곳의 문화권(얼굴이 여기서 갈린다)과 할 이야기.</summary>
    private sealed record StrangerSeat(int Culture, string? Line);

    /// <summary>
    /// 무명 손님 자리를 짓는다(<c>0x004A15C0</c>) — 반쯤은 그 나라 <b>수도</b>에서 온 손님이라(<c>0x004A14F0</c>)
    /// 얼굴과 이야기 갈래가 수도 문화권을 따르고, 이야기는 이때 하나로 정해진다(<c>0x004A43F0</c>).
    /// </summary>
    private StrangerSeat SeatStranger()
    {
        var dice = _game.Random;
        _game.CatchUpMonths();
        int culture = _cultureNo;
        int nationId = _game.CityRows?.NationOf(_cityId) ?? -1;
        if (dice.Next(2) != 0 && _game.Nations?.Find(nationId) is { Capital: >= 0 } nation)
            culture = _game.CityRows?.CultureOf(nation.Capital) ?? culture;

        (string, int)? woman = _game.Barmaids?.Standing(_cityId, _player.Date.Year) is { } her
                               && her.Id != _player.SpouseId
            ? (her.Name, her.Personality) : null;
        string? line = StrangerTalk.Pick(culture, _cultureNo, _player.RumorsOf(_cityId), woman,
                                         () => TavernRumors.Of(culture, dice), dice);
        return new StrangerSeat(culture, line);
    }

    /// <summary>
    /// 그 사람과 낯을 텄는지. 술집에서 한잔 사거나 말을 걸어 용건을 물었으면 아는 사이고,
    /// 제 부하도 물론 안다. 세이브의 고용상태로는 가르지 않는다.
    /// </summary>
    /// <summary>
    /// 그 건물에 <b>앉아 있는</b> 사람들.
    /// </summary>
    /// <remarks>
    /// <b>이미 고용한 사람은 뺀다.</b> 부하가 되면 배를 타고 따라다니지 술집에 남아
    /// 있을 까닭이 없다. 인물 표에는 그대로 그 도시 그 건물이 적혀 있으므로
    /// (게임은 고용 칸을 3 으로 바꿔 표에서 걷는다) 여기서 걸러 낸다.
    /// </remarks>
    private IReadOnlyList<TavernRoster.Person> Sitting(byte building)
    {
        var people = _game.Roster?.At(_cityId, building) ?? [];
        if (_player.MateCount == 0) return people;

        return [.. people.Where(p => !_player.HasMate(p.Name))];
    }

    /// <remarks>
    /// 고용상태(2 고용가능)로 아는 사이를 가르면 <b>처음 들어간 여관에서도 이름이 뜬다</b>.
    /// 게임은 사람마다 「모르는 사이」 깃발을 들고 있다가 용건을 물을 때(<c>0x004A4BB0</c> →
    /// <c>0x004321C0</c>, <c>[인물+0xF8] = 0</c>) 걷는다 — 곧 실제로 말을 걸어야 안다.
    /// </remarks>
    private bool Known(TavernRoster.Person who) =>
        _player.HasMet(who.Name) || _player.HasMate(who.Name);

    /// <summary>
    /// 인물을 눌렀을 때. <b>낯을 텄는지에 따라 두 갈래</b>다 — 게임도 인물 객체의
    /// <c>vtbl[0x34]</c> 하나로 이렇게 가른다(<c>0x0042F3D0</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>모르는 사람</b> — "술을 마시고 있는 남자가 있다"(<c>0x0054AC40</c> 자리에
    ///         "남자"·"여" 이름표 <c>0x005609C8</c> 를 끼운 것)로 부르고 <b>한잔 산다</b>만 된다.
    ///         한잔 사면 낯을 튼다.</item>
    ///   <item><b>아는 사람</b> — "[이름]이 있다"(<c>0x0054AC20</c>)로 부르고
    ///         <b>말을 건다</b>가 뜬다. 말을 걸면 용건을 묻는다.</item>
    /// </list>
    /// 말을 걸었을 때 뜰 줄은 게임이 인물 갈래(<c>+0xE8</c>)로 고르는데(<c>0x004A4DE0</c>),
    /// 우리는 세이브의 고용상태(1 대화만 · 2 고용가능 · 3 고용중)로 대신한다.
    /// </remarks>
    /// <param name="inn">
    /// 여관 손님인지. 여관(<c>0x004A4F80</c>)은 한잔 사는 줄이 없다 — 모르는 사람이면
    /// 「낯선 %s%s 있다」(<c>0x005518A8</c>), 아는 사람이면 「[%s]%s 있다」(<c>0x005518B8</c>)
    /// 뒤에 <b>말을 건다 · 무시한다</b>를 내고 곧장 용건으로 간다.
    /// </param>
    private void MeetPerson(TavernRoster.Person who, bool female, bool inn = false)
    {
        // 사진은 들어올 때 세운 그대로라, 방금 들인 부하가 아직 서 있을 수 있다 — 두 번 들이지 않는다.
        if (_player.HasMate(who.Name)) return;

        var face = FaceOf(who);
        bool known = Known(who);

        // <b>이 줄에는 얼굴이 안 붙는다.</b> 「…이 있다」 는 그 사람이 하는 말이 아니라
        // 눈에 띄었다는 서술이라 게임도 그냥 알림으로 낸다. 얼굴은 말을 걸고 나서부터다.
        // 이름표는 게임 표(0x005609C8)의 「남자」·「여」다.
        string label = female ? "여" : "남자";

        // 술집에서 모르는 사람은 「술을 마시고 있는 %s%s 있다」(0x0054ABF0)에 한잔 산다 · 무시한다이고,
        // 한잔 사면 <b>곧바로 그 사람과 이야기로</b> 넘어간다(0x0042F4B0 → 0x004A4DE0) — 따로 인사하는 말은 없다.
        if (!known && !inn)
        {
            if (TalkDialog.Ask(_view, null, "", $"술을 마시고 있는 {label}{Subject(label)} 있다",
                               "한잔 산다", "무시한다") != 0) return;
            if (!BuyDrink()) return;
        }
        else
        {
            string line = known ? $"[{who.Name}]{Subject(who.Name)} 있다"
                                : $"낯선 {label}{Subject(label)} 있다";
            if (TalkDialog.Ask(_view, null, "", line, "말을 건다", "무시한다") != 0) return;
        }

        // 일기토는 <b>역사 항해자 열넷에게만</b> 건다. 게임도 차림표를 짓고 나서
        // 조건이 안 맞으면 그 줄을 지운다(0x004A4AA0 이 0x00468F70 의 답을 보고
        // [esp+0x18] 을 0 으로 눕힌다). 그 조건은 아직 못 밝혔고, 실제 놀이에서
        // 역사 인물에게만 뜨는 것을 보고 그대로 맞춘다.
        bool hireable = who.Hire == TavernRoster.Hireable;
        bool duelable = who.Index < PersonTable.VoyagerCount;

        // 말이 전혀 안 통하면 용건도 못 묻는다(0x004A4BB0 → 0x00468F70).
        if (TongueWith(who.Index) <= 0)
        {
            TalkDialog.Say(_view, face, "", "무슨 말을 하는 건지, 전혀 모르겠군.");
            return;
        }
        TalkDialog.Say(_view, face, "", "무슨 용건인가?");
        // 용건을 묻고 나면 아는 사이가 된다 — 게임도 여기서 「모르는 사이」 깃발을 걷는다(0x004321C0).
        _player.Meet(who.Name);

        // 게임은 차림표를 <b>되풀이해</b> 낸다. 「정보를 듣는다」는 한 번 들으면 줄이 사라지고,
        // 인물 판에서 중단하거나 자리·말 검사에서 물리면 차림표로 돌아온다.
        bool heard = false;
        while (true)
        {
            var rows = new List<string>();
            int hearAt = -1, hireAt = -1, duelAt = -1;
            if (!heard) { hearAt = rows.Count; rows.Add("정보를 듣는다"); }
            if (hireable) { hireAt = rows.Count; rows.Add("부하로 고용한다"); }
            if (duelable) { duelAt = rows.Count; rows.Add("일기토를 신청한다"); }
            rows.Add("떠난다");

            int at = ChoiceDialog.Ask(_view, "", rows[..^1], rows[^1]);
            if (at < 0 || at >= rows.Count - 1) return;

            if (at == duelAt) { Duel(who, face); return; }
            if (at == hearAt)
            {
                // 게임은 이 사람 몫으로 대본이 넣어 둔 말(0x005AA278, 역사 항해자 대본 26 0A)이 살아 있으면 그것을,
                // 없으면 <b>그 사람 고향 문화권의 소문</b>을 한 마디 한다(0x004A45E0 → 0x004A4790
                // → 0x004A4630 갈래 0 → 0x004A3740).
                _game.CatchUpMonths();
                TalkDialog.Say(_view, face, "", _player.PersonLineOf(who.Index)
                                                ?? TavernRumors.Of(HomeCulture(who.Index), _game.Random));
                heard = true;
                continue;
            }
            if (at == hireAt && Hire(who, face)) return;
        }
    }

    /// <summary>
    /// 그 사람이 소문을 꺼낼 문화권 — 제 나라 수도의 문화권이고, 모르면 이 술집 마을 것이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A4590</c> 이다. 도시(<c>0x00429970</c>)의 <c>+0x58</c> 문화권을 밑값으로
    /// 두었다가, 인물의 나라 형편 칸(<c>0x00477EA0</c>)에서 수도를 얻으면 그 도시의 <c>+0x58</c>
    /// 로 덮는다. 나라는 세이브에 없어 인물 밑표(<see cref="PersonTemplate"/>)에서 온다.
    /// </remarks>
    private int HomeCulture(int person)
    {
        if (_game.PersonTemplates?.Find(person) is { } template
            && _game.Nations?.Find(template.Nation) is { } nation
            && _game.CityRows is { } cities
            && cities.CultureOf(nation.Capital) is var culture and >= 0)
            return culture;
        return _cultureNo;
    }

    /// <summary>
    /// 일기토를 신청한다(<c>0x004A4AA0</c> 의 둘째 줄).
    /// </summary>
    /// <remarks>
    /// 게임 차례 그대로다.
    /// <list type="number">
    ///   <item>부관이 있으면 "제독! 진심이십니까?", 없으면 "일기토를 신청합니다.
    ///         좋습니까?" 로 한 번 되묻는다(<c>0x004A4B6A</c>).</item>
    ///   <item>상대가 달아나려 든다 — <b>체력에 주사위 오십씩</b>을 얹어 견주고 못
    ///         미치면 놓친다(<c>0x004A494B</c>).</item>
    ///   <item>판이 열린다(<see cref="DuelDialog"/>).</item>
    ///   <item>이기면 <b>처형한다 · 놓아 준다 · 모두 뺏는다</b>를 고른다
    ///         (<c>0x004A8380(3)</c>, <see cref="Triumph"/>). 예전에는 "술집 판은 종류가 8 이라
    ///         안 뜬다" 고 적어 두었는데 실제 게임에서는 뜬다 — 반란 판(7)만 이 줄이 없다.</item>
    ///   <item>지면 도망·용서·죽음으로 갈린다(<see cref="Engine.Town.Duel.FateOf"/>).</item>
    ///   <item>이기든 지든 <b>남은 부위의 평균만큼 체력이 준다</b>.</item>
    /// </list>
    /// </remarks>
    /// <summary>그 사람의 성미 여덟 칸(<c>vtbl+0x24</c> = <c>0x00477FE0</c>). 밑표를 못 읽으면 다 보통이다.</summary>
    private int[] FortuneOf(TavernRoster.Person who) =>
        _game.PersonTemplates?.Find(who.Index) is { } t
            ? Engine.Sea.FleetRaid.FortuneOf(t.Face, t.Blood, t.Nation)
            : [1, 1, 1, 1, 1, 1, 1, 1];

    private void Duel(TavernRoster.Person who, uint[]? face)
    {
        string ask = _player.MateCount > 0
            ? "제독! 진심이십니까?"
            : "일기토를 신청합니다. 좋습니까?";
        if (!ConfirmDialog.Ask(_view, ask, "일기토", face)) return;

        // 상대가 먼저 한 마디 한다(0x004A48A0) — <b>성미 일곱째 칸</b>(무신경 0 ~ 신경질 2)으로
        // 세 묶음이 갈리고 그 안에서 굴린다.
        var dice = new GameRandom(Environment.TickCount);
        TalkDialog.Say(_view, face, "", Engine.Town.Duel.Taunt(FortuneOf(who), dice));

        // 상대가 <b>달아나려 드는지부터</b> 성미로 가른다(0x004A4901) — 안 들면 그대로 붙는다.
        var temper = FortuneOf(who);
        if (Engine.Town.Duel.TriesToFlee(temper[Engine.Town.Duel.TauntSlot], dice))
        {
            // 쫓기 전에 한마디 — 부관 있음/없음 두 벌이다(0x004A4937 의 0x00469680).
            string fled = _player.MateAt(0).Length > 0
                ? "앗, 도망쳤다!"
                : $"{who.Name}{GameUi.Josa(who.Name, "이", "가")} 도망쳤다!";
            TalkDialog.Say(_view, _game.AideFace, "", fled);

            // 쫓는 값은 제독 체력이지만 <b>부관 것이 더 크면 그것</b>이다(0x004A4964).
            int chase = _player.AbilityOf(Ability.Body);
            if (_player.MateInfoOf(_player.MateAt(0)) is { } chaser)
                chase = Math.Max(chase, chaser.Body);

            if (!Engine.Town.Duel.Caught(chase, who.Body, dice))
            {
                // 0x004A49A6 — 부관이 있으면 부관이 이르고, 없으면 이름 없이 상자만 뜬다.
                if (_game.AideFace is { } aide)
                    TalkDialog.Say(_view, aide, "", "도망쳐 버렸군요....");
                else
                    NoticeDialog.Show(_view, "도망쳤다!");
                return;
            }
        }

        var mate = SendMate(dice);
        var duel = new Engine.Town.Duel(mate is { } m ? MateSide(m) : Mine(),
                                        Theirs(who, dice), Shielded(), Environment.TickCount);
        DuelDialog.Show(_view, duel, dice, face, _game.Fighters,
                        FighterSprites.SetForCulture(_cultureNo),
                        myFace: _game.Faces?.TryGetBgra(
                            PortraitAges.At(_player.Face, _player.Age, false, _game.Faces),
                            female: false),
                        arena: DuelArt.TavernFor(_cultureNo),
                        bgm: _game.Bgm);

        int lost = duel.BodyLost;

        if (duel.Won == true)
        {
            // 「일기토를 신청한다」는 판 종류 8 이라 <b>승리 차림표가 안 뜬다</b> —
            // 종류 2·5·7 이상은 처형·놓아 준다·모두 뺏는다를 통째로 건너뛰고 결과 1 만
            // 세운다(0x004AA2B1~0x004AA2CC). 그래서 물러가는 말과 행동 늦어짐만 남는다
            // (0x004A49F1~0x004A4A6A).
            Setback(who, face, dice);
        }
        else if (LostDuel(duel, face, dice, mate is { }))
        {
            // 베였으면 놀이가 끝난다(0x004A4A74 → 0x0044AF40(4)).
            EndGame();
            return;
        }

        // 대신 나간 사람이 다친다.
        if (mate is { } hurt) _player.HurtMate(hurt.Name, lost);
        else _player.Hurt(lost);
    }

    /// <summary>
    /// 일기토에 진 뒤처리(<c>0x004A9E50</c>) — 도망·용서·죽음을 가르고 그 말을 낸다. 베였으면 true.
    /// </summary>
    /// <remarks>
    /// 판이 무엇이든(술집 손님 · 도전 · 싸움) 같은 뒤처리다. 베이면 판의 결과가 3 이 되고
    /// 부르는 쪽이 <c>0x0044AF40(4)</c> 로 놀이를 끝낸다(<c>0x004A4A74</c> · <c>0x0042FD55</c> · <c>0x0042ED16</c>).
    /// </remarks>
    private bool LostDuel(Engine.Town.Duel duel, uint[]? face, GameRandom dice, bool mateFought,
                          bool canFlee = true, bool canSpare = true) =>
        LostDuel(_view, _player, duel, face, dice, mateFought, canFlee, canSpare);

    /// <inheritdoc cref="LostDuel(Engine.Town.Duel, uint[], GameRandom, bool)"/>
    /// <param name="canFlee">
    /// 도망을 굴릴 수 있는 판인지 — 술집·여관 무대(4 이상)뿐이다(<c>0x004A9EE4</c>).
    /// </param>
    /// <param name="canSpare">
    /// 용서를 굴릴 수 있는 판인지 — 갑판(무대 0)과 종류 0·3·8 이 아닌 판은 곧바로 죽는다
    /// (<c>0x004A9EBE</c>).
    /// </param>
    internal static bool LostDuel(Window view, Player player, Engine.Town.Duel duel, uint[]? face,
                                  GameRandom dice, bool mateFought,
                                  bool canFlee = true, bool canSpare = true)
    {
        switch (duel.FateOf(player.Fame, canFlee, canSpare, player.Crew))
        {
            case Engine.Town.Duel.Fate.Fled:
                NoticeDialog.Show(view, "안되겠다. 이길 수가 없군! 틈을 봐서 도망쳐야겠다!", "일기토");
                // 등 뒤로 한마디 듣는다(0x004A9F78 의 rand(5)).
                TalkDialog.Say(view, face, "", Jeered[dice.Next(Jeered.Length)]);
                return false;
            case Engine.Town.Duel.Fate.Spared:
                TalkDialog.Say(view, face, "", Spared[dice.Next(Spared.Length)]);
                return false;
            default:
                // <b>부관을 내보냈으면 말이 다르다</b>(0x004AA0A9 의 [+0x138]==1) —
                // 상대가 그 다음으로 제독에게 눈을 돌린다. 도망까지 실패한 판
                // ([+0x1D0] > 3)에서는 「너의 고용주도 함께 처리해 주겠다.」(0x00534538)
                // 하는데, 우리 판정은 도망 실패를 따로 내지 않아 그 갈래는 안 쓴다.
                // 제독이 몸소 졌으면 다섯 말 가운데 하나다(0x004AA142 의 rand(5)). 「자네, 제독감이
                // 아니로군…」은 반란 판(갈래 7)에서 부관이 졌을 때만의 말이다(0x004AA0B6).
                TalkDialog.Say(view, face, "", mateFought
                    ? AfterMate[dice.Next(AfterMate.Length)]
                    : Slain[dice.Next(Slain.Length)]);
                return true;
        }
    }

    /// <summary>놀이 끝 — 그림 0x0B 와 CONTINUE? 뒤 첫 화면으로(<c>0x0044AF40(4)</c>).</summary>
    private void EndGame()
    {
        if (_view is CityPicView city) city.EndGame();
    }

    /// <summary>
    /// 부관을 대신 내보낼지 묻는다(<c>0x004A8611</c>).
    /// </summary>
    /// <remarks>
    /// 제독이 부관보다 세면 <b>부관이 꺼린다</b> — 그 말을 하고 한 번 더 묻는다. 세기는
    /// <c>(무력+1)/20 + 검술*10</c> 으로 잰다(<c>0x004A86CD</c>). 부관이 더 세면 흔쾌히
    /// 나서고 다시 묻지 않는다.
    /// </remarks>
    private Player.MateInfo? SendMate(GameRandom dice) => SendMate(_view, _player, _game, dice);

    /// <inheritdoc cref="SendMate(GameRandom)"/>
    internal static Player.MateInfo? SendMate(Window view, Player player, Engine.Game game, GameRandom dice)
    {
        string first = player.Mates.FirstOrDefault(m => m.Length > 0) ?? "";
        if (first.Length == 0 || player.MateInfoOf(first) is not { } mate) return null;
        if (!ConfirmDialog.Ask(view, "　부관을 싸우게 하겠습니까?", "일기토")) return null;

        int mine = (player.AbilityOf(Ability.Might) + 1) / MateEdge
                 + player.LevelOf(Skill.Names[Skill.Sword]) * MateSwordWeight;
        int theirs = (mate.Might + 1) / MateEdge + mate.Sword * MateSwordWeight;

        var face = game.Faces?.TryGetBgra(mate.Face, female: false);
        if (mine <= theirs)
        {
            TalkDialog.Say(view, face, "", MateEager[dice.Next(MateEager.Length)]);
            return mate;
        }

        TalkDialog.Say(view, face, "", MateShy[dice.Next(MateShy.Length)]);
        return ConfirmDialog.Ask(view, "　부관을 싸우게 하겠습니까?", "일기토") ? mate : null;
    }

    /// <summary>부관 세기를 재는 잣대 — 무력을 스물로 나누고 검술에 열을 곱한다.</summary>
    internal const int MateEdge = 20, MateSwordWeight = 10;

    /// <summary>부관이 꺼릴 때 하는 말(<c>0x005341F8</c> 다섯).</summary>
    internal static readonly string[] MateShy =
    [
        "옛, 저 말입니까? 제독이 더 강하지 않습니까?",
        "그다지 자신은 없지만, 해 보겠습니다.",
        "제가 일기토를? 제독보다 약한 제가 말입니까?",
        "저보고 싸우라고요? 제독이 나가는 편이 이길 확률이 높을 텐데요.",
        "일기토 말입니까... 제독이 나가는 편이 나을 거라 생각합니다만...",
    ];

    /// <summary>부관이 나설 때 하는 말(<c>0x00534330</c> 다섯).</summary>
    internal static readonly string[] MateEager =
    [
        "저에게 맡겨 주십시오! 기필코 이기겠습니다.",
        "저를 지명하리라고는, 역시 제독이십니다.",
        "봐 주십시오. 저런 녀석은 한번에 쓰러뜨리겠습니다.",
        "제가 활약할 때가 온 것 같군요, 맡겨 주십시오.",
        "저런 녀석, 혼 줄을 내버리겠습니다.",
    ];

    /// <summary>부관 몫. 무기·방어구는 제독의 것을 그대로 쓴다(게임도 그렇다).</summary>
    private Engine.Town.Duel.Fighter MateSide(in Player.MateInfo mate) =>
        new(mate.Name, mate.Body, mate.Might, mate.Sword, mate.Luck,
            Best(Engine.Town.Duel.WeaponCategory), Best(Engine.Town.Duel.ArmorCategory));

    /// <summary>부관 얼굴. 못 구하면 null.</summary>
    private uint[]? MateFace(in Player.MateInfo who) =>
        _game.Faces?.TryGetBgra(who.Face, female: false);

    /// <summary>이겼을 때 상대가 남기는 말(<c>0x005348A8</c> 다섯).</summary>
    internal static readonly string[] Beaten =
    [
        "제길, 기억해 두어라.",
        "오늘은 여기까지 해 두지. 그럼.",
        "다음 번엔 어림도 없다, 각오해라.",
        "이 굴욕은 잊지 않겠다.",
        "나를 죽이지 않은 것을 언젠가 후회하게 해 주마.",
    ];

    /// <summary>졌는데 봐 줄 때 하는 말(<c>0x005346E8</c> 다섯).</summary>
    /// <summary>
    /// 대신 싸운 <b>부관이 졌을 때</b> 상대가 내뱉는 말 셋(<c>0x004AA12C</c>, <c>rand(3)</c>).
    /// </summary>
    internal static readonly string[] AfterMate =
    [
        "적의 제독도 한꺼번에 없애버려라!",
        "녀석들! 적의 제독도 쓰러뜨려라!",
        "하는 김에 적의 제독도 쓰러뜨려라!",
    ];

    /// <summary>제독을 쓰러뜨린 상대가 하는 말 다섯(<c>0x00534560</c>~, <c>0x004AA142</c> 의 <c>rand(5)</c>).</summary>
    internal static readonly string[] Slain =
    [
        "죽어라!",
        "상대를 잘못 만난 것 같군...",
        "미안하지만, 죽어줘야겠네.",
        "네 여행도 여기까지다.",
        "저 세상에서나 후회하거라.",
    ];

    /// <summary>
    /// 도망친 뒤 <b>등 뒤로 듣는 말</b> 다섯(<c>0x004A9F4D</c>, <c>rand(5)</c>).
    /// </summary>
    internal static readonly string[] Jeered =
    [
        "쳇, 도망치는 건 빠른 것 같군.",
        "도망치긴가, 한심한 녀석이군.",
        "이봐, 기다려라!",
        "너, 그래도 남자라고 할 수 있느냐.",
        "꽁무니를 빼다니.",
    ];

    internal static readonly string[] Spared =
    [
        "칫, 병아린가. 용서해 주지.",
        "너 같은 녀석 죽여도 자랑할게 못된다.",
        "여자와 아이, 약한 자들은 죽이지 않는 주의라서...",
        "너 같은 녀석 죽일 가치도 없다. 빨리 사라져라.",
        "이번만은 용서해 주지. 좀더 힘을 길러라.",
    ];

    /// <summary>
    /// 이긴 뒤 — <b>처형한다 · 놓아 준다 · 모두 뺏는다</b>(<c>0x004A8380(3)</c>).
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004AA2D2</c> ~ <c>0x004AA588</c> 이다. 진 사람이 고른 것에 따라 한 마디 한다.
    /// <code>
    ///   처형한다    「죽어야 하나...? 내가...」 다섯(0x005347F8~) → 그 인물이 사라진다(0x00432180(0))
    ///   놓아 준다   「제길, 기억해 두어라.」 다섯(0x005348A8~)   → 「명성이 10 올라갔다」
    ///   모두 뺏는다 「이런 야비한 녀석.」 다섯(0x00534978~)     → 「악명이 100 올라갔다」
    ///                                                            「금화 %d닢을 손에 넣었다」 20 + rand(11)
    ///                                                            「상대는 %s 장비하고 있다」 → 그 무기·방어구를 얻는다
    /// </code>
    /// 상대가 지닌 무기·방어구(<c>[결투+0x198]</c> · <c>+0x19C</c>)는 인물 표에 적혀 있는 것이
    /// 아니라 복장 갈래와 무력으로 그 자리에서 굴린다(<see cref="Engine.Town.Duel.GearOf"/>).
    /// 창을 물리면 놓아 준 것으로 친다.
    /// </remarks>
    /// <summary>
    /// 일기토에 진 사람이 물러가며 한 마디 하고 <b>제 길이 늦어진다</b>(<c>0x004A49F1</c>) — 결판(0·1)이
    /// 난 뒤다. 말은 다섯 가운데 rand(5), 늦어짐은 <c>(rand(3) x 3 + 3) x 4</c> 날(<c>0x004322A0</c>)이고
    /// 「%s의 행동이 늦어졌습니다」를 알린 뒤 악명이 조용히 100 오른다(<c>0x004697C0(1, 100)</c>).
    /// 늦어짐이 실제로 먹는 것은 행적을 되짚는 누적 캐릭터뿐이다(<see cref="Engine.AccReplay.Delay"/>).
    /// </summary>
    private void Setback(TavernRoster.Person who, uint[]? face, GameRandom dice)
    {
        TalkDialog.Say(_view, face, "", Retreat[dice.Next(Retreat.Length)]);
        _game.World?.Replay?.Delay(who.Index, _game.Random);
        NoticeDialog.Show(_view, Engine.AccReplay.Delayed(who.Name));
        _player.Infamy = Math.Min(Engine.Sea.FleetRaid.MaxRenown, _player.Infamy + Engine.AccReplay.DelayInfamy);
    }

    /// <summary>일기토에 진 사람이 물러가며 하는 말 다섯(<c>0x00551578</c>~).</summary>
    internal static readonly string[] Retreat =
    [
        "윽, 제법이군. 자, 다시 만나세.",
        "상처가 깊은 것 같군. 이러면 계획이 늦어져 버릴텐데. 앞으로 조금인데.",
        "모처럼 단서를 잡았는데, 이런 일이... 운이 나쁘군.",
        "너의 이름은... 그래, 그 이름 기억해 두지. 그럼.",
        "생각보단 꽤 하는군. 하지만 이런 곳에서 죽을 수 있나. 내게는 큰 꿈이 있다.",
    ];

    /// <summary>
    /// 이긴 뒤 처형·놓아 준다·모두 뺏는다(<c>0x004A8380(3)</c>). 고른 번호(0 처형 · 1 놓아 줌 ·
    /// 2 뺏음)를 낸다.
    /// </summary>
    /// <param name="indoors">
    /// 판 무대가 <b>4 이상</b>(술집·모스크·사원)인지. 그때만 「놓아 준다」·「모두 뺏는다」가
    /// 붙고, 갑판(0)·초원(1) 같은 데서는 「처형한다」 한 줄뿐이다(<c>0x004A8470</c>).
    /// </param>
    /// <param name="gear">
    /// 판이 열릴 때 굴려 둔 상대의 무기·방어구 번호 — 「모두 뺏는다」가 그것을 준다
    /// (<c>0x004AA4B3</c>). 굴린 것이 없으면 (0, 0) 이고 그때는 안 준다.
    /// </param>
    internal int Triumph(int person, uint[]? face, GameRandom dice, bool indoors = true,
                         (int Weapon, int Armor) gear = default, bool mateFought = false)
    {
        int pick = ChoiceDialog.Pick(_view, "",
            indoors ? ["처형한다", "놓아 준다", "모두 뺏는다"] : ["처형한다"]);
        switch (pick)
        {
            case 0:
                TalkDialog.Say(_view, face, "", Executed[dice.Next(Executed.Length)]);
                if (_game.World?.People.FirstOrDefault(r => r.Id == person) is { } row)
                    row.Appear = 0;
                break;

            case 2:
                TalkDialog.Say(_view, face, "", Robbed[dice.Next(Robbed.Length)]);
                if (gear.Weapon != 0) Loot(gear.Weapon, gear.Armor);
                // 실제로 오르는 것은 10 인데 알림만 100 이라고 찍는다 — 원본이 그렇다
                // (0x004AA467 의 0x004800E0(1, 10) 뒤 0x004AA470 의 0x64).
                _player.Infamy += RobInfamy;
                NoticeDialog.Show(_view, $"악명이 {RobInfamyShown} 올라갔다", "일기토");
                int gold = dice.Next(RobGoldRoll) + RobGoldBase;
                _player.Earn(gold);
                NoticeDialog.Show(_view, $"금화 {gold}닢을 손에 넣었다", "일기토");
                break;

            default:
                TalkDialog.Say(_view, face, "", Beaten[dice.Next(Beaten.Length)]);
                _player.Fame += SpareFame;
                NoticeDialog.Show(_view, $"명성이 {SpareFame} 올라갔다", "일기토");
                break;
        }

        GrowMight(_view, _player, mateFought, dice);
        return pick;
    }

    /// <summary>
    /// 이긴 뒤 <b>1/100</b> 으로 무력이 오른다(<c>0x004AA592</c>) — 승리 차림표가 뜬 판에서만이다.
    /// </summary>
    internal static void GrowMight(Window view, Player player, bool mateFought, GameRandom dice)
    {
        string first = player.MateAt(0);
        var mate = first.Length > 0 ? player.MateInfoOf(first) : null;
        var (by, mine, theirs) = Engine.Town.Duel.MightGrowth(
            mateFought, player.AbilityOf(Ability.Might), mate?.Might, dice);
        if (by <= 0) return;

        if (mine) player.AdjustAbility(Ability.Might, by);
        if (theirs && mate is { } who) player.GrowMate(who.Name, by);

        // 0x00560258 · 0x00560280 · 0x005602A8 — 제독만 · 부관만 · 둘 다.
        string me = player.Name;
        NoticeDialog.Show(view, (mine, theirs) switch
        {
            (true, true) => $"{me}, 부관의 무력이 {by} 상승했다!",
            (true, false) => $"{me}의 무력이 {by} 상승했다!",
            _ => $"부관의 무력이 {by} 상승했다!",
        }, "성장");
    }

    /// <summary>놓아 주면 오르는 명성(<c>0x004AA3E0</c>) · 뺏으면 오르는 악명(<c>0x004AA470</c> 알림 값).</summary>
    /// <summary>
    /// 놓아 주면 명성 +10(<c>0x004AA3DB</c>), 모두 뺏으면 악명 <b>+10</b>(<c>0x004AA467</c>).
    /// 악명 알림만 100 이라고 찍는다(<c>0x004AA470</c>) — 원본이 스스로 어긋나 있다.
    /// </summary>
    internal const int SpareFame = 10, RobInfamy = 10, RobInfamyShown = 100;

    /// <summary>뺏는 금화 — <c>rand(11) + 20</c>(<c>0x004AA486</c>).</summary>
    /// <remarks>
    /// 금화에 이어 <b>상대의 무기·방어구</b>도 뺏는다 — 인물 표에 소지품 칸은 없지만 그 둘은
    /// 판이 열릴 때 복장 갈래와 무력으로 굴려 두므로(<see cref="Engine.Town.Duel.GearOf"/>)
    /// 그것을 그대로 준다(<see cref="Loot"/>, <c>0x004AA4B3</c>).
    /// </remarks>
    internal const int RobGoldRoll = 11, RobGoldBase = 20;

    /// <summary>처형당하기 전에 하는 말(<c>0x005347F8</c> 다섯).</summary>
    internal static readonly string[] Executed =
    [
        "죽어야 하나...? 내가...",
        "이자, 너무 강하다...",
        "자. 잠깐 기다려라, 아직 각오가... 으윽.",
        "너에게 진 것이라면 후회는 없다. 자, 죽여라.",
        "내 인생도 끝인가... 분하다!",
    ];

    /// <summary>다 뺏길 때 하는 말(<c>0x00534978</c> 벌 — 끝의 둘이 같은 줄이다).</summary>
    internal static readonly string[] Robbed =
    [
        "이런 야비한 녀석.",
        "무일푼이냐...",
        "기다려라, 이것은 중요한 물건이다.",
        "기억해 두어라, 비겁한 녀석!",
        "기억해 두어라, 비겁한 녀석!",
    ];

    /// <summary>
    /// 이긴 상대의 <b>무기와 방어구를 뺏는다</b>(<c>0x004AA4B3</c>).
    /// </summary>
    /// <remarks>
    /// 무엇을 지녔는지는 <see cref="Engine.Town.Duel.GearOf"/> 가 정한다 — 인물 표에 적혀 있는
    /// 것이 아니라 복장 갈래와 무력으로 그 자리에서 굴린다. 소지품 칸이 다 차면 못 받는다.
    /// <code>
    ///   0x00534A48  "상대는 %s%s %s%s 장비하고 있다"   ; 방어구가 있을 때
    ///   0x00534A70  "상대는 %s%s 장비하고 있다"        ; 무기뿐일 때
    /// </code>
    /// </remarks>
    private void Loot(int weapon, int armor)
    {
        string Name(int id) => _game.Items?.Find(id)?.Name ?? "";

        string w = Name(weapon), a = Name(armor);
        if (w.Length == 0) return;

        NoticeDialog.Show(_view, a.Length > 0
            ? $"상대는 {w}{NameToken.Of(w, 3)} {a}{NameToken.Of(a, 2)} 장비하고 있다"
            : $"상대는 {w}{NameToken.Of(w, 2)} 장비하고 있다", "일기토");

        foreach (int id in a.Length > 0 ? new[] { weapon, armor } : [weapon])
        {
            if (_player.IsBagFull)
            {
                NoticeDialog.Show(_view, "더 이상 가질 수 없습니다! 소지품을 삭제해 주십시오", "일기토");
                return;
            }
            _player.Take(id);
        }
    }

    /// <summary>내 몫 — 능력치와 검술, 그리고 지닌 무기·방어구 가운데 가장 센 것.</summary>
    private Engine.Town.Duel.Fighter Mine() =>
        new(_player.Name.Length > 0 ? _player.Name : "제독",
            _player.AbilityOf(Ability.Body),
            _player.AbilityOf(Ability.Might),
            _player.LevelOf(Skill.Names[Skill.Sword]),
            _player.AbilityOf(Ability.Luck),
            Best(Engine.Town.Duel.WeaponCategory),
            Best(Engine.Town.Duel.ArmorCategory));

    /// <summary>상대 몫. 세이브에 적힌 능력치와 검술을 그대로 쓴다.</summary>
    private Engine.Town.Duel.Fighter Theirs(in TavernRoster.Person who, GameRandom dice)
    {
        var (weapon, armor) = Engine.Town.Duel.GearFor(FoeSet(who.Index), who.Might, dice, EffectOf);
        return new(who.Name, who.Body, who.Might, who.Sword, who.Luck, weapon, armor);
    }

    /// <summary>아이템 번호의 효과(표 <c>+0x10</c>). 표를 못 읽으면 0.</summary>
    private int EffectOf(int item) => _game.Items?.Find(item)?.Effect ?? 0;

    /// <summary>
    /// 상대의 복장 갈래 — <b>그 사람 나라의 수도 문화권</b>으로 고른다(<c>0x004A88EA</c>).
    /// 나라를 모르면 유럽(1)이다.
    /// </summary>
    private int FoeSet(int person)
    {
        if (_game.PersonTemplates?.Find(person) is not { } who) return 1;
        if (_game.Nations?.Find(who.Nation) is not { } nation) return 1;
        return FighterSprites.SetForCulture(_game.CityRows?.CultureOf(nation.Capital) ?? 0);
    }

    /// <summary>이디스의 방패를 지녔는가 — 스친 것이 막은 것이 된다.</summary>
    private bool Shielded() => _player.Items.Contains(Engine.Town.Duel.EdithShieldId);

    /// <summary>지닌 것 가운데 그 갈래에서 가장 센 효과. 표를 못 읽었으면 0.</summary>
    private int Best(int category)
    {
        if (_game.Items is not { } table) return 0;
        int best = 0;
        foreach (int id in _player.Items)
            if (table.Find(id) is { } item && item.Category == category && item.Effect > best)
                best = item.Effect;
        return best;
    }

    // ── 부하 고용 ─────────────────────────────────────────────────────────────
    //
    // 볼트 87.분석-부하 고용(결정 조건과 거절 말). 무작위는 없다.

    /// <summary>부관 자리.</summary>
    private const int FirstMateSlot = 0;

    /// <summary>통역 자리.</summary>
    private const int InterpreterSlot = 3;

    /// <summary>부관·통역 자리에 앉히려면 넘어야 하는 말 수준(<c>0x00453580</c>).</summary>
    private const int FluentTongue = 3;

    /// <summary>명성 셈에서 매력이 이 값을 넘는 만큼 보탠다(<c>0x00453600</c>).</summary>
    private const int CharmFloor = 69;

    /// <summary>명성이 모자랄 때 대신 내밀 수 있는 소지품.</summary>
    private const string Dumpling = "수수경단";

    /// <summary>
    /// 「부하로 고용한다」. 인물 판에서 <b>결정</b>하고 자리·말 검사를 넘어 판정까지 갔으면 true —
    /// 게임은 그때 붙든 못 붙든 차림표를 닫는다(<c>0x004A4BB0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   ① 빈 자리가 없다     「부하는 동시에 4명밖에 고용할 수 없습니다」
    ///   ② 빈 자리가 부관·통역뿐인데 제독과 말이 3 미만
    ///                        「지금 고용할 수 있는 것은 완전히 언어가 통하는 사람 뿐입니다」
    ///   ③ 명성  (max(0, 매력-69) + 100) x 제독 명성 &gt; 인물 명성 x 100
    ///      이기면 계약금 = 밑값 x (10 - 웅변) / 3 을 부르고, 지면 수수경단을 묻는다
    /// </code>
    /// 계약금으로 붙어도 「고용했다」는 말이 없다 — 곧장 일을 정한다.
    /// </remarks>
    private bool Hire(TavernRoster.Person who, uint[]? face)
    {
        var row = RowOf(who.Index);
        if (!PersonInfoDialog.AskHire(_view, SheetOf(who), face)) return false;

        if (!HasOpenSlot(row, anySlot: true))
        {
            NoticeDialog.Show(_view, "부하는 동시에 4명밖에 고용할 수 없습니다");
            return false;
        }
        if (!HasOpenSlot(row, anySlot: false))
        {
            NoticeDialog.Show(_view, "지금 고용할 수 있는 것은 완전히 언어가 통하는 사람 뿐입니다");
            return false;
        }

        int charm = Math.Max(0, _player.AbilityOf(Ability.Charm) - CharmFloor) + 100;
        bool famous = (long)charm * _player.Fame > (long)who.Fame * 100;

        // 판정 결과를 <b>설득 애니메이션(5번)</b>으로 보인다 — 무릎 꿇고 청하다가 이기면
        // 받아들여지고 지면 엎어진다(0x00453600 → 0x004A63A0). 후원자 설득과 같은 연출이다.
        (_view as CityPicView)?.PlayFameCheck(famous);

        int fee = 0;
        bool hired = false;
        if (famous)
        {
            int eloquence = _player.LevelOf(Skill.Names[Skill.Rhetoric]);
            fee = Math.Max(0, (row?.Fee ?? 0) * (10 - eloquence) / 3);
            if (ConfirmDialog.Ask(_view,
                    $"선금으로 금화 {fee}닢이라면, 그 이야기 들어줄 수도 있지. 어떤가?", null, face))
            {
                if (_player.Gold >= fee) hired = true;
                else NoticeDialog.Show(_view, "계약금이 너무 비쌉니다!");
            }
            // NO 면 아무 말 없이 끝난다.
        }
        else
        {
            if (_game.Items?.Find(Dumpling) is { } dumpling && _player.HasItem(dumpling.Id)
                && ConfirmDialog.Ask(_view, $"{Dumpling}{Object(Dumpling)} 주시겠습니까?"))
            {
                TalkDialog.Say(_view, face, "",
                    "이런것에 마음이 변하리라 생각했나? ~우물우물~! 어디까지나 제독을 따라가겠습니다! 맡겨 주십시오.");
                _player.Drop(dumpling.Id);
                hired = true;
            }
            if (!hired)
                TalkDialog.Say(_view, face, "", "시시한 배를 탈 정도로 바보는 아니네.");
        }

        if (hired)
        {
            _player.Spend(fee);
            // 됨됨이를 지금 베껴 둔다 — 나중에 인물정보를 낼 때 게임 세이브를 다시 안 뒤지게.
            _player.RememberMate(Tavern.MateInfoOf(who));
            PlaceMate(who.Name);
            // 부하가 되면 술집 자리에서 빠진다(Sitting) — 사진 앞 손님도 다시 세운다.
            (_view as CityPicView)?.RefreshPhoto();
        }
        return true;
    }

    /// <summary>
    /// 새 부하의 일을 정한다(<c>0x00453764</c>). 찬 자리를 고르면 바꿀지 묻고, 밀려난 사람의
    /// 일을 다시 정한다 — 빈 자리가 하나는 있으니 사슬은 거기서 끝난다.
    /// </summary>
    private void PlaceMate(string name)
    {
        string? next = name;
        while (next != null)
        {
            string placing = next;
            NoticeDialog.Show(_view, $"{placing}의 일을 정해 주십시오");

            var row = RowOf(placing);
            var rows = new List<(string Text, bool On)>();
            for (int i = 0; i < Player.MaxMates; i++)
            {
                string role = Player.MateRoles[i], sitting = _player.MateAt(i);
                // <b>줄은 다 살아 있다</b> — 게임도 고르게 두고 나서 물린다(0x00453F8E).
                rows.Add((sitting.Length > 0 ? $"{role} ({sitting})" : role, true));
            }

            int slot = ChoiceDialog.Pick(_view, "", rows, exitRow: false);
            if (slot < 0 || slot >= Player.MaxMates) continue;    // 물릴 수 없다 — 다시 묻는다

            // 부관·통역은 <b>제독과 말이 3 이상</b>이라야 앉는다(0x00453F86).
            // 안 되면 한 줄 내고 고르기로 되돌아간다.
            if (!CanSit(row, slot))
            {
                GameDialog.Show(_view, slot == FirstMateSlot
                    ? "말이 통하지 않는 자는 부관이 될 수 없습니다!"
                    : "말이 통하지 않는 자는 통역이 될 수 없습니다!");
                continue;
            }

            string old = _player.MateAt(slot);
            if (old.Length > 0)
            {
                string role = Player.MateRoles[slot];
                if (!ConfirmDialog.Ask(_view,
                        $"현재의 {role}{Topic(role)} {old}입니다. {placing}에 변경하시겠습니까?"))
                    continue;
            }
            _player.SetMate(slot, placing);
            next = old.Length > 0 ? old : null;
        }
    }

    /// <summary>
    /// 들어올 자리가 있는지(<c>0x00453580</c>). <paramref name="anySlot"/> 이면 빈 자리만 보고,
    /// 아니면 부관·통역 자리는 제독과 말이 3 이상이어야 친다.
    /// </summary>
    private bool HasOpenSlot(PersonTable.Row? row, bool anySlot)
    {
        for (int i = 0; i < Player.MaxMates; i++)
        {
            if (_player.MateAt(i).Length > 0) continue;
            if (anySlot || CanSit(row, i)) return true;
        }
        return false;
    }

    /// <summary>그 사람이 그 자리에 앉을 수 있는지(<c>0x00453530</c>). 항해사·측량사는 늘 된다.</summary>
    private bool CanSit(PersonTable.Row? row, int slot) =>
        slot is not (FirstMateSlot or InterpreterSlot) || PlayerTongue(row) >= FluentTongue;

    /// <summary>
    /// 그 사람과 통하는 말 — 제독·부관·통역 가운데 가장 잘 통하는 사람의 수준(<c>0x00468F70</c>).
    /// 인물 표에 없으면 막지 않는다.
    /// </summary>
    private int TongueWith(int person)
    {
        if (RowOf(person) is not { } row) return FluentTongue;
        int best = PlayerTongue(row);
        foreach (int slot in (int[])[FirstMateSlot, InterpreterSlot])
            if (RowOf(_player.MateAt(slot)) is { } mate)
                best = Math.Max(best, Shared(mate.Languages, i => row.Languages[i]));
        return best;
    }

    /// <summary>제독과 그 사람이 함께 잘하는 말의 수준. 표에 없으면 막지 않는다.</summary>
    private int PlayerTongue(PersonTable.Row? row) =>
        row == null ? FluentTongue
                    : Shared(row.Languages, i => _player.TongueOf(Skill.Languages[i]));

    /// <summary>언어 열넷 중 <c>max(min(갑, 을))</c> — 게임의 <c>0x00478050</c> 이다.</summary>
    private static int Shared(IReadOnlyList<int> theirs, Func<int, int> ours)
    {
        int best = 0;
        for (int i = 0; i < Math.Min(theirs.Count, Skill.Languages.Length); i++)
            best = Math.Max(best, Math.Min(theirs[i], ours(i)));
        return best;
    }

    /// <summary>인물 표의 그 줄. 없으면 null.</summary>
    private PersonTable.Row? RowOf(int id) =>
        _game.World?.People.FirstOrDefault(r => r.Id == id);

    /// <summary>이름으로 찾은 인물 표의 줄. 없으면 null.</summary>
    private PersonTable.Row? RowOf(string name) =>
        name.Length == 0 ? null : _game.World?.People.FirstOrDefault(r => r.Name == name);

    /// <summary>술집 인물 판에 적을 것. 직업·나라는 인물 밑표에서 온다.</summary>
    private PersonInfoDialog.HireSheet SheetOf(in TavernRoster.Person who)
    {
        // 별자리·혈액형도 밑표에서 온다 — 인물에게는 생일 칸이 없다(볼트 87 §10).
        if (_game.PersonTemplates?.Find(who.Index) is not { } t)
            return new(who.Name, who.Body, who.Mind, who.Might, who.Charm, who.Age, "", "", "", "");

        string job = t.JobName.Length > 0 ? t.JobName : Job.Of(t.Job).Name;
        string nation = _game.Nations?.Find(t.Nation) is { } nat ? nat.Name : "";
        return new(who.Name, who.Body, who.Mind, who.Might, who.Charm, who.Age,
                   job, t.Zodiac, t.BloodName, nation);
    }

    /// <summary>이름 뒤에 붙는 목적격 조사. 받침이 있으면 "을", 없으면 "를".</summary>
    private static string Object(string name) => HasBatchim(name) ? "을" : "를";

    /// <summary>이름 뒤에 붙는 보조사. 받침이 있으면 "은", 없으면 "는".</summary>
    private static string Topic(string name) => HasBatchim(name) ? "은" : "는";

    private static bool HasBatchim(string name) =>
        name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 != 0;

    /// <summary>이름 뒤에 붙는 주격 조사. 받침이 있으면 "이", 없으면 "가".</summary>
    /// <remarks>
    /// 게임도 조사를 따로 끼워 넣는다 — "%s%s 있다"(<c>0x0054ABF0</c>) 의 두 번째 자리다.
    /// </remarks>
    private static string Subject(string name)
    {
        if (name.Length == 0) return "가";
        char last = name[^1];
        if (last is < '가' or > '힣') return "가";       // 한글이 아니면 그냥 둔다
        return (last - '가') % 28 == 0 ? "가" : "이";
    }

    /// <summary>
    /// 그 사람의 얼굴. 세이브의 얼굴코드가 <c>0xFFFF</c> 면 얼굴이 없다는 뜻이라 null 이다.
    /// </summary>
    private uint[]? FaceOf(TavernRoster.Person who) =>
        who.FaceCode is >= 0 and < 0xFFFF
            ? _game.Faces?.TryGetBgra(who.FaceCode, female: false)
            : null;

    /// <summary>한잔 산다. 정말 샀으면 true — 낯을 트는 것은 부르는 쪽이 판단한다.</summary>
    /// <remarks>
    /// <b>샀다고 알리지 않는다.</b> 게임에는 그런 문구가 없다 — 값만 빠지고 곧바로
    /// 상대가 말을 잇는다. 돈이 모자랄 때 물리는 "돈 먼저 지불하게."(<c>0x0054AC98</c>)만
    /// 게임 것이다.
    /// </remarks>
    /// <summary>
    /// 이 방문에 술을 한잔이라도 샀는지. 게임은 술집 화면 객체의 <c>+0xB4</c> 에 적는다.
    /// </summary>
    /// <remarks>「정보를 듣는다」가 이것을 본다 — 마시기 전에는 입을 안 연다.</remarks>
    private bool _drank;

    /// <summary>
    /// 술집의 <b>정보를 듣는다</b>(<c>0x0042F8E0</c>) — 주인이 계약 목표에 얽힌 소문을 들려준다.
    /// </summary>
    /// <remarks>
    /// 들려줄 소문은 들어설 때와 술을 시킬 때마다 새로 고른다(<see cref="PickRumor"/>). 말은 모두 술집 주인 얼굴이다.
    /// <code>
    ///   술을 안 샀으면   "%s? 으~ ~음.....! … 마시면 가르쳐 주지"   0x0054ACB0 (%s = 계약 힌트 이름)
    ///                    소문의 목표가 계약 목표와 다르면 "자, 우리 가게 술을 마신다면 가르쳐 주지." 0x0054AD10
    ///   말할 도시를 고른다(0x0042F790) — 못 고르면 "정보? 그런 것은 없네"           0x0054ADB8
    ///   여기면            소문 본문을 그대로 읽어 준다                             0x004B0990
    ///   같은 문화권       "확실히 %s에서 비슷한 소문을 들은 적이 있네."              0x0054AD40
    ///   아니면            "%s%s 간 선원한테서 그런 이야기를 들은 적이 있네."          0x0054AD70
    /// </code>
    /// 방향 이름은 문화권 표(<c>0x00560BE8</c>)의 열하나인데 <b>아메리카만 「신대륙」</b>으로
    /// 바꿔 부른다(<c>0x0042FA6C</c> 가 10 을 따로 가린다). 답한 뒤에도 아무것도 지우지 않는다.
    ///
    /// 원본은 주인의 성미 여덟째 칸이 1 이상이면 술을 안 사도 말해 주는데(<c>0x0042F8F8</c>), 주인
    /// 성미를 셀 값(화자 객체 <c>+0x18</c>)을 아직 못 밝혀 옮기지 않았다.
    /// </remarks>
    public void HearInfo() => Alone(() =>
    {
        var face = _game.SpeakerFace(BuildingCode, _cultureNo);
        // 정보도 그 도시 나라의 말로 들린다 — 아랍어 1레벨이면 주인 대사도
        // 손님 소문과 같은 확률로 뭉개야 한다(0x004780E0 → 0x004252F0).
        int language = _game.Nations?.Find(_game.CityRows?.NationOf(_cityId) ?? -1)?.Language ?? -1;
        int level = language >= 0 && language < Skill.Languages.Length
            ? _player.TongueOf(Skill.Languages[language]) : Skill.MaxLevel;
        void Say(string words) => TalkDialog.Say(_view, face, "",
                                                 StrangerTalk.Garble(words, level, _game.Random));

        if (_rumor < 0 || _game.Rumors?.Rumors is not { } rumors || _rumor >= rumors.Count) return;
        var rumor = rumors[_rumor];

        if (!_drank)
        {
            var hint = _player.Contract is { } deal ? _game.Hints?.Find(deal.Hint) : null;
            Say(hint is { } h && h.Discovery == rumor.Discovery
                ? $"{h.Name}? 으~ ~음.....! 그러고 보니 들은 적이 있는 것 같군! "
                  + "자, 우리 가게 술을 마시면 가르쳐 주지."
                : "자, 우리 가게 술을 마신다면 가르쳐 주지.");
            return;
        }

        if (RumorCity(rumor) is not int target)
        {
            Say("정보? 그런 것은 없네");
            return;
        }

        if (target == _cityId) { Say(rumor.Text); return; }

        int mine = _game.CityRows?.CultureOf(_cityId) ?? -1;
        int there = _game.CityRows?.CultureOf(target) ?? -2;
        if (mine == there)
        {
            Say($"확실히 {_game.CityName(target)}에서 비슷한 소문을 들은 적이 있네.");
            return;
        }

        string way = Bearing(there);
        Say($"{way}{GameUi.Josa(way, "으로", "로")} 간 선원한테서 그런 이야기를 들은 적이 있네.");
    });

    /// <summary>들려줄 소문 줄(<c>[+0xB8]</c>). 없으면 −1 이고 「정보를 듣는다」 줄이 안 선다.</summary>
    private int _rumor = -1;

    /// <summary>말할 도시를 굴릴 씨(<c>[+0xBC]</c>) — 한 잔 사이에는 같은 도시를 댄다.</summary>
    private int _rumorSeed;

    /// <summary>「정보를 듣는다」 줄을 세울지 — 들려줄 소문이 골라졌는지(<c>0x0042FE5C</c>).</summary>
    public bool HasRumor => Candidates(_cityId).Count > 0 || Candidates(-1).Count > 0;

    /// <summary>
    /// 들려줄 소문을 고른다(<c>0x0042E750</c> → <c>0x0042E780</c>) — 들어설 때와 술을 시킬 때마다다.
    /// </summary>
    /// <remarks>
    /// 계약 힌트의 목표 발견물과 같은 소문 가운데 <b>이 도시가 칸에 든 것</b>을 먼저 모으고, 없으면 도시 칸이
    /// 하나라도 선 것을 모아 무작위로 하나 고른다. 칸마다 한 번씩 담기므로 이 도시가 여러 칸에 든 소문일수록
    /// 잘 뽑힌다(<c>0x00414390</c>). 계약이 없으면 −1 이다.
    /// </remarks>
    private void PickRumor()
    {
        var here = Candidates(_cityId);
        var pool = here.Count > 0 ? here : Candidates(-1);
        _rumor = pool.Count > 0 ? pool[_game.Random.Next(pool.Count)] : -1;
        _rumorSeed = _game.Random.Next();
    }

    /// <summary>
    /// 계약 목표와 같은 소문의 줄 번호를 도시 칸마다 모은다(<c>0x00414390</c>). <paramref name="city"/> 가 −1 이면
    /// 칸의 도시가 무엇이든 담는다. 빈 칸 · 아직 안 선 도시는 뺀다(도시 형편 비트 2, <c>+4 &amp; 4</c>).
    /// </summary>
    private List<int> Candidates(int city)
    {
        var got = new List<int>();
        if (_player.Contract is not { } deal || _game.Hints?.Find(deal.Hint) is not { } hint
            || _game.Rumors?.Rumors is not { } rumors) return got;

        for (int i = 0; i < rumors.Count; i++)
        {
            if (rumors[i].Discovery != hint.Discovery) continue;
            foreach (int c in rumors[i].Cities)
                if (c >= 0 && _game.CityStanding(c) && (city < 0 || c == city)) got.Add(i);
        }
        return got;
    }

    /// <summary>
    /// 소문을 말할 도시(<c>0x0042F790</c>) — 소문의 도시 칸에서 <b>이 도시</b>, 없으면 <b>같은 문화권</b>,
    /// 없으면 <b>아무 도시</b> 차례로 찾아 무작위로 하나 댄다. 굴림은 술을 시킬 때 적어 둔 씨로 하므로
    /// 한 잔 사이에는 몇 번을 물어도 같은 도시다. 없으면 null.
    /// </summary>
    private int? RumorCity(RumorTable.Rumor rumor)
    {
        int culture = _game.CityRows?.CultureOf(_cityId) ?? -1;
        var dice = new Random(_rumorSeed);
        for (int mode = 0; mode < 3; mode++)
        {
            var got = rumor.Cities.Where(c => c >= 0 && _game.CityStanding(c)
                && mode switch
                {
                    0 => c == _cityId,
                    1 => (_game.CityRows?.CultureOf(c) ?? -2) == culture,
                    _ => true,
                }).ToList();
            if (got.Count > 0) return got[dice.Next(got.Count)];
        }
        return null;
    }

    /// <summary>
    /// 문화권을 부르는 이름(<c>0x00560BE8</c>). 아메리카(10)만 「신대륙」이다.
    /// </summary>
    private static readonly string[] Bearings =
    [
        "이베리아", "북유럽", "지중해", "아프리카", "중근동", "인도",
        "중국", "중앙아시아", "동남아시아", "일본", "신대륙",
    ];

    private static string Bearing(int culture) => BearingName(culture);

    /// <summary>
    /// 그 문화권을 술집이 부르는 이름. 표 밖이면 「먼 바다」다.
    /// </summary>
    /// <remarks>힌트 편집기가 같은 말을 미리 내 보이려고 함께 쓴다.</remarks>
    public static string BearingName(int culture) =>
        culture >= 0 && culture < Bearings.Length ? Bearings[culture] : "먼 바다";

    /// <summary>「포카를 권한다」 — 술집 주인과 카드 도박을 한다(<see cref="PokerDialog.Play"/>).</summary>
    public void PlayPoker() => Alone(() => PokerDialog.Play(_view, _game, _cultureNo));

    public bool BuyDrink()
    {
        if (_player.Gold < Tavern.DrinkPrice)
        {
            // 한잔 사 주는 자리는 말이 다르다(0x0042F2A6) — 「돈 먼저 지불하게.」(0x0054AC98)는
            // 제 술을 시킬 때의 말이다(0x0042F638).
            ConfirmDialog.Tell(_view, "공짜로 마시게 할 술은 없다!", face: HostFace());
            return false;
        }
        _player.SetGold(_player.Gold - Tavern.DrinkPrice);
        _drank = true;
        return true;
    }

}
