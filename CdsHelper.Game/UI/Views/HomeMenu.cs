using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택 — 휴양과 저금.
/// </summary>
/// <remarks>
/// 달수와 지문은 <see cref="Home"/> 가 알고, 여기서는 묻고 알리는 차례만 맡는다.
/// 게임의 휴양 창이 <c>0x00460660</c>, 저금 창이 <c>0x004609C0</c> 이다.
///
/// 보관(<c>StorageDialog</c>)과 기능(<see cref="GameSystemMenu"/>)은 자택 줄이지만
/// 여기 없다 — 보관은 창 하나로 끝나고, 기능은 도시 일이 아니라 판 일이다.
/// </remarks>
/// <param name="view">이 자택을 낸 도시 창. 물음창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위가 여기서 온다.</param>
/// <param name="menu">자택 명령 창. 휴양·저금 창을 그 위에 쌓는다.</param>
internal sealed class HomeMenu(Window view, Engine.Game game, GameMenuHost menu)
{
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly GameMenuHost _menu = menu;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

    // ── 자택에 들면 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 자택에 들면 먼저 하는 일 — 아직 안 알린 아이를 소개하고, 맏딸이 나이 차고
    /// 저금이 넉넉하면 결혼 이야기를 듣는다. 게임의 <c>0x0045FFC0</c> 이다.
    /// </summary>
    /// <remarks>
    /// <see cref="CityPicView"/> 가 건물에 들 때마다(<c>Greet</c>) 자택이면 이것을 부른다.
    /// <b>다섯 살 이하</b>인 아이를 처음 소개할 때는 사건 그림 8 을 세우고 말한다(<c>0x0045FFEB</c> —
    /// <c>0x00472FA0(8)</c> 로 그림을 올리고 소개가 끝나면 <c>0x00473160</c> 으로 내린다). 여섯 살부터는 말만 한다.
    /// </remarks>
    public void Greet()
    {
        var owner = Owner;

        // 아직 소개 안 한 아이가 있으면 그것으로 끝난다(0x0045FFC0 이 참이면 뒤가 다 건너뛴다).
        // 한 번에 <b>하나만</b> 소개한다 — 원본은 0x004AB980 으로 아직 안 알린 아이 가운데
        // <b>가장 어린</b> 하나만 집어 0x00460070 을 한 번 부른다(0x0045FFC6).
        // NotIntroduced 가 태어난 차례(오름차순)라 마지막이 가장 어리다.
        if (Home.NotIntroduced(_player) is [.., var newborn])
        {
            Introduce(owner, newborn);
            return;
        }

        // 그 다음이 사건이다 — 사건이 나면 맞이하는 말은 안 한다(0x004605C0).
        if (Incident(owner)) return;

        // 아내와 아이가 맞는다(0x004144C0 · 0x00414670).
        if (_player.Spouse.Length > 0)
            TalkDialog.Say(owner, null, _player.Spouse,
                           Home.WifeWelcome[_random.Next(Home.WifeWelcome.Length)]);

        // 아이는 <b>하나만</b> 인사한다 — 다섯~열넷 살 가운데 굴려 고른다(0x00414670).
        if (Home.WelcomerOf(_player, _random) is { } welcomer)
            TalkDialog.Say(owner, ChildFace(welcomer), welcomer.Name,
                           Home.WelcomeOf(welcomer.Daughter, welcomer.AgeOn(_player.Date), _random));
    }

    /// <summary>
    /// 집에 돌아왔을 때 나는 사건 — 딸의 결혼이 먼저고, 그 다음이 아내의 돈벌이다(<c>0x004605C0</c>).
    /// </summary>
    /// <remarks>
    /// 딸의 결혼은 <c>rand(5) == 0</c>, 돈벌이는 <c>rand(100) &lt;= 2</c> 다. 어느 쪽이든 하나가
    /// 나면 그 날의 맞이하는 말은 없다.
    /// </remarks>
    /// <returns>사건이 났으면 참.</returns>
    private bool Incident(Window owner)
    {
        if (_random.Next(Home.MarriageRoll) == 0 && Home.MarriageableDaughter(_player) is { } daughter)
        {
            ProposeMarriage(owner, daughter);
            return true;
        }

        if (!Home.IncidentDue(_player, _random)) return false;
        return Home.SellsPhotos(_player.JobIndex) ? SellPhotos(owner) : ShowAnimals(owner);
    }

    /// <summary>
    /// 아내의 성미 칸 <see cref="Home.GreedSlot"/>(칸 6). 여급 표에서 별자리와 혈액형을 꺼내
    /// 센다(<c>0x0047CB70</c>).
    /// </summary>
    /// <remarks>
    /// 아내는 별자리를 <b>생월·생일</b>로 쥐고 있어(<c>0x0047CB50</c>) 얼굴로 지어내지 않는다.
    /// 운명 코드 보정(<c>0x0047D710</c>)은 여자면 <c>clamp(나이/5, 0, 2)</c> 줄인데, 그 줄이
    /// 칸 6 을 건드리면 값이 갈릴 수 있다 — 여기서는 아직 얹지 않는다(불확실).
    /// 아내를 못 찾으면 1 로 둔다.
    /// </remarks>
    private int WifeGreed()
    {
        if (_game.Barmaids?.Find(_player.SpouseId) is not { } her) return 1;
        return Engine.Sea.FleetRaid.FortuneOfZodiac(her.Zodiac, her.Blood)[Home.GreedSlot];
    }

