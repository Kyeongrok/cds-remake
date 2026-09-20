using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 항구 — 함대편성(기함·편입·삭제·파기) · 선원편성(모집·해고) · 발표.
/// </summary>
/// <remarks>
/// 값과 조건은 <see cref="CrewHire"/> · <see cref="Harbor"/> 가 알고, 여기서는 묻고
/// 알리는 차례만 맡는다. 배를 맡기고 찾는 것은 <b>이 마을</b>과 함께 적히므로 마을
/// 번호를 든다.
/// </remarks>
/// <param name="view">이 항구를 낸 도시 창. 물음창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위가 여기서 온다.</param>
/// <param name="menu">항구 명령 창. 함대편성·선원편성 창을 그 위에 쌓는다.</param>
/// <param name="cityId">이 마을 번호. 배를 맡기고 찾을 때 쓴다.</param>
/// <param name="culture">이 마을 문화권. 부관이 없을 때 나서는 얼굴이 여기 따라 갈린다.</param>
internal sealed class HarborMenu(Window view, Engine.Game game, GameMenuHost menu, int cityId,
                                 int culture)
{
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly GameMenuHost _menu = menu;
    private readonly int _cityId = cityId;
    private readonly int _culture = culture;

    private Player _player => _game.Player;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

    /// <summary>부관이 앉는 자리. <see cref="Player.MateRoles"/> 의 첫 자리다.</summary>
    private const int MateSlot = 0;

    /// <summary>
    /// 항구에 들어설 때 부관이 건네는 한마디. 부관 자리가 비었으면 아무 일도 없다.
    /// </summary>
    /// <remarks>
    /// 칸 2 <c>0x004770A0</c> 이다. 바다로 <b>닿아서</b> 들어왔으면(<c>+0x98</c>) 묻지 않고,
    /// 마을에서 걸어 들어왔을 때만 「제독, 바다에 나가시겠습니까?」(<c>0x00477141</c>)다.
    /// 빌린 배 인사(<c>0x00476EC0</c>)는 그 <b>뒤</b>다.
    /// </remarks>
    /// <remarks>
    /// 여기만은 화자표가 아니라 <b>부하 제 얼굴</b>이다. 부하는 이름만 들고 있어 신상은
    /// 판이 찾아 준다(<see cref="Engine.Game.MateInfo"/>) — 못 찾으면 얼굴 없이 말만
    /// 낸다. 그림이 없다고 말까지 막을 일은 아니다.
    /// </remarks>
    public void Greet(bool arrived = false)
    {
        if (!arrived && _player.MateAt(MateSlot).Length > 0)
            ConfirmDialog.Tell(_view, "제독, 바다에 나가시겠습니까?", face: MateFace());

        GreetLoan();
    }

    /// <summary>
    /// 스폰서가 대 준 배가 있으면 항구 사람이 아는 체를 한다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00476EC0</c> 이다.
    /// <code>
    ///   476EC0  계약 물건(0x0061D1D0) 이 있다
    ///   476ECD  배를 빌렸다(0x0061D1E8 == 1)   ; 0x0041079D 가 박아 둔 깃발
    ///   476EEF  "%s님이시지요?"                 제독 이름
    ///   476F2B  "%s %s%s 배를 준비하도록 전해 들었습니다. 편성 명령이 있을 경우는
    ///            언제든지 준비하겠습니다."      후원자 이름 + 경칭 + 조사
    /// </code>
    /// 얼굴은 <b>항구 화자</b>다(<c>0x00476EF6</c> 이 시설 객체의 <c>+0x80</c> 을 넘긴다).
    ///
    /// <b>딱 한 번만 나온다</b> — 게임은 인사를 낸 바로 뒤 <c>0x00476F40</c> 에서 깃발을
    /// 1 에서 2 로 올리고, 물음(<c>0x00476ECD</c>)은 1 일 때만 참이다.
    /// </remarks>
    private void GreetLoan()
    {
        if (_player.Contract is not { ShipsLent: true, LoanAnnounced: false } deal) return;
        deal.LoanAnnounced = true;

        var face = _game.SpeakerFace(BuildingCode, _culture);
        ConfirmDialog.Tell(_view, $"{_player.Name}님이시지요?", face: face);

        var sponsor = _game.Sponsors?.FindByName(deal.Sponsor);
        string name = sponsor?.Name ?? deal.Sponsor;
        string sir = sponsor?.Honorific ?? "각하";
        ConfirmDialog.Tell(_view,
            $"{name} {sir}{FromParticle(sir)} 배를 준비하도록 전해 들었습니다. "
          + "편성 명령이 있을 경우는 언제든지 준비하겠습니다.", face: face);
    }

    /// <summary>「으로부터 / 로부터」 — 앞 글자에 받침이 있으면 「으」가 붙는다.</summary>
    private static string FromParticle(string word)
    {
        if (word.Length == 0) return "으로부터";

        char last = word[^1];
        if (last is < '가' or > '힣') return "으로부터";

        int coda = (last - '가') % 28;                  // 0 이면 받침이 없다
        return coda is 0 or 8 ? "로부터" : "으로부터";  // 8 = ㄹ 받침도 「로」다
    }

    /// <summary>항구의 건물 코드. 부관이 없을 때 화자표에서 사람을 찾는다.</summary>
    private const int BuildingCode = 0;

    /// <summary>
    /// 부관 얼굴. 자리가 비었거나 신상을 못 찾으면 null.
    /// </summary>
    /// <remarks>성문도 이 얼굴을 쓴다 — <see cref="CityPicView"/> 의 탐험 관문이다.</remarks>
    /// <remarks>
    /// 항구에서 말을 거는 것은 <b>부하 첫 자리</b>다. 부하는 이름만 들고 있어 신상은
    /// 판이 찾아 준다(<see cref="Engine.Game.MateInfo"/>).
    /// </remarks>
    internal uint[]? MateFace()
    {
        string mate = _player.MateAt(MateSlot);
        if (mate.Length == 0) return null;

        return _game.MateInfo(mate) is { Face: >= 0 and < 0xFFFF } who
            ? _game.Faces?.TryGetBgra(who.Face, female: false)
            : null;
    }

    /// <summary>
    /// 부관이 없을 때 출항 알림에 서는 얼굴 번호. 화면에서 대 보아 <b>299번</b>이었다.
    /// </summary>
    /// <remarks>
    /// 화자표(<c>0x0056823C</c>)에서 끌어오던 것을 못 박았다 — 화자표는 문화권마다 다른
    /// 얼굴을 내는데, 출항 알림은 어느 마을에서나 같은 뱃사람 얼굴이다.
    /// </remarks>
    private const int SailorFace = 299;

    /// <summary>
    /// 출항 알림에 서는 얼굴 — <b>부관이 있으면 부관, 없으면 뱃사람</b>이다.
    /// </summary>
    /// <remarks>부관 자리가 비어도 창이 얼굴 없이 뜨지는 않는다. 화면에서 본 대로다.</remarks>
    private uint[]? SailFace() =>
        MateFace() ?? _game.Faces?.TryGetBgra(SailorFace, female: false);

    /// <summary>이만큼 버틸 수 있으면 "준비 만반" 이다(<c>0x004772A0</c> 의 <c>cmp eax,0x14</c>).</summary>
    private const int ReadyDays = 20;

    /// <summary>
    /// "출항" 을 눌렀을 때의 관문. 나가도 좋으면 true.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00477220</c> 차례 그대로다.
    /// <code>
    ///   477253  선원 0 이하        "선원이 모자랍니다. 이래서는 출항할 수 없습니다!"   막는다
    ///   47726D  선원 &lt; 필요       "…함대의 속도가 늦어지지만, 괜찮으십니까?"          YES/NO
    ///   47728F  버틸 날 0 이하     "이것만으로는 보급 물자가 모자랍니다!"              막는다
    ///   4772A5  버틸 날 &lt; 20      "%d일 정도 항해할 수 있다고 생각합니다. 출항하겠습니까?"  YES/NO
    ///   4772B7  그 밖              "준비 만반입니다. 언제라도 출항할 수 있습니다!…"    YES/NO
    /// </code>
    /// 선원이 모자랄 때의 문구 <b>둘</b>은 <c>0x00469680</c> 이 부관 있고 없고로 고른다 —
    /// 부관이 있으면 <c>0x00544ED8</c> "제독, … 함대가 늦어지지만", 없으면
    /// <c>0x00544F20</c> "… 함대의 속도가 늦어지지만" 이다. 한 글자씩 다르다.
    ///
    /// 선원 둘은 <c>0x004695C0</c>·<c>0x00469680</c> 으로 나간다 — 둘 다 <c>0x004695E0</c> 을 거쳐
    /// <b>부관이 있으면 부관 얼굴</b>, 없으면 얼굴 없는 알림이다(예전 화면은 부관 없이 본 것이다).
    /// 보급 셋은 <c>0x00469660</c> 으로 나가고 그 얼굴은 <b>부관</b>, 부관 자리가 비면
    /// <b>항구 화자</b>가 대신 나선다(<see cref="SailFace"/>).
    ///
    /// 맨 앞에서 보는 것은 "편성돼 있지 않은 선박"(<c>0x004688A0</c>) — 이 마을에
    /// <b>맡겨 둔 배</b>가 한 척이라도 있으면 출항을 막는다.
    /// </remarks>
    public bool ConfirmSail()
    {
        var owner = Owner;

        // 이 마을에 맡겨 둔 배가 있으면 출항이 막힌다(0x004688CE) — 얼굴 없는 알림이다.
        // <b>모항은 예외</b>다 — 0x004688A8 이 도시 +0x1D 비트 8 을 보고 검사를 건너뛴다.
        if (_cityId != _player.HomePort && _player.DockedAt(_cityId).Count > 0)
        {
            ConfirmDialog.Tell(owner, "편성돼 있지 않은 선박이 있습니다! 출항할 수 없습니다");
            return false;
        }

        if (_player.Crew <= 0)
        {
            MateSays(owner, "선원이 모자랍니다. 이래서는 출항할 수 없습니다!");
            return false;
        }

        if (_player.Crew < _player.MinCrew
            && !MateAsks(owner, _player.MateAt(0).Length > 0
                   ? "제독, 선원이 모자랍니다. 이대로라면 함대가 늦어지지만, 괜찮으십니까?"
                   : "선원이 모자랍니다. 이대로라면 함대의 속도가 늦어지지만, 괜찮으십니까?"))
            return false;

        // 보급 쪽은 부관이 말한다. 부관이 없으면 항구 사람이 대신 나선다.
        uint[]? face = SailFace();

        int days = _player.SupplyDaysLeft;
        if (days <= 0)
        {
            ConfirmDialog.Tell(owner, "이것만으로는 보급 물자가 모자랍니다!", face: face);
            return false;
        }

        if (!ConfirmDialog.Ask(owner, days < ReadyDays
                ? $"{days}일 정도 항해할 수 있다고 생각합니다. 출항하겠습니까?"
                : "준비 만반입니다. 언제라도 출항할 수 있습니다! 출항하겠습니까?", face: face))
            return false;

        // 모항에서 나설 때는 아내가 배웅한다(0x00477181 — 도시 +0x1D 비트 8 과 아내가 있을 때).
        if (_cityId == _player.HomePort && _player.Spouse.Length > 0)
            TalkDialog.Say(owner, null, _player.Spouse, Farewells[_game.Random.Next(Farewells.Length)]);

        return true;
    }

    /// <summary>모항에서 나설 때 아내가 하는 말 셋(<c>0x00544E48</c>~).</summary>
    private static readonly string[] Farewells =
    [
        "부디, 무사히 돌아 오세요.",
        "꼭 돌아오세요.",
        "엉뚱한 짓은 하지 말아요. 기다리고 있을 테니.",
    ];

    /// <summary>
    /// 함대편성 창. 게임처럼 제목 없이 줄만 쌓고, 마지막 줄만 회녹색 띠가 된다.
    /// "편성 종료" 를 누르면 항구 창으로 되돌아간다 — 창을 닫는 것이 아니라 담긴 것만 갈린다.
    /// </summary>
    public GameMenu FleetMenu() => new(
        [.. Facility.FleetMenu.Select(item => (item, FleetAction(item)))]);

    /// <summary>
    /// 함대편성 줄의 켜짐. 게임의 조건을 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기함 변경  0x0046A220  배가 두 척 이상
    ///   선박 편입  0x0046A240  함대가 여덟 척 미만이고 이 마을에 맡긴 배가 있다
    ///   선박 삭제  0x0046A270  배가 두 척 이상(이 마을이 더 맡을 수 있어야)
    ///   선박 파기  0x0046A2C0  배가 두 척 이상
    /// </code>
    /// 조건이 어긋난 줄은 흐리게 둔다.
    /// </remarks>
    /// <summary>
    /// 함대편성 줄 자체를 켤지 — 그 안의 네 줄 가운데 하나라도 살아 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 배가 없어도 계류된 배가 있으면 「선박 편입」이 살아 있어 창이 열린다. 빌린 배를
    /// 이 창으로만 받을 수 있으니 여기서 막으면 길이 끊긴다.
    /// </remarks>
    public bool CanFormFleet =>
        Facility.FleetMenu.Any(item => item != Facility.FleetExit && FleetAction(item) != null);

    private Action? FleetAction(string item) => item switch
    {
        "기함 변경" when _player.Ships.Count > 1 => ChangeFlagship,
        "선박 편입" when !_player.IsFleetFull
                      && _player.DockedAt(_cityId).Count > 0 => TakeShip,
        // 배를 맡겨 두는 것은 <b>모항에서만</b>이다(0x0046A2A4 — 도시 +0x1D 비트 8).
        "선박 삭제" when _player.Ships.Count > 1 && _cityId == _player.HomePort
                      && _player.DockedAt(_cityId).Count < Player.MaxDocked => LeaveShip,
        "선박 파기" when _player.Ships.Count > 1 => ScrapShip,
        Facility.FleetExit => FleetDone,
        _ => null,
    };

    /// <summary>
    /// 함대편성을 나선다(<c>0x0046A65F</c>) — 덜어 낸 배에서 내린 선원은 남은 정원만큼만 도로 타고
    /// (<c>0x0040E3F0</c>) 나머지는 사라진다. 그 다음 짐이 넘치면 알리고 짐 덜기 창을 띄운다(<c>0x0044DEF0</c>).
    /// </summary>
    private void FleetDone()
    {
        _player.SetCrew(_player.Crew);
        CargoDropDialog.Force(Owner, _game, _cityId);
        _menu.Pop();
    }

    /// <summary>함대 목록의 차례 — <b>기함이 맨 앞</b>이고 나머지는 칸 차례다(<c>0x0049D360</c>).</summary>
    private List<int> FleetOrder()
    {
        var order = new List<int>();
        int flag = _player.Flagship;
        if (flag >= 0 && flag < _player.Ships.Count) order.Add(flag);
        for (int i = 0; i < _player.Ships.Count; i++) if (i != flag) order.Add(i);
        return order;
    }

    /// <summary>기함을 바꾼다. 게임의 <c>0x0046A2F0</c> 자리다.</summary>
    private void ChangeFlagship()
    {
        var owner = Owner;
        var ships = _player.Ships;

        var order = FleetOrder();
        int pick = HintListDialog.Pick(owner,
            [.. order.Select(i => ShipyardMenu.ShipLine(ships[i], i == _player.Flagship))],
            "기함 변경", "바꿀 배가 없습니다");
        if (pick < 0) return;
        int at = order[pick];

        var name = ships[at].Name;
        if (!ConfirmDialog.Ask(owner, $"기함을 {name}호로 변경하겠습니다. 좋습니까?")) return;

        _player.SetFlagship(at);
        _menu.Refresh();   // 기함이 바뀌면 줄 켜짐도 다시 잰다
    }

    /// <summary>맡겨 둔 배를 함대에 넣는다. 게임의 <c>0x0046A350</c> 자리다.</summary>
    /// <remarks>
    /// 한 척 받고 끝나지 않는다 — 물리거나 맡긴 배가 떨어질 때까지 목록을 다시 연다(<c>0x0046A3DE</c> →
    /// <c>0x0046A355</c>). 받다가 여덟 척이 차면 「이 이상 편입할 수 없습니다.」(<c>0x005453D0</c>)다.
    /// </remarks>
    private void TakeShip()
    {
        var owner = Owner;
        while (_player.DockedAt(_cityId) is { Count: > 0 } docked)
        {
            if (_player.IsFleetFull)
            {
                GameDialog.Show(owner, "이 이상 편입할 수 없습니다.");
                break;
            }

            int at = HintListDialog.Pick(owner, [.. docked.Select(h => ShipyardMenu.ShipLine(h, false))],
                                         "편입선박 선택", "이 마을에 맡겨 둔 배가 없습니다");
            if (at < 0) break;
            _player.Undock(_cityId, at);
        }

        // 계류된 배를 다 데려가면 「선박 편입」이 흐려진다 — 줄을 다시 지어야 보인다.
        _menu.Refresh();
    }

    /// <summary>함대의 배를 이 마을에 맡긴다. 게임의 <c>0x0046A400</c> 자리다.</summary>
    private void LeaveShip()
    {
        var owner = Owner;

        var order = FleetOrder();
        int pick = HintListDialog.Pick(owner,
            [.. order.Select(i => ShipyardMenu.ShipLine(_player.Ships[i], i == _player.Flagship))],
            "선박삭제", "삭제할 배가 없습니다");
        if (pick < 0) return;
        int at = order[pick];

        if (!_player.Dock(at, _cityId))
            GameDialog.Show(owner, "이 이상 삭제할 수 없습니다.");

        _menu.Refresh();
    }

    /// <summary>배를 없앤다. 게임의 <c>0x0046A490</c> 자리다 — 묻지 않는다.</summary>
    private void ScrapShip()
    {
        var owner = Owner;

        var order = FleetOrder();
        int pick = HintListDialog.Pick(owner,
            [.. order.Select(i => ShipyardMenu.ShipLine(_player.Ships[i], i == _player.Flagship))],
            "선박파기", "파기할 배가 없습니다");
        if (pick < 0) return;
        int at = order[pick];

        // 원본은 고르면 묻지 않고 곧바로 없앤다(0x0046A4B8 → 0x00473E60 · 0x0044CA90).
        if (!_player.Scrap(at))
            GameDialog.Show(owner, "이 이상 파기할 수 없습니다.");

        _menu.Refresh();
    }

    /// <summary>
    /// 선원편성 창. 모집·해고 두 줄과 돌아가기다 — 게임의 <c>0x004774E0</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// "선원해고" 는 태운 선원이 있어야 눌린다. 게임도 고르는 창을 지으며 그 줄의 켜짐을
    /// <c>0x0040E360() &gt; 0</c>(지금 선원 수)으로 정한다(<c>0x0047753E</c>).
    /// </remarks>
    public GameMenu CrewMenu() => new(
        [.. Facility.CrewMenu.Select(item => (item, CrewAction(item)))]);

    /// <summary>
    /// "선원편성" 을 눌렀을 때. <b>늘 선원모집 · 선원해고 · 돌아간다 창을 낸다.</b>
    /// </summary>
    /// <remarks>
    /// 예전에는 선원이 하나도 없으면 창을 건너뛰고 곧장 모집으로 갔다. 게임은 그러지 않는다 —
    /// 창(<c>0x004774E0</c>)은 늘 서고 해고할 사람이 없을 때 "선원해고" 줄만 흐려진다
    /// (<see cref="CrewAction"/>).
    /// </remarks>
    public void CrewForm() => _menu.Push(CrewMenu);

    private Action? CrewAction(string item) => item switch
    {
        "선원모집" => HireCrew,
        "선원해고" when _player.Crew > 0 => FireCrew,
        Facility.CrewExit => _menu.Pop,
        _ => null,
    };

    /// <summary>선원 한 사람 값. 이름이 높을수록 싸다(<see cref="CrewHire"/>).</summary>
    private int CrewPrice => CrewHire.PriceFor(_player.Fame);

    /// <summary>
    /// 선원을 모집한다. 게임의 <c>0x00477330</c> 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 정원이 찼으면 아예 묻지 않고 물린다. 값을 못 치르면 다시 묻고, 다 태우고 나서도
    /// 최저 승원에 모자라면 한 번 더 권한다 — 게임도 그 자리에서 되돌아간다.
    ///
    /// 막는 말 둘은 <b>부관이 있으면 다른 말</b>이다(<c>0x00469680</c>).
    /// <code>
    ///   0x00545008 · 0x00545048   정원이 찼다
    ///   0x00545118 · 0x00545148   돈이 모자란다
    /// </code>
    /// </remarks>
    private void HireCrew()
    {
        var owner = Owner;
        bool mate = _player.MateAt(0).Length > 0;

        while (true)
        {
            if (_player.Crew >= _player.MaxCrew)
            {
                MateSays(owner, mate
                    ? "제독, 이 이상 선원을 고용해도, 태울 수 있는 배가 없습니다."
                    : "선원수가 함대의 상한에 달하고 있습니다! 이 이상 고용해도 승선할 수 없습니다.");
                return;
            }

            int price = CrewPrice;
            MateSays(owner, $"몇 명 모집하겠습니까? 한 사람 당 금화 {price}닢 필요합니다.");

            // 돈이 모자라면 <b>수 적기 창으로 곧장</b> 되돌아간다(0x00477400 → 0x004773AB) — 값을 다시 이르지 않는다.
            int want;
            while (true)
            {
                want = CountDialog.Ask(owner, "선원고용", "고용할 사람 수", "명",
                                       _player.MaxCrew - _player.Crew, 1, false,
                                       new CountDialog.Gauge("현재의 선원 수", _player.Crew),
                                       new CountDialog.Gauge("최저 선원 수", _player.MinCrew));
                if (want <= 0) return;
                if (price * want <= _player.Gold) break;

                MateSays(owner, mate
                    ? "그렇게 고용할 수 있을 정도로 돈이 없습니다." : "소지금이 모자랍니다.");
            }

            _player.Pay(price * want);
            _player.AddCrew(want);

            // 아직 최저 승원에 모자라면 한 번 더 권한다.
            int lack = _player.MinCrew - _player.Crew;
            if (lack <= 0) return;
            if (!MateAsks(owner,
                    $"앞으로 적어도 {lack}명은 필요합니다. 좀더 선원을 모집하겠습니까?"))
                return;
        }
    }

    /// <summary>
    /// 선원을 해고한다. 게임의 <c>0x00477460</c> 차례 그대로다 — 삯은 돌려주지 않는다.
    /// </summary>
    private void FireCrew()
    {
        // 이 창의 말은 다 부관 몫이다(0x004695C0 · 0x00469680 → 0x004695E0) — 부관이 있으면 부관 얼굴,
        // 없으면 얼굴 없는 알림이다.
        var owner = Owner;

        MateSays(owner, "선원을 몇 명 해고시키겠습니까?");

        int want = CountDialog.Ask(owner, "선원해고", "해고할 사람 수", "명", _player.Crew,
                                   1, false,
                                   new CountDialog.Gauge("현재의 선원 수", _player.Crew),
                                   new CountDialog.Gauge("최저 승원 수", _player.MinCrew));
        if (want <= 0) return;

        // 최저 승원을 밑돌게 되면 한 번 물어본다.
        if (_player.Crew - want < _player.MinCrew
            && !MateAsks(owner, "선원 수가 최저 승원 수를 밑돌고 있습니다. 괜찮습니까?"))
            return;

        _player.AddCrew(-want);
    }

    /// <summary>부관이 말한다 — 부관이 없으면 얼굴 없는 알림이다(<c>0x004695E0</c>).</summary>
    private void MateSays(Window owner, string text)
    {
        if (MateFace() is { } face) ConfirmDialog.Tell(owner, text, face: face);
        else GameDialog.Show(owner, text);
    }

    /// <summary>부관이 묻는다 — 부관이 없으면 얼굴 없이 묻는다.</summary>
    private bool MateAsks(Window owner, string text) => ConfirmDialog.Ask(owner, text, face: MateFace());

    // ── 마을정보 ────────────────────────────────────────────────────────────

    /// <summary>한 건 값의 밑값. 시세를 먹여 실제 값이 나온다(<c>0x00477735</c> 의 <c>0x64</c>).</summary>
    private const int CityInfoBase = 100;

    /// <summary>
    /// 항구의 <b>마을정보</b> — 값을 받고 같은 지역 도시의 위경도를 일러 준다.
    /// </summary>
    /// <remarks>
    /// 게임 자리는 <c>0x00477650</c> 이다.
    /// <code>
    ///   00477735  값 = 0x429DC0(도시, 100) = 시세 * 100 / 100  (적어도 1)
    ///   0047775D  "다른 마을에 대해 듣고 싶나? 그렇다면 한건 당 금화 %ld닢이네."
    ///   00477773  소지금 &lt; 값 이면 "공짜로 가르쳐 줄 것은 없네."
    ///   0047777B  마을정보 목록 — 고를 때마다 값을 물고 한 곳씩 일러 준다
    ///   004775F0  목록은 <b>같은 지역 무리</b>(표 +0x1C)의 도시다. 지금 있는 마을은 뺀다
    /// </code>
    /// 값을 못 치를 때까지 목록으로 되돌아온다 — 게임도 그 자리에서 돈다.
    /// </remarks>
    public void CityInfo()
    {
        var owner = Owner;
        var rows = _game.CityRows;
        if (rows == null) return;

        int fee = Math.Max(_game.Rates.Of(_cityId) * CityInfoBase / 100, 1);

        // 같은 지역의 도시들. 지금 있는 마을은 뺀다 — 여기 있는데 물을 까닭이 없다.
        // 아직 안 선 도시도 뺀다(0x00477611 — 도시 +4 비트 4).
        var towns = rows.InRegion(rows.RegionOf(_cityId));
        towns.Remove(_cityId);
        towns.RemoveAll(c => !_game.CityStanding(c));
        if (towns.Count == 0) return;

        // 세 마디 다 <b>항구 사람</b> 얼굴이다(0x0047776B · 0x00477905 · 0x004777DA — 시설 +0x80).
        var face = _game.SpeakerFace(BuildingCode, _culture);
        ConfirmDialog.Tell(owner,
            $"다른 마을에 대해 듣고 싶나? 그렇다면 한건 당 금화 {fee}닢이네.", face: face);

        var names = towns.Select(_game.CityName).ToList();
        while (_player.Gold >= fee)
        {
            int pick = MapPointDialog.Ask(owner, names, "마을정보", MapPointDialog.NarrowWidth);
            if (pick < 0) return;

            _player.Pay(fee);
            ConfirmDialog.Tell(owner, WhereIs(towns[pick]), face: face);
        }

        ConfirmDialog.Tell(owner, "공짜로 가르쳐 줄 것은 없네.", face: face);
    }

    /// <summary>
    /// 「톨레도라면 북위  40도, 서경   4도네.」 — 게임 서식 <c>0x00545280</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// 도를 셈하는 자리는 <c>0x00477830</c> 이다. 표의 칸 좌표를 열여섯 곱해 원본값으로
    /// 되돌린 뒤, 가운데(경도 20000 · 위도 10000)에서 떨어진 만큼에 <c>9/1000</c> 을 먹인다.
    /// <code>
    ///   도 = |원본값 - 가운데| * 9 / 1000
    ///   경도 20000 이상이면 동, 아니면 서 · 위도 10000 이상이면 남, 아니면 북
    /// </code>
    /// 톨레도는 칸 (1219, 346) 이라 서경 4도 · 북위 40도가 된다.
    /// </remarks>
    private string WhereIs(int city)
    {
        var rows = _game.CityRows;
        string name = _game.CityName(city);
        if (rows == null || !rows.TryCell(city, out int cx, out int cy, out _))
            return $"{name}{GameUi.Josa(name, "이라면", "라면")} 나도 모르네.";

        int lonRaw = cx * RawPerCell, latRaw = cy * RawPerCell;
        int lon = Math.Abs(lonRaw - LonMiddle) * DegreeNum / DegreeDen;
        int lat = Math.Abs(latRaw - LatMiddle) * DegreeNum / DegreeDen;

        return $"{name}{GameUi.Josa(name, "이라면", "라면")} " +
               $"{(latRaw >= LatMiddle ? "남" : "북")}위 {lat,3}도, " +
               $"{(lonRaw >= LonMiddle ? "동" : "서")}경 {lon,3}도네.";
    }

    /// <summary>칸 하나가 원본값 열여섯이다(<c>0x0047785E</c> 의 <c>shl 4</c>).</summary>
    private const int RawPerCell = 16;

    /// <summary>가운데 — 경도는 그리니치, 위도는 적도다.</summary>
    private const int LonMiddle = 20000, LatMiddle = 10000;

    /// <summary>원본값을 도로 바꾸는 비(<c>9/1000</c>).</summary>
    private const int DegreeNum = 9, DegreeDen = 1000;

    /// <summary>지금 항구에서 알릴 수 있는 발견물(<see cref="Harbor.Announceable"/>).</summary>
    public List<DiscoveryTable.Record> Announceable() =>
        Harbor.Announceable(_player, _game.Discoveries?.Table, _game.Hints);

    /// <summary>
    /// 발견물을 알린다. 게임의 <c>0x00476E10</c> → <c>0x0047EA80</c> 차례다.
    /// </summary>
    /// <remarks>
    /// 알리면 명성이 <b>보수 ÷ 70</b>(적어도 10)만큼 오른다(<c>0x0047E849</c> 가 보수를
    /// 0x46 으로 나누고 10 과 견준다). 그 자리에서 <b>피로도가 풀리고 규율이 100 으로
    /// 돌아온다</b>(<c>0x0047E885</c> · <c>0x0047E88A</c>) — <see cref="Harbor.Celebrate"/> 다.
    ///
    /// 창은 <b>여럿 고르기</b>다(<c>0x0047EA80</c>) — 켠 것을 결정 한 번에 차례로 다 알리고 끝난다.
    /// <code>
    ///   고르기    「발표할 발견물 선택」(0x0055A358), 누를 때마다 켜고 끈다
    ///   넘침      켠 것 가운데 아이템을 주는 수 + 지금 소지품 &gt; 16 이면
    ///             「소지품을 다 갖진 못하게 됩니다만, 괜찮습니까?」(0x0055A370) — 아니오면 고르던 창으로
    ///   알리기    켠 차례대로 0x0047E8D0: 말 · 동영상 · 명성(0x0047E810)
    ///   아이템    다 알린 뒤 한꺼번에 들인다(0x0047EA5F → 0x004B1710)
    /// </code>
    /// </remarks>
    public void Announce()
    {
        var owner = Owner;
        var rows = Announceable();
        if (rows.Count == 0) return;

        IReadOnlyList<int> picked = [];
        while (true)
        {
            picked = HintListDialog.PickMany(owner, [.. rows.Select(r => r.Name)], "발표할 발견물 선택", picked);
            if (picked.Count == 0) return;

            int gives = picked.Count(i => rows[i].GivesItem);
            if (_player.Items.Count + gives <= Player.MaxItems
                || ConfirmDialog.Ask(owner, "소지품을 다 갖진 못하게 됩니다만, 괜찮습니까?"))
                break;
        }

        var found = new List<int>();
        foreach (int at in picked)
        {
            var row = rows[at];
            if (!_player.Announce(row.Id)) continue;
            if (row.GivesItem) found.Add(row.ItemId);

            // 어느 것이든 먼저 「%s의 발견을 발표했다!」와 동영상이다(0x0047E953 · 0x0047E96F) — 그 다음에
            // 들어 주는지를 가린다(0x0047E810).
            GameDialog.Show(owner, $"{row.Name}의 발견을 발표했다!");

            // 그림은 <b>세 단</b>이다(0x0047E96F → 0x004AAF30) — 표 +0x10 동영상, 없으면 +0x14 움직이는 그림
            // (DISCOVER.CDS), 없으면 +0x0C 스틸이다. 보고도 같은 함수를 쓴다.
            if (row.Movie >= 0)
                MoviePlayer.Play(owner, DiscoveryDialog.MovieOf(_game.Directory, row.Movie));
            else if (row.Clip >= 0)
                DiscoveryClipPlayer.Play(owner, _game.Clips, row.Clip);
            else if (row.Picture >= 0)
                DiscoveryDialog.ShowPicture(owner, _game.Stills, row.Picture);   // 그림만(0x004AD640)

            // 자리로는 못 찾는 것(유적 속 물건·인물·비보)은 알려도 아무도 안 들어 준다
            // (0x0047E8AE) — 명성도 회복도 없이 알린 것으로만 찍힌다.
            if (row.Indirect)
            {
                GameDialog.Show(owner, Harbor.NobodyCares);
                continue;
            }

            int fame = Harbor.FameFor(row);
            _player.Fame += fame;
            Harbor.Celebrate(_player);

            GameDialog.Show(owner, $"명성이 {fame} 올라갔다!");
        }

        // 발표한 발견물의 아이템이 <b>그제야</b> 소지품으로 들어온다 — 아무도 안 들어 준 것이어도 들어온다.
        // 넘치면 물릴 수 없는 버리기 창이다(ItemGain).
        ItemGain.AddForced(owner, _game, found);
    }
}