    /// <summary>가진 아이템 가운데 그 분류인 것(<c>0x004AB680</c>).</summary>
    private List<Local.Helpers.ItemTable.Record> Owned(int category)
    {
        var table = _game.Items;
        if (table == null) return [];
        return [.. _player.Items.Select(table.Find)
                               .Where(r => r is { } rec && rec.Category == category)
                               .Select(r => r!.Value)];
    }

    /// <summary>
    /// 잡아 온 동물로 구경거리를 벌였거나, 놓쳐서 소문이 났거나(<c>0x00460280</c>).
    /// </summary>
    private bool ShowAnimals(Window owner)
    {
        var mine = Owned(Home.AnimalCategory);
        if (mine.Count == 0) return false;

        if (Home.ShowsAnimals(WifeGreed(), _player.AbilityOf(Ability.Luck), _random))
        {
            int fee = mine.Sum(r => Home.ShowFee(r.SellList));
            if (fee <= 0) return false;
            TalkDialog.Say(owner, null, _player.Spouse,
                           "돌아오셨어요? 참, 마을 사람들에게 당신이 잡아온 희한한 동물을 보여 주었더니, "
                           + $"관람료로 금화 {fee} 닢이나 모아졌지 뭐예요!");
            _player.SetSavings(_player.Savings + fee);
            return true;
        }

        var ran = mine[_random.Next(mine.Count)];
        TalkDialog.Say(owner, null, _player.Spouse,
                       $"돌아오셨어요? 여보, 큰일 났었어요! 당신이 키우고 있는 {ran.Name}"
                       + $"{Local.Helpers.NameToken.Of(ran.Name, 0)} 도망쳐서 마을이 온통 야단법석이었어요! "
                       + "마을 사람들이 잡아 주었으니 망정이지, 영주님께 혼이났어요.");
        _player.Infamy += Home.RunawayInfamy(ran.SellList);
        return true;
    }

    /// <summary>
    /// 가져온 유물의 사진을 아내가 팔았다(<c>0x00460420</c>).
    /// </summary>
    private bool SellPhotos(Window owner)
    {
        var mine = Owned(Home.RelicCategory);
        if (mine.Count == 0 || !Home.SellsRelicPhotos(WifeGreed())) return false;

        var shot = mine[_random.Next(mine.Count)];
        int paid = Home.PhotoFee(shot.SellList, _player.AbilityOf(Ability.Luck), _random);
        if (paid <= 0) return false;

        TalkDialog.Say(owner, null, _player.Spouse,
                       $"다녀오셨어요? 당신이 집에 없을 때, [{shot.Name}]의 사진을 찍어서 팔았더니, "
                       + $"인기가 좋아서 금화 {paid}닢이나 벌었어요. 놀랐지 뭐예요.");
        _player.SetSavings(_player.Savings + paid);
        return true;
    }

    /// <summary>아이 하나를 소개하고, 바라면 이름을 새로 짓는다(<c>0x00460070</c>).</summary>
    /// <remarks>다섯 살 이하면 사건 그림 8 을 함께 낸다(<c>0x0045FFE6</c> 의 <c>cmp 나이, 5</c>).</remarks>
    private void Introduce(Window owner, Player.Child child)
    {
        // 소개 대사는 <b>아내가 하는 말</b>이다 — 아내가 없으면 통째로 건너뛰고 이름 짓기만
        // 묻는다(0x004600A5 의 je 0x004600F2). 아이 얼굴로 대신 내지 않는다.
        // (원본은 아내가 없어도 다섯 살 이하면 EVSTILL 8 을 뒤에 세워 두지만, 우리 그림은
        //  글 창과 한 벌이라 글이 없으면 그림도 안 띄운다.)
        if (_player.Spouse.Length > 0)
        {
            string words = Home.IntroductionOf(child, _player.Date);
            if (child.AgeOn(_player.Date) <= Home.BabyAge)
                DiscoveryDialog.Show(owner, _game.EventStills, Home.BabyStill, words);
            else TalkDialog.Say(owner, null, _player.Spouse, words);
        }

        var named = child;
        if (ConfirmDialog.Ask(owner, "새로운 이름을 짓겠습니까?"))
        {
            GameDialog.Show(owner, "아이의 이름을 결정해 주십시오!");
            if (TextInputDialog.Ask(owner, child.Name, Home.ChildNameMaxLength,
                                    child.Daughter ? "딸의 이름" : "아들의 이름") is { Length: > 0 } name)
                named = named with { Name = name };
        }
        _player.ReplaceChild(child, named with { Introduced = true });
    }

    /// <summary>
    /// 딸이 결혼해도 되냐고 묻는다(<c>0x00460180</c>). <b>아니오를 골라도 결국 허락한다</b> —
    /// 원본 그대로다.
    /// </summary>
    private void ProposeMarriage(Window owner, Player.Child daughter)
    {
        if (!ConfirmDialog.Ask(owner,
                "아버지, 할 이야기가 있어요. 저 좋아하는 사람이 있는데, 그 사람이 결혼하재요···아버지, 결혼해도 되겠지요?",   // 0x00539488
                face: ChildFace(daughter)))
        {
            TalkDialog.Say(owner, ChildFace(daughter), daughter.Name, "너무 해요! 아버지, 그런 슬픈 말씀 하지 마세요!");
            TalkDialog.Say(owner, null, _player.Spouse, "당신, 딸의 부탁하니, 제발 허락해 주세요.");
        }

        TalkDialog.Say(owner, ChildFace(daughter), daughter.Name, "고마워요, 아버지! 꼭 행복하겠어요.");
        TalkDialog.Say(owner, null, _player.Spouse,
            $"잘 되었구나. 그건 그렇고, 당신 결혼 준비금으로 금화를 {Home.MarriageDowry} 닢 준비해 주세요!");   // 0x00539580

        _player.SetSavings(_player.Savings - Home.MarriageDowry);
        _player.RemoveChild(daughter);
    }

    // ── 후손을 남긴다 ────────────────────────────────────────────────────────

    /// <summary>그 아이의 얼굴 — 나이로 바뀐다(<c>0x0047D710</c>). 얼굴을 안 적던 세이브면 null 이다.</summary>
    private uint[]? ChildFace(Player.Child child)
    {
        int face = Home.FaceOf(child, child.AgeOn(_player.Date));
        return face < 0 ? null : _game.Faces?.TryGetBgra(face, child.Daughter);
    }

    /// <summary>아내가 있어야 눌린다 — 없으면 줄이 흐리다.</summary>
    public bool CanLeaveHeir => Home.CanLeaveHeir(_player);

    /// <summary>
    /// "후손을 남긴다" — 게임의 <c>0x00461330</c> 이다.
    /// </summary>
    /// <remarks>
    /// 차례가 이렇다.
    /// <code>
    ///   461363  아내가 없으면([0x005B61B0] == -1) 아무 일도 없다
    ///   46137c  아내 상태가 2 라야 한다
    ///   46139e  체력([0x005B60D8])이 100 이상이라야 한다
    ///   4613cc  rand(8) &lt; 2 라야 얻는다                      ← 네 번에 한 번
    ///   4613e3  0x004A6340(된 것인가) — MPEFFECT 2번(대포)
    ///   4613fc  됐으면 아이를 만든다
    ///   461401  0x00469850(5) — 닷새가 간다
    /// </code>
    /// 컨디션 관문은 옮겼다 — 100 밑이면 아내가 「안색이 안 좋은데요…」 하고 끝난다. 아내 상태(사람 칸 <c>+0x04</c> 가 2)
    /// 관문은 우리 쪽에 그 칸이 없어 늘 지나간 것으로 둔다. 애니메이션이 대포인 것은 게임 그대로다.
    /// </remarks>
    public void LeaveHeir()
    {
        if (!CanLeaveHeir) return;

        // 몸이 성해야 한다 — 컨디션이 100 밑이면 아내가 말리고 끝난다(0x0046139E).
        if (_player.Condition < Home.HeirCondition)
        {
            TalkDialog.Say(Owner, null, _player.Spouse, Home.HeirTired);
            return;
        }

        bool born = Home.HeirBorn(_player, _random);

        // 애니메이션은 도시 그림 위에서 돈다 — 명령 창이 아니라 그림이 든다.
        (_view as CityPicView)?.PlayHeir(born);

        // 됐든 안 됐든 아내가 한 마디 한다(0x00414B30) — 쉰 살이 넘어야 앞 셋이 나온다.
        TalkDialog.Say(Owner, null, _player.Spouse, Home.WifeWord(_player.Age, _random));

        // 됐는지는 따로 알리지 않는다 — 원본은 애니메이션(0x004A6340)과 아내 말(0x00414B30) 뒤에
        // 됐으면 조용히 아이를 들일 뿐이다(0x004613F4 → 0x00460C50, 말이 없다).
        if (!born) { PassHeirDays(); return; }

        // 아내의 운명 코드와 혈액형이 아이 능력치·혈액형에 든다(0x00461139 · 0x00460FA0).
        var wife = _player.SpouseId >= 0 ? _game.Barmaids?.Find(_player.SpouseId) : null;
        // 성별이 먼저 정해져야 이름을 뽑는다 — 이미 아이가 있으면 그 반대다(0x00460CA1).
        bool daughter = _player.Children.Count > 0 ? !_player.Children[^1].Daughter : _random.Next(2) == 0;
        // 아이가 물려받는 언어는 아버지 것뿐이 아니다 — 제독 나라의 언어와 아내가 가르치는
        // 언어도 3 으로 들어온다(0x00460EB8).
        var child = Home.Conceive(_player, _random, HeirName(daughter),
                                  wife?.Fortune ?? -1, wife?.Blood ?? -1, daughter,
                                  _game.Nations?.Find(_player.Nation)?.Language ?? -1,
                                  wife?.Tongues ?? 0);
        _player.AddChild(child);
        PassHeirDays();
    }

    /// <summary>
    /// 후손 남기기가 잡아먹는 닷새 — 차례가 <b>맨 뒤</b>다(<c>0x004613E3</c>~<c>0x00461408</c>:
    /// 애니메이션 → 아내 말 → 아이 만들기 → <c>0x00469850(5)</c>). 아이를 만든 뒤에 날이 가야
    /// 태어나는 날이 안 밀린다.
    /// </summary>
    private void PassHeirDays() => _player.AdvanceDays(Home.HeirDays);

    // ── 교육 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 「교육」 — 게임의 <c>0x004617D0</c> 이다. 맏아들에게 아버지가 더 잘하는 기능·언어를 한 단계 가르친다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   아들이 없거나 10세 밑이면 아내가 말하고 끝
    ///     아들·딸 둘 다   「%s%s %d세, %s%s %d세에요. %s에게는 교육은 아직 무리에요.」
    ///     아들만          「%s%s 아직 %d세에요. 교육은 아직 일러요」
    ///     딸만            「%s%s 아직 %d세예요. 가정 교육은 나에게 맡겨 주세요.」
    ///   계약 중이면 아내 「여보, 당신 지금 계약중이죠? … 일을 먼저 끝낸 다음에 해 주세요.」
    ///   가르칠 것(아버지 &gt; 아이)이 없으면 「더 이상 가르칠 것이 없습니다!」
    ///   「교육 가능 기능」에서 고른다 → 0x00461470
    ///     한도(Home.CanLearnMore)를 넘으면 「더 이상 기능을 습득할 수 없습니다!」 / 「… 언어를 …」
    ///     날이 간다(Home.EducateDays) → 한 단계 오른다 → 「%s%s %s%s 터득했습니다!」 → 아이 소감
    /// </code>
    /// 날을 보내는 동안 <b>제독 컨디션이 지난 날의 10분의 1</b> 만큼 찬다(<c>0x0046155B</c> → <c>0x00469820</c>) —
    /// 수련과 같은 셈이다. <c>0x004A5AE0(0x14, 1)</c> 은 애니메이션이 아니라 <b>20밀리초 멈춤</b>
    /// (<c>0x00428000</c> 이 그만큼 기다리다 글쇠·클릭이 오면 곧바로 빠진다)이라 옮길 것이 없다.
    /// 아내가 없으면 막는 말도 없이 끝난다 — 게임 그대로다.
    /// </remarks>
    /// <summary>
    /// 자택 「은퇴한다」(<c>0x00462050</c>) — 두 번 묻고, 그림 15 를 세우고, 세이브를 지운 뒤 끝낸다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   초심자면      「…단, 초심자용 캐릭터는 누적 캐릭터로 등록할 수 없습니다. 좋습니까?」 0x0053A4D8
    ///   비트 0x40     「…단, 누적 캐릭터가 5명 등록되어 있기 때문에 …」                     0x0053A538
    ///   비트 0x10     「…단, %s%s 등록하면 현재 등록되어 있는 누적 캐릭터가 모두 삭제됩니다.」 0x0053A5B0
    ///   아니면        「…은퇴시키고 누적 캐릭터로 등록하겠습니다. 괜찮습니까?」               0x0053A498
    ///   두 번째 물음  「%s%s 모험가로서 게임에 복귀할 수 없게 됩니다만, 괜찮습니까?」          0x0053A630
    ///   EVSTILL 15 · 곡 0x0C · 「%s%s 모험가로서의 일생을 마쳤다...」                          0x0053A670
    ///   그 뒤 0x0041AB90 이 세이브(SAVEDATA.CDS·TMP·ACCDATA.CDS)를 지우고 끝낸다
    /// </code>
    /// 비트 0x40 을 세우는 코드는 게임에 없어 셋째 물음은 절대 안 뜬다 — 옮기지 않는다.
    /// 비트 0x10 은 NEW GAME 에서 「누적캐릭터를 등장시키지 않는다」를 고른 판이다
    /// (<see cref="Player.SkipsCumulative"/>) — 등록하기 앞서 올라 있던 다섯을 모두 지운다(<c>0x0041AD55</c>).
    /// 초심자용이 아니면 누적 캐릭터 다섯 자리에 올린다(<see cref="Engine.AccData"/>) — 행적도
    /// 함께 올라가, 다음 판에서 인물 276~280 으로 서서 옛 발자취를 되짚는다
    /// (<see cref="Engine.AccReplay"/>). 부하·아내·아이는 게임도 <b>안 건드린다</b>(세이브째 사라진다).
    /// </remarks>
    /// <returns>은퇴했으면 참 — 부르는 쪽이 첫 화면으로 돌아간다.</returns>
    public bool Retire()
    {
        var player = _game.Player;
        string me = player.Name;
        bool novice = Engine.Beginner.IsBeginnerBook(player.ActiveStoryBook);
        string first = novice
            ? $"{me}{GameUi.Josa(me, "을", "를")} 은퇴시키겠습니다. 단, 초심자용 캐릭터는 "
              + "누적 캐릭터로 등록할 수 없습니다. 좋습니까?"
            : player.SkipsCumulative
                ? $"{me}{GameUi.Josa(me, "을", "를")} 은퇴시키고 누적 캐릭터로 등록하겠습니다. "
                  + $"단, {me}{GameUi.Josa(me, "을", "를")} 등록하면 현재 등록되어 있는 누적 캐릭터가 모두 삭제됩니다. 괜찮습니까?"
                : $"{me}{GameUi.Josa(me, "을", "를")} 은퇴시키고 누적 캐릭터로 등록하겠습니다. 괜찮습니까?";
        if (!ConfirmDialog.Ask(_view, first)) return false;
        if (!ConfirmDialog.Ask(_view,
                $"{me}{GameUi.Josa(me, "은", "는")} 모험가로서 게임에 복귀할 수 없게 됩니다만, 괜찮습니까?"))
            return false;

        _game.Bgm.Play(RetireTrack);
        DiscoveryDialog.Show(_view, _game.EventStills, RetireStill,
                             $"{me}{GameUi.Josa(me, "은", "는")} 모험가로서의 일생을 마쳤다...");
        // 초심자용 캐릭터가 아니면 누적 캐릭터 다섯 자리에 올린다(0x0041AB90).
        // 자리가 다 찼으면 <b>아무 말 없이</b> 못 올린다 — 원본도 그렇다.
        if (!novice)
        {
            if (player.SkipsCumulative) Engine.AccData.Clear();
            Engine.AccData.Register(player);
        }

        Engine.GameSave.Delete();
        return true;
    }

    /// <summary>은퇴 그림(EVSTILL 15)과 곡(<c>0x0C</c>).</summary>
    private const int RetireStill = 15, RetireTrack = 0x0C;

    public void Educate()
    {
        var owner = Owner;
        void Wife(string words)
        {
            if (_player.Spouse.Length > 0) TalkDialog.Say(owner, null, _player.Spouse, words);
        }
        string Is(string name) => name + GameUi.Josa(name, "은", "는");

        var son = Home.EldestSon(_player);
        var daughter = _player.Children.Where(c => c.Daughter).OrderBy(c => c.Born).FirstOrDefault();
        int sonAge = son?.AgeOn(_player.Date) ?? -1;

        if (son == null || sonAge < Home.EducateAge)
        {
            // 딸을 먼저 적고 아들을 뒤에 적으며, 끝은 <b>아들 이름</b>이다 — 밀어넣는 차례가
            // 딸이름·딸조사·딸나이·아들이름·아들조사·아들나이·아들이름이다(0x004619A3~0x004619C0).
            if (son != null && daughter != null)
                Wife($"{Is(daughter.Name)} {Math.Max(0, daughter.AgeOn(_player.Date))}세, {Is(son.Name)} {Math.Max(0, sonAge)}세에요. {son.Name}에게는 교육은 아직 무리에요.");
            else if (son != null)
                Wife($"{Is(son.Name)} 아직 {Math.Max(0, sonAge)}세에요. 교육은 아직 일러요");
            else if (daughter != null)
                Wife($"{Is(daughter.Name)} 아직 {Math.Max(0, daughter.AgeOn(_player.Date))}세예요. 가정 교육은 나에게 맡겨 주세요.");
            return;
        }

        // 계약은 아들·나이를 다 본 <b>다음</b>에 본다(0x004617FB → 0x0046180D · 0x0046181E →
        // 0x00461827) — 아들이 없거나 어리면 계약 중이어도 계약 대사가 안 나온다.
        if (_player.Contract != null)
        {
            Wife("여보, 당신 지금 계약중이죠? 아이에게 가르쳐 주는 건 고맙지만, 일을 먼저 끝낸 다음에 해 주세요.");
            return;
        }

        var teachable = Home.Teachable(_player, son);
        if (teachable.Count == 0)
        {
            GameDialog.Show(owner, "더 이상 가르칠 것이 없습니다!");
            return;
        }

        var rows = teachable.Select(t => t.Skill
            ? $"{Skill.Names[t.Index]}  {son.Skills[t.Index]}"
            : $"{Skill.Languages[t.Index]}  {son.Tongues[t.Index]}").ToList();
        int pick = ChoiceDialog.Ask(owner, "교육 가능 기능", rows, "취소");
        if (pick < 0 || pick >= teachable.Count) return;
        var (isSkill, index) = teachable[pick];

        if (!Home.CanLearnMore(son, isSkill))
        {
            GameDialog.Show(owner, isSkill ? "더 이상 기능을 습득할 수 없습니다!" : "더 이상 언어를 습득할 수 없습니다!");
            return;
        }

        int next = (isSkill ? son.Skills[index] : son.Tongues[index]) + 1;
        int days = Home.EducateDays(son, next);
        // 가르치는 동안 화면이 덮였다 밝는다(0x004A5AE0(0x14, 1)).
        DayPass.Blackout(_view, () => _player.AdvanceDays(days));
        // 가르치는 동안 쉰 셈으로 컨디션이 찬다(0x0046155B 의 날/10).
        _player.SetCondition(_player.Condition + days / 10);

        var skills = (int[])son.Skills.Clone();
        var tongues = (int[])son.Tongues.Clone();
        if (isSkill) skills[index] = next; else tongues[index] = next;
        _player.ReplaceChild(son, son with { Skills = skills, Tongues = tongues });

        string what = isSkill ? Skill.Names[index] : Skill.Languages[index];
        GameDialog.Show(owner, $"{Is(son.Name)} {what}{GameUi.Josa(what, "을", "를")} 터득했습니다!");

        // 그 다음 숙달 능력 오름(0x004615B4 · 0x00461615 → 0x00490B40)을 부른다. 원본 그대로의 흠이 있다 —
        // 숙달인지는 <b>제독</b>의 그 기능 레벨로 보고, 오르는 것도 <b>제독</b>의 능력(0x005B60C0)인데
        // 알림만 아이 이름으로 「%s의 %s%s %d 올라갔다!」(0x0055A4C0)다.
        int fatherLevel = isSkill ? _player.LevelOf(what) : _player.TongueOf(what);
        var gains = Engine.Town.Mastery.Gains(isSkill ? index : -1, !isSkill, fatherLevel, _random);
        for (int k = 0; k < gains.Length; k++)
        {
            if (gains[k] <= 0) continue;
            int was = _player.AbilityOf(k);
            _player.AdjustAbility(k, gains[k]);
            int up = _player.AbilityOf(k) - was;
            if (up > 0)
                GameDialog.Show(owner, $"{son.Name}의 {Ability.Names[k]}{GameUi.Josa(Ability.Names[k], "이", "가")} {up} 올라갔다!");
        }

        // 아이 소감 — 기능은 2·3 단계에서 기능마다 한마디, 언어는 2 단계에서 그 말로 뽐내고 3 단계에서 딴 나라를 그린다.
        string? remark = isSkill
            ? next == 3 ? Home.SkillRemarks[index].Three : next == 2 ? Home.SkillRemarks[index].Two : null
            : next == 2 ? $"{what}{GameUi.Josa(what, "은", "는")} 유창하게 할 수 있어요! [안×하×요]···어때?"
            : next == 3 ? "세계에는 여러가지 언어가 있네요. 딴 나라에 가보고 싶어." : null;
        // 소감은 아이 얼굴을 걸고 낸다(0x00461723 의 0x00469540(아이, 0, 글)).
        if (remark != null) TalkDialog.Say(owner, ChildFace(son), son.Name, remark);
    }

    // ── 세대교체 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 「세대교체」 — 게임의 <c>0x00461A90</c> 이다. 맏아들이 제독 자리를 잇는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   아들이 없으면 끝
    ///   18세 밑이면   아내 「%s에게는 책임이 너무 무거운 것 같아요. 당신도 아직 일할 수 있잖아요.」
    ///   계약 중이면   아내 「여보, 당신 지금 계약중이 아니에요? 자기 계약은 스스로 끝내 주세요.」
    ///   「%s에게 뒤를 잇게 하겠습니까?」 → 예라야 잇는다
    ///   금화 2/3 · 저금 4/5 · 명성·악명은 Home.InheritedFame/Infamy
    ///   부하를 모두 내보낸다(0x004534E0)
    ///   제독 자리를 아들로 갈아 끼운다(0x0047D4B0) — 이름·생년월일·능력치·기능·언어가 아들 것이 된다
    ///   [플레이어 정보 / 직업 변경 / 게임 재개]
    ///   사건 그림 9 · 소리 0x4D → 「%s의 아들 %s%s %s%s서의 첫걸음을 내디뎠다.」
    ///   딸이 있으면 작별 인사 · 아이 칸을 비운다
    ///   후원자 친밀도 0 · 배신 표시를 지운다(0x00461E40) · 여급 친밀도를 지운다 · 낯튼 사람을 잊는다
    /// </code>
    /// 게임이 18세 밑·계약 중일 때 내는 건 아내 대사뿐이라, 아내가 없으면 아무 말 없이 끝난다 — 그대로 옮겼다.
    /// 얼굴(운명 코드)은 아버지 것을 그대로 쓴다(게임도 아이 칸 +0x334 에 아버지 값을 넣어 두었다 되돌린다).
    /// </remarks>
    public void Succeed()
    {
        var owner = Owner;
        void Wife(string words)
        {
            if (_player.Spouse.Length > 0) TalkDialog.Say(owner, null, _player.Spouse, words);
        }

        if (Home.EldestSon(_player) is not { } son) return;

        int age = son.AgeOn(_player.Date);
        if (age < Home.SucceedAge)
        {
            // 「%s%s %d세입니다. 18세 미만의 아이는…」(0x0053A458)은 안 보이는 기록용
            // 힌트 패널로만 간다(0x00461F9F → 0x0040E0A0(0x580C48, …)) — 창으로 띄우지 않는다.
            Wife($"{son.Name}에게는 책임이 너무 무거운 것 같아요. 당신도 아직 일할 수 있잖아요.");
            return;
        }
        if (_player.Contract != null)
        {
            // 「계약중에는 세대교체를 할 수 없습니다」(0x0053A3E8)도 힌트 패널행이다(0x00461F1F).
            Wife("여보, 당신 지금 계약중이 아니에요? 자기 계약은 스스로 끝내 주세요.");
            return;
        }
        if (!ConfirmDialog.Ask(owner, $"{son.Name}에게 뒤를 잇게 하겠습니까?")) return;

        string father = _player.Name;

        // 명성·악명만 깎여 물려진다. <b>소지금과 저금은 그대로 간다</b> — 게임도 아들 칸에 2/3·4/5 를 적어 두지만
        // 제독 자리로 옮길 때 그 두 칸(+0xF4·+0xF8)을 안 베껴 원래 값이 그대로 남는다(0x0047D4B0).
        _player.Fame = Home.InheritedFame(_player.Fame);
        _player.Infamy = Home.InheritedInfamy(_player.Infamy);

        // 부하를 다 내보낸다.
        for (int slot = 0; slot < _player.Mates.Count; slot++) _player.SetMate(slot, "");

        // 제독 자리를 아들로.
        _player.Given = son.Name;
        _player.Name = _player.Family.Length > 0 ? $"{son.Name}·{_player.Family}" : son.Name;
        _player.BirthMonth = son.Born.Month;
        _player.BirthDay = son.Born.Day;
        _player.BirthYear = son.Born.Year;
        _player.SetAbilities(son.Abilities);
        // 주량도 아들 칸을 그대로 이어받는다(0x0047D4F5 의 rep movsd 가 +0x3C 를 함께 옮긴다).
        _player.Drinking = son.Drinking;
        for (int i = 0; i < Skill.Names.Length && i < son.Skills.Length; i++) _player.SetSkill(Skill.Names[i], son.Skills[i]);
        for (int i = 0; i < Skill.Languages.Length && i < son.Tongues.Length; i++) _player.SetTongue(Skill.Languages[i], son.Tongues[i]);

        // 플레이어 정보 · 직업 변경 · 게임 재개 — 게임 재개를 고를 때까지 돈다.
        while (true)
        {
            int pick = ChoiceDialog.Pick(owner, "세대교체", ["플레이어 정보", "직업 변경", "게임 재개"]);
            if (pick == 0) PlayerInfoDialog.Show(owner, _game);
            else if (pick == 1)
            {
                int job = ChoiceDialog.Ask(owner, "직업 변경",
                                           [.. Job.All.Take(Job.Choosable).Select(j => j.Name)], "취소");
                if (job >= 0 && job < Job.Choosable) _player.JobIndex = job;
            }
            else if (pick == 2) break;
        }

        _game.Sfx?.Play(0x4D - Support.Local.Helpers.WaveBank.FirstSoundId);
        string jobName = _player.Work.Name;
        DiscoveryDialog.Show(owner, _game.EventStills, 9,
            $"{father}의 아들 {son.Name}{GameUi.Josa(son.Name, "은", "는")} {jobName}{GameUi.Josa(jobName, "으로", "로")}서의 첫걸음을 내디뎠다.");

        // 딸이 있으면 작별 인사를 한다.
        if (_player.Children.FirstOrDefault(c => c.Daughter) is { } daughter)
        {
            int her = daughter.AgeOn(_player.Date);
            string? words = her >= 15
                ? (_player.Age > her ? "이것으로 오빠도 성인이 되는거네! 가끔 오빠 집에 놀러 갈께." : "나는 시집가지만 앞으로 열심히 노력해!")
                : her >= 5 ? "오빠, 안녕! 가끔 놀러 갈께요." : null;
            // 딸의 작별 인사도 얼굴을 건다(0x00461DDA 의 0x00469540).
            if (words != null) TalkDialog.Say(owner, ChildFace(daughter), daughter.Name, words);
        }

        _player.ClearChildren();

        // 새 제독은 세상과 새로 인연을 맺는다.
        _player.RestoreCloseness(null);
        _player.RestoreBetrayals(null);
        _player.ClearLiking();
        _player.ForgetEveryone();
    }

    /// <summary>
    /// 아이 이름. 게임은 이름 표에서 뽑는데(<c>0x004611E0</c>) 우리는 <b>제독의 이름</b>에 차례를 붙인다.
    /// </summary>
    /// <remarks>
    /// 게임은 이름 표에서 <b>아무거나 하나 뽑는다</b>(<c>0x004611E0</c>) — 아들은 주인공 이름 목록 서른일곱,
    /// 딸은 여자 이름 열여섯(<see cref="PlayerNameTable.Girls"/>)이다. 성은 아버지 것을 그대로 붙인다.
    /// 표를 못 읽으면 예전처럼 「제 이름 N세」로 물러선다.
    /// </remarks>
    /// <summary>아들 이름 목록 — 한 번 읽어 들고 있는다.</summary>
    private IReadOnlyList<string>? _boyNames;

    private string HeirName(bool daughter)
    {
        var names = daughter
            ? Local.Helpers.PlayerNameTable.Girls
            : _boyNames ??= Local.Helpers.PlayerNameTable.Open(_game.Directory)?.GivenFor(_player.Nation) ?? [];
        if (names.Count == 0)
        {
            string given = _player.Given.Length > 0 ? _player.Given
                         : _player.Name.Length > 0 ? _player.Name : "이름 없는";
            return $"{given} {_player.Children.Count + 2}세";
        }
        return names[_random.Next(names.Count)];
    }

    /// <summary>
    /// 자택의 휴양 창 — 한 달 휴양 · 장기 휴양 · 취소. 게임의 <c>0x00460660</c> 그대로다.
    /// </summary>
    public GameMenu RestMenu() => new(
        [.. Facility.RestMenu.Select(item => (item, RestAction(item)))]);

    private Action? RestAction(string item) => item switch
    {
        "한 달 휴양" => RestOneMonth,
        "장기 휴양" => RestLong,
        Facility.RestExit => _menu.Pop,
        _ => null,
    };

    /// <summary>한 달 쉰다. 물어보고 예라야 쉰다.</summary>
    /// <remarks>쉬든 안 쉬든 휴양 창은 닫힌다 — 원본은 한 번 묻는 팝업이다(<c>0x00469A70</c>).</remarks>
    private void RestOneMonth()
    {
        if (ConfirmDialog.Ask(Owner, "한 달 동안 휴양하겠습니까?")) Rest(1);
        _menu.Pop();
    }

    /// <summary>몇 달이고 쉰다. 게임처럼 한 해까지만 고를 수 있다.</summary>
    private void RestLong()
    {
        var owner = Owner;

        // 물음 뒤에 계산기 판이 곧바로 뜬다(0x00460788 → 0x00481FE0, 1~12) — 수 적기 창이 아니다.
        GameDialog.Show(owner, "몇 개월 동안 휴양하겠습니까?");
        if (NumberPadDialog.Ask(owner, 1, 1, Home.MaxRestMonths) is { } months && months > 0) Rest(months);
        _menu.Pop();
    }

    /// <summary>
    /// 그만큼 쉰다. 값은 안 든다 — 내 집이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 <b>날수</b>를 넘긴다 — 달력 달이 아니라
    /// 서른 날이다. 쉬고 나면 아내가 있으면 아내가, 없으면 지문이 셋 중 하나를 낸다
    /// (<c>0x004607FE</c> 의 <c>rand(3)</c>). 우리 쪽에는 아내가 없어 지문만 쓴다.
    ///
    /// 쉬면 <b>하루에 피로 -1, 사기 +3</b> 씩 돌아온다 — 게임은 그것을 날을 넘기는 자리
    /// (<c>0x004A2AD0</c>)에서 함께 하므로 <see cref="Player.AdvanceDays"/> 가 맡는다.
    /// 그래서 한 달만 쉬어도 폭풍 몇 번 분이 한꺼번에 풀린다.
    /// </remarks>
    private void Rest(int months)
    {
        // 쉬는 동안 화면을 덮는다(0x004606EB — 0x004A59F0 · 0x004A5AE0 · 0x004A5AA0).
        DayPass.Blackout(Owner, () => _player.AdvanceDays(Home.RestDays(months)));

        // 쉬는 사이에 아이가 태어났으면 그 자리에서 소개하고, 그때는 쉰 말을 건너뛴다(0x00460727 →
        // 0x00460150 → 0x0045FFC0). HP 는 그대로 찬다.
        var newborns = Home.NotIntroduced(_player);
        if (newborns.Count > 0)
            foreach (var child in newborns) Introduce(Owner, child);
        // 아내가 있으면 아내가 말하고, 없으면 지문이 뜬다(0x004607FE).
        else if (_player.Spouse.Length > 0)
            TalkDialog.Say(Owner, null, _player.Spouse, Home.RestWifeWord(_random));
        else
            GameDialog.Show(Owner, Home.RestWord(_random));
        // HP 는 달마다 50~99, 아내가 있으면 0~19 더 찬다(0x00460859).
        _player.SetCondition(_player.Condition + Vitality.HomeRest(_random, months, _player.Spouse.Length > 0));
    }

    /// <summary>
    /// 자택의 저금 창 — 저금한다 · 꺼낸다 · 중지한다. 게임의 <c>0x004609C0</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// 제목이 <c>"저금 %8ld 닢"</c>(<c>0x005398C0</c>) 이라 지금 맡겨 둔 돈이 창 이름에 붙는다.
    /// 줄의 켜짐도 게임과 같다 — 저금은 소지금이, 꺼내기는 저금이 있어야 눌린다.
    /// </remarks>
    /// <summary>
    /// 소지금도 저금도 없으면 창이 <b>아예 안 열린다</b>(<c>0x00460A01</c>).
    /// </summary>
    public bool HasMoneyToBank => _player.Gold > 0 || _player.Savings > 0;

    /// <summary>그때 내는 말(<c>0x005398A0</c>).</summary>
    public const string NoMoneyAtAll = "소지금도 저금도 없습니다!";

    public GameMenu SavingsMenu() => new(
        $"저금 {_player.Savings,8} 닢", null,
        [.. Facility.SavingsMenu.Select(item => (item, SavingsAction(item)))]);

    private Action? SavingsAction(string item) => item switch
    {
        "저금한다" when _player.Gold > 0 => Deposit,
        "꺼낸다" when _player.Savings > 0 => Withdraw,
        Facility.SavingsExit => _menu.Pop,
        _ => null,
    };

    /// <summary>
    /// 저금한다. 소지금과 저금 칸이 남은 만큼만 맡길 수 있다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00460AC9</c> 그대로다 — 저금이 이미 백만 닢이면 "더 이상 저금할 수
    /// 없습니다"(<c>0x00539948</c>) 로 물리고, 아니면 <c>min(백만 - 저금, 소지금)</c> 까지 받는다.
    /// </remarks>
    private void Deposit()
    {
        var owner = Owner;

        int room = Player.MaxGold - _player.Savings;
        if (room <= 0)
        {
            GameDialog.Show(owner, "더 이상 저금할 수 없습니다");
            return;
        }

        int want = CountDialog.Ask(owner, "저금한다", "금  액", "닢",
                                   Math.Min(room, _player.Gold), MoneyStep, full: true,
                                   new CountDialog.Gauge("소지금", _player.Gold),
                                   new CountDialog.Gauge("저  금", _player.Savings));
        if (want <= 0) return;

        GameDialog.Show(owner, $"금화 {_player.Deposit(want)}닢을 저금하겠습니다");
        _menu.Pop();   // 맡기고 나면 저금 창이 닫힌다(0x00460BEB) — 수 적기를 물렸을 때만 창이 남는다
    }

    /// <summary>
    /// 저금을 꺼낸다. 소지금도 백만 닢에서 막히므로 그만큼만 꺼낼 수 있다.
    /// </summary>
    /// <remarks>게임의 <c>0x00460B5F</c> 그대로다.</remarks>
    private void Withdraw()
    {
        var owner = Owner;

        int room = Player.MaxGold - _player.Gold;
        if (room <= 0)
        {
            GameDialog.Show(owner, "더 이상 꺼낼 수 없습니다");
            return;
        }

        int want = CountDialog.Ask(owner, "저금을 꺼낸다", "금  액", "닢",
                                   Math.Min(room, _player.Savings), MoneyStep, full: true,
                                   new CountDialog.Gauge("소지금", _player.Gold),
                                   new CountDialog.Gauge("저  금", _player.Savings));
        if (want <= 0) return;

        GameDialog.Show(owner, $"금화 {_player.Withdraw(want)}닢을 꺼내겠습니다");
        _menu.Pop();   // 꺼내고 나면 저금 창이 닫힌다(0x00460BEB)
    }

    /// <summary>돈을 ↑↓ 로 움직이는 단위. Shift 를 누르면 천 닢씩 뛴다.</summary>
    private const int MoneyStep = 100;
}
