using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 조선소 — 매각 · 수리 · 개조(마스트 · 돛 · 포탑 · 대포 · 선명).
/// </summary>
/// <remarks>
/// 값과 조건은 <see cref="Shipyard"/> 가 알고, 여기서는 <b>묻고 알리는 차례</b>만 맡는다.
/// 게임도 조선소 화면 하나가 이 셋을 다 거느린다(<c>0x0044B820</c> 매각 ·
/// <c>0x0044B9C0</c> 수리 · <c>0x00496960</c> 개조).
///
/// 도시 그림 창이 들고 있던 것을 그대로 옮겼다 — 배를 손보는 일은 도시가 아니라
/// 조선소가 한다.
/// </remarks>
/// <param name="view">이 조선소를 낸 도시 창. 물음창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위가 여기서 온다.</param>
/// <param name="menu">조선소 명령 창. 줄의 흐림을 다시 잡을 때 쓴다.</param>
/// <param name="cityId">이 마을 번호. 맡겨 둔 배를 찾을 때 쓴다.</param>
/// <param name="rate">이 마을 시세(%). 매각·수리 값에 먹인다.</param>
internal sealed class ShipyardMenu(Window view, Engine.Game game, GameMenuHost menu,
                                   int cityId, int culture, int rate)
{
    /// <summary>조선소의 건물 코드. 화자표에서 목수를 찾을 때 쓴다.</summary>
    private const int BuildingCode = 6;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly GameMenuHost _menu = menu;
    private readonly int _cityId = cityId;
    private readonly int _culture = culture;
    private readonly int _rate = rate;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window ?? _view;

    /// <summary>
    /// 조선소 주인 얼굴. <b>이 집의 말은 모두 이 얼굴을 세운다</b> — 게임도 그렇다.
    /// </summary>
    private uint[]? Face => _game.SpeakerFace(BuildingCode, _culture);

    /// <summary>주인이 한 마디 한다.</summary>
    private void Say(string text) => ConfirmDialog.Tell(Owner, text, face: Face);

    /// <summary>
    /// 얼굴 없는 알림(<c>0x00469060</c>) — 정박·척수·자금·매각값·수리 돈 부족은 목수 말이 아니라 이것이다.
    /// </summary>
    private void Notice(string text) => ConfirmDialog.Tell(Owner, text);

    /// <summary>주인이 예·아니오를 묻는다.</summary>
    private bool Ask(string text) => ConfirmDialog.Ask(Owner, text, face: Face);

    /// <summary>
    /// 들어설 때 목수가 건네는 한마디. 게임의 <c>0x0044B4A0</c> 자리다 — 문구가
    /// <c>0x00530F38</c> 이고, 얼굴은 이 마을 문화권이 정한다(리스본은 402, 이슬람권은
    /// 315 다).
    /// </summary>
    /// <summary>
    /// 들어설 때의 인사(<c>0x0044B4A0</c>) — <b>부관이 있으면 부관이 먼저 묻는다</b>.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0044b4c1  부관 「새로운 배라도 사십니까?」        0x00530F20
    ///   0044b4d7  아니면 「형씨, 바다에 나갈 거면 좋은 배를 사요.」 0x00530F38
    /// </code>
    /// 부관 쪽에는 관문이 하나 더 있다 — 함대가 이 도시에 닻을 내렸는가(<c>0x0040E1C0(도시, 0)</c>).
    /// 걸어 들어온 마을이면 부관이 있어도 조선공이 말한다.
    /// </remarks>
    public void Greet()
    {
        if (_game.AideFace is { } aide && _player.FleetHere(_cityId))
            TalkDialog.Say(_view, aide, "", "새로운 배라도 사십니까?");
        else
            ConfirmDialog.Tell(_view, "형씨, 바다에 나갈 거면 좋은 배를 사요.",
                               face: _game.SpeakerFace(BuildingCode, _culture));
    }

    /// <summary>
    /// 배를 산다. 게임의 <c>0x0044B5A0</c> 자리다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00422CA0  이 조선소가 파는 선체 — 없으면 "미안하지만, 우리집은 새로 만든 배는 취급하지 않네." 뒤 끝
    ///   0x00531068  "새로운 배가 갖고 싶나?"                           얼굴 창
    ///   0x00422DE0  「선체종류 선택」 — 중단이면 끝
    ///   0x005310B8  함대가 8척이면 "이 이상 배를 늘릴 수 없습니다!" 뒤 끝
    ///   0x0044B450  값 = 선체표 +0x38 x 1000 x 시세 / 100 (적어도 1)
    ///   0x005310D8  "%s%s 갖고 싶다면 금화 %ld닢이 필요하네."          얼굴 YES/NO — NO 면 표로
    ///   0x00531100  소지금이 모자라면 "자금이 모자랍니다!" 뒤 표로     돈은 YES 뒤에 본다
    ///   0x00422C10  AVI\S%02d_0001.AVI — 선체 번호(0~7) 동영상
    ///   0x0044B7B0  선명입력 → 배를 지어 함대에 붙인다. 끝 알림은 없다
    /// </code>
    /// </remarks>
    public void BuyShip()
    {
        var owner = Owner;
        var face = _game.SpeakerFace(BuildingCode, _culture);

        // 파는 선체는 도시마다 다르고 해가 가면 는다(0x00422CA0 · ShipyardStock). 등록해 넣은 배는 늘 판다.
        var hulls = SoldHulls();
        if (hulls.Count == 0)
        {
            Say(ShipyardStock.NoneWord);
            return;
        }

        Say("새로운 배가 갖고 싶나?");

        while (HullSelectDialog.Show(owner, hulls) is { } hull)
        {
            // 선체를 고른 뒤에 함대가 여기 있는지 본다(0x0044B63A) — 없으면 말하고 끝이다.
            if (!_player.FleetHere(_cityId))
            {
                Notice("함대가 정박해 있지 않는 마을에서는 배를 살 수 없습니다");
                return;
            }
            if (_player.Ships.Count >= Player.MaxShips)
            {
                Notice("이 이상 배를 늘릴 수 없습니다!");
                return;
            }

            int price = Math.Max(1, hull.Price * _rate / 100);
            string what = hull.Name;
            if (!Ask($"{what}{GameUi.Josa(what, "이", "가")} 갖고 싶다면 금화 {price}닢이 필요하네."))
                continue;

            if (!_player.CanAfford(price))
            {
                Notice("자금이 모자랍니다!");
                continue;
            }

            // 동영상은 넘겨받은 창을 가득 채운다 — 명령 창(작다)이 아니라 맨 위 게임 창을 덮는다.
            MoviePlayer.Play(GameUi.RootOf(owner), MovieOf(hull));

            string name = ShipNameDialog.Ask(owner, _player.SuggestShipName(), mustName: true)!;
            _player.Buy(hull, name, price);
            _menu.Refresh();
            return;
        }
    }

    /// <summary>
    /// 이 조선소가 지금 파는 선체. 도시 표를 못 읽으면 모두다.
    /// </summary>
    /// <remarks>
    /// 도시 레코드 <c>+0x1E</c> 의 여덟 비트를 다 훑으므로(<c>0x00422CA0</c>) 코구(0) ·
    /// 대형카락(4) · 다우(7) 도 그 도시에서는 실제로 판다 — 붙박이 다섯에 없으면
    /// <see cref="Hull.FromTable"/> 로 선체표 아래값을 세워 목록에 올린다.
    /// </remarks>
    private List<Hull> SoldHulls()
    {
        if (_game.CityRows is not { } cities) return [.. Hull.All];

        var sold = ShipyardStock.HullsAt(cities, _cityId, _player.Date);
        var list = Hull.All.Where(h => h.Id is < 0 or >= 8 || sold.Contains(h.Id)).ToList();

        // 붙박이에 없는 선체(코구·대형카락·다우)도 켜져 있으면 낸다.
        foreach (int id in sold)
            if (!list.Any(h => h.Id == id)) list.Add(Hull.FromTable(id));

        // 값이 비싼 쪽이 위다 — Hull.All 과 같은 차례로 다시 세운다.
        return [.. list.OrderByDescending(h => h.Price)];
    }

    /// <summary>
    /// 그 선체의 동영상 자리 — 올려 둔 것이 먼저, 없으면 게임 폴더 원본이다.
    /// 게임 선체가 아니면(등록해 넣은 배) null — 안 튼다.
    /// </summary>
    private string? MovieOf(Hull hull)
    {
        int n = MovieFiles.Hulls.IndexOf(hull.Name);
        return n < 0 ? null : MovieFiles.Resolve(_game.Directory, MovieFiles.HullStem(n));
    }

    /// <summary>고칠 배가 있는지. 없으면 게임처럼 "수리" 줄이 흐리다.</summary>
    public bool CanRepair => RepairTargets().Count > 0;

    /// <summary>
    /// 조선소에 배를 판다. 게임의 <c>0x0044B820</c> 자리다.
    /// </summary>
    /// <remarks>
    /// 값은 산 값의 <b>6할</b>에 도시 시세를 먹인 것이다(<see cref="Hull.SellPrice"/> ·
    /// <c>0x00423A30</c> → <c>0x00429DC0</c>). 배가 한 척뿐이면 줄 자체가 흐리고
    /// (<c>0x0044B863</c> 의 <c>cmp esi,1 / jle</c>), <b>기함은 못 판다</b>
    /// (<c>0x00531188</c> "기함을 처분하는 일은 불가능합니다!").
    ///
    /// 차례는 이렇다.
    /// <code>
    ///   0044B863  배가 한 척뿐이면 "기함을 처분하는 일은 불가능합니다!"
    ///             (없으면 "이 이상 배를 처분하는 일은 불가능합니다.")
    ///   0044B889  [배+0x64] 가 선 배는 값이 0 이다 — 기함이라 못 판다
    ///   0044B8B9  "어느 배를 팔 건가? 봐 주겠네."
    ///   0044B8DA  0x00423750 — 「매각선박의 선택」. 고른 것을 비트마스크로 낸다
    ///   0044B91C  "%ld닢입니다. 좋습니까?"  · YES 면 고른 배를 다 처분하고 값을 받는다
    /// </code>
    /// <b>여러 척을 한꺼번에 판다.</b> 그래서 목록 아래에 "견적합계" 가 붙는다.
    /// </remarks>
    public void SellShip()
    {
        var owner = Owner;

        // 배가 기함뿐이면 그 자리에서 물린다 — 목록도 안 뜬다.
        if (_player.Ships.Count <= 1)
        {
            ConfirmDialog.Tell(owner, _player.Ships.Count == 1
                ? "기함을 처분하는 일은 불가능합니다!"
                : "이 이상 배를 처분하는 일은 불가능합니다.");
            return;
        }

        Say("어느 배를 팔 건가? 봐 주겠네.");

        // 값이 0 이라 줄이 흐린 것은 <b>빌린 배</b>다(0x0044B889 의 배 +0x64) — 기함이 아니다.
        // 기함도 팔 수 있고, 배가 한 척뿐일 때만 위에서 막는다(0x0044B863).
        var rows = _player.Ships.Select((s, i) => new ShipSellDialog.Row(
            i, s.Name, s.Hull.Name,
            s.Figurehead >= 0 ? NameOf(s.Figurehead) : "---",
            s.Lent ? 0 : Shipyard.SellPrice(s, _rate))).ToList();

        var picked = ShipSellDialog.Ask(owner, rows);
        if (picked.Count == 0) return;

        int paid = picked.Sum(at => _player.Ships[at].Lent ? 0 : Shipyard.SellPrice(_player.Ships[at], _rate));
        if (!ConfirmDialog.Ask(owner, $"{paid}닢입니다. 좋습니까?")) return;

        // 뒤에서부터 뺀다 — 앞을 먼저 빼면 뒤 자리가 하나씩 밀린다.
        foreach (int at in picked.OrderByDescending(i => i)) _player.Scrap(at);
        _player.Earn(paid);

        // 배가 줄어 짐이 넘치면 짐 덜기 창이다(0x0044B968).
        CargoDropDialog.Force(owner, _game, _cityId);

        // 한 척만 남았으면 "매각" 줄이 그 자리에서 꺼져야 한다.
        _menu.Refresh();
    }

    /// <summary>
    /// 배를 고친다. 게임의 <c>0x0044B9C0</c> 자리다.
    /// </summary>
    /// <remarks>
    /// 고칠 배는 <b>함대와 이 마을에 맡겨 둔 배</b>를 다 훑어 모은다(<c>0x0044BC50</c>).
    /// 값은 이렇다.
    /// <code>
    ///   0x0044BBF0  손상 = (최대내구 - 지금내구) + (최대돛 - 지금돛)   ; 음수는 0
    ///   0x0044BAA1  값 = (rand(4) + 26) * 손상                        ; 26~29 곱
    ///   0x0044BABD  값 = 값 x 도시 시세 / 100                          ; 적어도 1
    /// </code>
    /// 우리 선체 표에는 돛 값이 없어 <b>내구만</b> 센다.
    ///
    /// <b>여러 척을 한꺼번에 고른다</b>(<c>0x0044BA62</c> 가 고름표를 낸다) — 손상을 다 더해
    /// <b>굴림 한 번</b>으로 값을 매긴다.
    /// </remarks>
    /// <summary>
    /// 이 마을에서 고칠 수 있는 배 — 함대 먼저, 그 뒤가 이 마을이 맡은 배다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044BC50(도시, 0)</c> 이다. 이 목록이 비면 조선소 차림표의 <b>"수리" 줄이
    /// 꺼진다</b>(<c>0x0044BD40</c> 이 <c>0x0044BC50 &gt; 0</c> 을 본다) — 그래서 평소에는
    /// "수리가 필요한 배는 없네!" 를 볼 일이 없다.
    /// </remarks>
    private List<(Ship Ship, bool Docked)> RepairTargets() =>
        Shipyard.RepairTargets(_player, _cityId);

    public void RepairShip()
    {
        var owner = Owner;
        var hurt = RepairTargets();
        if (hurt.Count == 0)
        {
            Say("수리가 필요한 배는 없네!");
            return;
        }

        var picked = ShipRepairDialog.Ask(owner,
            [.. hurt.Select((h, i) => new ShipRepairDialog.Row(
                i, h.Docked, h.Ship.Name, h.Ship.Hull.Name,
                h.Ship.Hp, h.Ship.MaxHp, h.Ship.RepairNeed))]);
        if (picked.Count == 0) return;

        // 손상을 다 더해 한 번만 굴린다(0x0044BA83 → 0x0044BAA1).
        int need = picked.Sum(at => hurt[at].Ship.RepairNeed);
        int cost = Shipyard.RepairCostOf(need, _rate, _random);
        if (!Ask($"수리하는데 금화 {cost}닢 필요하네. 좋나?")) return;

        if (!_player.Pay(cost))
        {
            Notice("소지금이 모자랍니다!");
            return;
        }
        foreach (int at in picked) hurt[at].Ship.Repair();

        // 마지막 상한 배를 고쳤으면 "수리" 줄이 그 자리에서 꺼져야 한다.
        _menu.Refresh();
    }

    // ── 개조 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 조선소 개조 — 배를 고르고, 그 배의 개조 창을 연다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x00496960</c>)은 배를 고른 뒤 <b>그 마을에서 손댈 수 있는 배인지</b>부터
    /// 본다 — 도시의 문화권(<c>0x004A1820</c>)이 0~2 나 10 이면 다우선(선체 7)을 못 고치고,
    /// 그 밖의 문화권에서는 <b>다우선만</b> 고친다. 못 고치면
    /// "이 배 형은 내가 어떻게 할 수 없다."(<c>0x00532338</c>) 를 내고 도로 고르게 한다.
    /// 우리 선체 다섯에는 다우선이 없으므로, 유럽권 밖(문화권 3~9)의 조선소에서는
    /// <b>어느 배도 못 고친다</b> — 원본 규칙 그대로다.
    ///
    /// 배를 고르면 열한 줄짜리 개조 창이 뜨고(<c>0x004966E0</c>), 한 줄을 마치면 게임은
    /// <b>그 줄만 꺼</b>(<c>0x0049690A</c>) 같은 배를 계속 손보게 둔다. 우리도 그렇게 한다 —
    /// 더 못 늘리는 줄은 저절로 흐려진다.
    /// </remarks>
    public void RefitShip()
    {
        var owner = Owner;

        // 함대가 이 마을에 없으면 「배가 없습니다」(0x005322D8)를 <b>얼굴 없이</b> 내고
        // 그대로 배 고르기로 넘어간다 — 돌아가지 않는다(0x00496994).
        if (!_player.FleetHere(_cityId) || _player.Ships.Count == 0)
            ConfirmDialog.Tell(owner, "배가 없습니다");

        Say("어느 배를 개조할 건가?");                       // 0x005322E8
        PickRefitShip();
    }

    /// <summary>
    /// 개조할 배를 고른다(<c>0x004969CE</c>) — 못 고치는 배면 말하고 다시 고르고(<c>0x00496A38</c>),
    /// 「개조를 그만둔다」로 나와도 여기로 돌아온다(<c>0x00496A36</c>). 「어느 배를…」은 다시 안 묻는다.
    /// </summary>
    private void PickRefitShip()
    {
        var owner = Owner;
        while (true)
        {
            int at = HintListDialog.Pick(owner,
                [.. _player.Ships.Select((s, i) => RefitLine(s, i == _player.Flagship))],
                "개조선박의 선택", "배가 없습니다", RefitHead);   // 0x00532300
            if (at < 0 || at >= _player.Ships.Count) return;

            // 그 마을에서 손댈 수 있는 배인지 본다(0x004969F9) — 유럽권(0·1·2·10)은 다우선을
            // 못 고치고, 그 밖의 문화권은 다우선만 고친다.
            if (!Shipyard.CanRefitHere(_player.Ships[at].Hull.Id, _culture))
            {
                Say("이 배 형은 내가 어떻게 할 수 없다.");        // 0x00532338
                continue;
            }

            Say("어디를 개조할 건가?");                          // 0x005322C0
            var ship = _player.Ships[at];
            _menu.Push(() => RefitMenu(ship));
            return;
        }
    }

    /// <summary>
    /// 배 한 척의 개조 창. 줄은 게임 열한 줄 그대로다.
    /// </summary>
    /// <remarks>
    /// <b>더 못 하는 줄도 처음에는 고를 수 있다</b> — 골라야 왜 안 되는지 말해 주고, 그
    /// 자리에서 줄이 꺼진다(<c>0x0049690A</c> 가 <c>*pInt == 0</c> 인 줄을 끈다).
    /// 예전에는 처음부터 흐리게 두어 그 말들을 볼 일이 없었다.
    /// </remarks>
    private GameMenu RefitMenu(Ship ship) => new(
        [.. Facility.RefitMenu.Select(item => (item, RefitAction(ship, item)))]);

    /// <summary>더 못 하는 줄을 골랐을 때의 말(<c>0x00531540</c> 벌). 할 수 있으면 빈 글이다.</summary>
    private static string RefitBlocked(Ship ship, string item) => item switch
    {
        Facility.RefitCapacity when !ship.CanGrowCapacity => "이 이상은 무리로군.",
        Facility.RefitTonnage when !ship.CanGrowTonnage => "이 이상 무리로군.",
        Facility.RefitReinforce when !ship.CanReinforce => "이미 한계다.",
        Facility.RefitMast when !ship.CanAddMast => "이 배는 이 이상 돛을 늘릴 수 없네.",
        Facility.RefitSailKind when !ship.CanChangeSail => "안됐지만, 이 타입은 돛의 종류를 바꿀 수 없네.",
        Facility.RefitSail when !ship.CanAddSail => "이 이상 돛을 단다면 마스트가 부러지네.",
        _ => "",
    };

    /// <summary>못 하는 줄이면 까닭을 말하고 참을 낸다.</summary>
    private bool Blocked(Ship ship, string item)
    {
        if (RefitBlocked(ship, item) is not { Length: > 0 } why) return false;
        Say(why);
        return true;
    }

    /// <summary>
    /// 줄의 손잡이 — 한 줄을 마치면 차림으로 돌아오며 「어디를 개조할 건가?」를 다시 묻는다(<c>0x0049682C</c> 가
    /// 되풀이 안에 있다). 「개조를 그만둔다」는 배 고르기로 돌아간다.
    /// </summary>
    private Action? RefitAction(Ship ship, string item)
    {
        if (item == Facility.RefitExit) return () => { _menu.Pop(); PickRefitShip(); };
        if (RefitWork(ship, item) is not { } work) return null;
        return () => { work(); Say("어디를 개조할 건가?"); };
    }

    private Action? RefitWork(Ship ship, string item) => item switch
    {
        Facility.RefitCapacity or Facility.RefitTonnage or Facility.RefitReinforce =>
            () => { if (!Blocked(ship, item)) DoRefit(ship, item); },
        Facility.RefitMast => () => { if (!Blocked(ship, item)) AddMast(ship); },
        Facility.RefitSailKind => () => { if (!Blocked(ship, item)) SwapSail(ship); },
        Facility.RefitSail => () => { if (!Blocked(ship, item)) AddSail(ship); },
        Facility.RefitTurrets => () => ChangeTurrets(ship),
        Facility.RefitCannon => () => BuyCannon(ship),
        Facility.RefitFigurehead => () => Carve(ship),
        // 빌린 배는 <b>이름을 못 바꾼다</b> — 게임이 그 줄을 흐리게 둔다(0x004967C2 가
        // 제독의 소유주 번호와 배 주인을 견준다). 이 줄만은 처음부터 흐리다.
        Facility.RefitRename when !ship.Lent => () => RenameShip(ship),
        _ => null,
    };

    /// <summary>
    /// 조선소가 갖춰 둔 선수상 — 어디나 둘, 문화권마다 한둘이 더 있다.
    /// </summary>
    private IReadOnlyList<int> Stock() => Figureheads.StockFor(_culture);

    /// <summary>지금 지니고 있는 선수상들 — 소지품에서 갈래 6 을 골라낸 것이다.</summary>
    /// <remarks>게임의 <c>0x00495BA0</c> 이 소지품을 훑어 같은 목록을 짓는다.</remarks>
    /// <remarks>원본은 갈래 6 인 칸을 <b>전부</b> 늘어놓는다 — 재고와 겹쳐도, 같은 것이 둘이어도 빼지 않는다.</remarks>
    private List<int> Carried() =>
        [.. _player.Items.Select(Figureheads.FromItem).Where(Figureheads.Known)];

    /// <summary>
    /// 선수상 — 조선소에서 사거나, 지니고 있는 것을 뱃머리에 단다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00495D10</c> 이 목록을 짓는다 — <b>조선소 재고</b>(<c>0x00429DF0</c>)와
    /// <b>내 소지품</b>(<c>0x00495BA0</c>)을 이어 붙인 것이다. 값은 어디서 온 것이냐에 따라
    /// 다르다.
    /// <code>
    ///   재고     아이템 구입값 x 시세 / 100        "금화 %1d닢이네."            0x00531DC0
    ///   소지품   0x0056E280[등급]                  "선두상을 단 값으로 …"       0x00531DD0
    ///   531bc0   "지금 붙어있는 선수상은 놓아 가고 가는가?"   이미 달았을 때
    ///   531cc0   "…저주받아 풀 수가 없네."                   저주는 못 뗀다
    ///   531d40   "이! 이 선수상은... 정말 이것을 달아도 좋단 말이지?"  저주받은 것을 달 때
    ///   531e08   "돈이 모자라는 것 같군."
    /// </code>
    /// 달고 있던 것은 놓고 가면 그 매각값을 도로 얹어 주고, 가지고 가겠다면 소지품에 넣는다
    /// (<c>0x00495CB6</c> 이 더하고 <c>0x00495CD3</c> 이 새 값을 뺀다).
    /// </remarks>
    private void Carve(Ship ship)
    {
        var owner = Owner;

        // 재고가 앞, 지닌 것이 뒤다 — 게임도 그 차례로 잇는다.
        var stock = Stock();
        var carried = Carried();
        var offer = new List<int>(stock);
        offer.AddRange(carried);
        if (offer.Count == 0) return;

        Say("어느 상을 달겠나?");                                   // 0x00531C10
        int at = HintListDialog.Pick(owner,
            [.. offer.Select((i, k) => $"{NameOf(i),-12}{CostOf(i, k < stock.Count),7}닢")],
            "선수상 선택", "달 수 있는 선수상이 없습니다", CarveHead);
        if (at < 0) return;

        int pick = offer[at];
        bool buying = at < stock.Count;
        int cost = CostOf(pick, buying);

        // <b>등급이 내려갈 때만</b> 한 번 말린다(0x00495EA6 — 옛 등급 &gt; 새 등급).
        // 사신·마왕은 등급 0 이라 그것으로 바꿀 때는 늘 뜨고, 그것에서 바꿀 때는 안 뜬다.
        if (ship.Figurehead >= 0
            && Figureheads.GradeOf(ship.Figurehead) > Figureheads.GradeOf(pick)
            && !Ask("지금 달려있는 쪽이 더 좋다고 생각하는데... 그래도 바꾸겠나?"))
            return;

        // 저주받은 것을 달고 있으면 <b>그 짝만</b> 갈아 낼 수 있다(0x00495ED4).
        if (Figureheads.Cursed(ship.Figurehead))
        {
            if (pick != Figureheads.CureFor(ship.Figurehead))
            {
                Say("안됐지만, 자네가 지금 달고 있는 선수상은 저주받아 풀 수가 없네. "
                  + Figureheads.CureHint(ship.Figurehead));
                return;
            }
            // 원본 글은 「[%s의 선두상] 의」이고 %s 는 짧은 이름이다(0x00531C68 · 표 0x0054A0A0). 낱말은 선수상으로 둔다.
            Say($"이 선수상이라면 자네가 지금 달고 있는 [{Figureheads.ShortName(ship.Figurehead)}의 선수상] 의 저주도 푸는 것이 가능하다네.");
        }

        // 저주받은 것을 달겠다고 하면 한 번 더 묻고(0x00495F54), 무르면 물러선다.
        if (Figureheads.Cursed(pick))
        {
            if (!Ask("이! 이 선수상은... 이보게, 정말 이것을 달아도 좋단 말이지?"))
            {
                Say("놀랄 걸세...");                                 // 0x00531DB0
                return;
            }
            Say(".....자네가 무슨 일을 하든 나와는 상관없네.");        // 0x00531D80
        }

        Say(buying
            ? $"금화 {cost}닢이네."
            : $"선수상을 단 값으로 금화 {cost}닢 받겠네.");
        if (!_player.CanAfford(cost)) { Say("돈이 모자라는 것 같군."); return; }

        // 마지막으로 한 번 더 묻는다(0x00496017).
        if (!Ask("이것을 달겠네.")) return;

        // 다는 것은 0x00495C10 이다. 지닌 것을 달면 먼저 소지품에서 던다(0x00495C29).
        if (!buying) _player.Drop(Figureheads.ToItem(pick));

        // 달고 있던 것을 놓고 갈지는 <b>맨 마지막</b>에 묻는다(0x00495C40). 「아니오」면 <b>가지고 간다</b> —
        // 소지품에 넣고(0x004B1710(…, 1, 1), 넘치면 버리기 창을 물릴 수 있다), 못 넣었거나 「예」면
        // 팔아 주며 「자, 금화 %ld닢으로 해 주겠네.」(0x00531BF0)라고 한다.
        if (ship.Figurehead >= 0)
        {
            bool leave = Ask("지금 붙어있는 선수상은 놓아 가고 가는가?");
            if (leave || !ItemGain.TryAdd(Owner, _game, Figureheads.ToItem(ship.Figurehead)))
            {
                int back = SellBack(ship.Figurehead);
                Say($"자, 금화 {back}닢으로 해 주겠네.");
                _player.Earn(back);
            }
        }

        ship.Carve(pick);
        _player.Pay(cost);
        _menu.Pop();
        _menu.Push(() => RefitMenu(ship));
    }

    /// <summary>
    /// 다는 값. 조선소에서 사면 구입값에 시세를 먹이고, 지닌 것을 달면 등급이 삯을 정한다.
    /// </summary>
    private int CostOf(int index, bool buying) => buying
        ? Math.Max(1, (_game.Items?.Find(Figureheads.ToItem(index))?.BuyList ?? 0) * _rate / 100)
        : Figureheads.PriceOf(index);

    /// <summary>
    /// 놓고 가는 선수상을 팔아 주는 값 — 매각값에 시세를 먹인다. 정가가 있으면 최소 1 이다
    /// (<c>0x00495C92</c> 가 <c>0x00429DC0</c> 을 탄다).
    /// </summary>
    private int SellBack(int index)
    {
        int list = _game.Items?.Find(Figureheads.ToItem(index))?.SellList ?? 0;
        return list <= 0 ? 0 : Math.Max(1, list * _rate / 100);
    }

    /// <summary>선수상 이름 — 아이템 표에서 낸다(213 송골매상 …).</summary>
    private string NameOf(int index) =>
        _game.Items?.Find(Figureheads.ToItem(index))?.Name ?? $"선수상 {index}";

    /// <summary>
    /// 마스트 추가 — 돛대를 하나 더 세운다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494BD0</c> 이다.
    /// <code>
    /// 494a50  코구·다우는 못 늘리고, 카라벨은 둘까지, 그 밖은 셋까지
    /// 494c2c  값 = 선체 구입값 / 5
    /// 494c7b  "적재용량이 조금 주는데 괜찮나?"
    /// 494c9a  카라벨·대형카라벨은 "마스트에는 삼각돛을 달겠네." — 고를 것 없이 삼각돛이다
    /// 494cc5  그 밖은 "마스트에 달 돛의 종류를 정해 주게." → 삼각 · 사각 · 그만둔다
    /// 494b52  적재용량 -= 25 · 필요승원 += 2
    /// </code>
    /// </remarks>
    private void AddMast(Ship ship)
    {
        var owner = Owner;
        int cost = Shipyard.MastCost(ship, _rate);

        Say($"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { Say("돈이 모자라는 것 같군."); return; }
        if (!Ask("적재용량이 조금 주는데 괜찮나?")) return;

        int sail;
        if (!ship.CanChangeSail)
        {
            if (!Ask("마스트에는 삼각돛을 달겠네.")) return;
            sail = Ship.Lateen;
        }
        else
        {
            // 돛을 고르고 「아니오」면 다시 고르게 한다(0x00494D50 → 0x00494CC5). 목록 끝에 「그만둔다」가 있다(0x005315E8).
            while (true)
            {
                Say("마스트에 달 돛의 종류를 정해 주게.");
                int at = HintListDialog.Pick(owner,
                    [Ship.SailNames[Ship.Lateen], Ship.SailNames[Ship.Square], "그만둔다"], "돛 종류", "");
                if (at < 0 || at == 2) return;
                sail = at == 0 ? Ship.Lateen : Ship.Square;
                if (Ask(sail == Ship.Lateen
                        ? "이것은 역풍에 뛰어나네. 이 돛을 달겠네?"
                        : "이것은 순풍에 뛰어나네. 이 돛을 달겠네?")) break;
            }
        }

        var was = ship.Snapshot();
        _player.Pay(cost);

        int mast = ship.AddMast(sail);
        if (mast < 0) return;

        string where = Ship.MastNames[mast], what = Ship.SailNames[sail];
        NoticeDialog.Show(owner, $"{where}에 {what}{GameUi.Josa(what, "을", "를")} 달았습니다");
        ShowRefit(owner, Refit.Mast(was, ship.Snapshot()), ship);
    }

    /// <summary>
    /// 돛종류 변경 — 마스트 하나의 돛을 삼각↔사각으로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00494F10</c> 이다. 값은 <b>선체 구입값 / 20</b>(<c>0x004950CB</c>).
    /// <code>
    ///   0x005316A8  "어느 마스트의 돛을 바꿀건가?"
    ///   0x005316D8  "삼각돛을 순풍에 뛰어난 사각돛으로 바꿀 건가?"
    ///   0x00531708  "사각돛을 역풍에 뛰어난 삼각돛으로 바꿀 건가?"
    ///   0x00531738  "금화 %ld닢이 드는데, 좋나?"
    ///   0x00531660  "%s%s %s%s 변경했습니다"
    /// </code>
    /// </remarks>
    private void SwapSail(Ship ship)
    {
        var owner = Owner;

        // 목록에 나오는 마스트 수는 <b>돛이 달린 가장 높은 마스트</b>다(0x00422CE0) — 그것이
        // 하나뿐이면 묻지도 늘어놓지도 않고 메인마스트를 곧장 바꿀지 묻는다(0x00494F7D).
        int masts = 0;
        for (int i = 0; i < Ship.MastSlots; i++) if (ship.Sails[i] != Ship.NoSail) masts = i + 1;
        if (masts == 0) return;

        // 여럿이면 아니오·돈 부족·바꾼 뒤에도 마스트 목록으로 돌아간다(jmp 0x00494F6B) — 물러야 나온다.
        bool single = masts <= 1;
        while (true)
        {
            int mast;
            if (single) mast = 0;
            else
            {
                // 줄은 <b>늘 셋</b>이다 — 돛이 없는 마스트도 「없음」으로 나온다(0x00494FA5 의
                // 되돌이가 0x0056E260 의 세 이름을 다 돈다). 끝에 「그만둔다」가 붙는다(0x005316C8).
                Say("어느 마스트의 돛을 바꿀건가?");
                List<string> rows =
                [
                    .. Enumerable.Range(0, Ship.MastSlots)
                                 .Select(i => $"{GameUi.Pad(Ship.MastNames[i], 14)}{Ship.SailNames[ship.Sails[i]]}"),
                    "그만둔다",
                ];
                int pick = HintListDialog.Pick(owner, rows, "돛종류 변경", "");
                if (pick < 0 || pick >= Ship.MastSlots) break;
                mast = pick;
            }

            // 물음은 <b>삼각돛일 때만</b> 「삼각→사각」이다 — 돛이 없어도 「사각→삼각」을
            // 묻고는 사각돛을 단다(0x0049507C 와 0x00495100 이 어긋난 채다).
            bool lateen = ship.Sails[mast] == Ship.Lateen;
            int cost = Shipyard.SailCost(ship, _rate);
            if (Ask(lateen
                    ? "삼각돛을 순풍에 뛰어난 사각돛으로 바꿀 건가?"
                    : "사각돛을 역풍에 뛰어난 삼각돛으로 바꿀 건가?")
                && Ask($"금화 {cost}닢이 드는데, 좋나?"))
            {
                if (!_player.Pay(cost)) Say("돈이 모자라는 것 같군.");
                else if (ship.SwapSail(mast))
                {
                    string where = Ship.MastNames[mast], what = Ship.SailNames[ship.Sails[mast]];
                    NoticeDialog.Show(owner,
                        $"{where}{GameUi.Josa(where, "을", "를")} {what}{GameUi.Josa(what, "으로", "로")} 변경했습니다");
                }
            }
            if (single) break;
        }
        _menu.Refresh();
    }

    /// <summary>
    /// 돛 추가 — 추진력을 올리고 그만큼 배가 여려진다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00495320</c> 이다. 값은 <b>선체 구입값 / 20</b>.
    /// <code>
    ///   0x005317D8  "이 이상 돛을 단다면 마스트가 부러지네."
    ///   0x00531800  "금화 %ld닢이 드네."
    ///   0x00531818  "마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만, 괜찮겠나?"
    /// </code>
    /// </remarks>
    private void AddSail(Ship ship)
    {
        var owner = Owner;
        int cost = Shipyard.SailCost(ship, _rate);

        Say($"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { Say("돈이 모자라는 것 같군."); return; }
        if (!Ask("마스트에 부담이 되어 배가 조그마한 충격에도 약해지지만, 괜찮겠나?")) return;

        _player.Pay(cost);
        ShowRefit(owner, ship.AddSail(), ship);
    }

    /// <summary>
    /// 포탑수변경 — 대포를 걸 자리를 늘리거나 줄인다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00496190</c> 이다.
    /// <code>
    /// 4961d6  상한 = min(선체 표 +0x30, 지금 포탑 + 적재용량)
    /// 496213  0x00454AA0("포탑수 결정", "포탑수", "문", 상한, "최대포탑수", "현재의 포탑수")
    /// 49621d  그대로면 "자네와 장난칠 여유없네."          0x00531F10
    /// 496234  값 = (새 - 지금) x 5 x 5 x 8 = 200 x 늘린 수
    /// 49624a  "금화 %ld닢 받겠네."                        0x00531F28
    /// 49625c  줄일 때는 "뗄 거라면 돈은 필요없네."         0x00531F40
    /// 4960d4  넘치는 대포는 "실을 수 없게 된 대포는…"      0x00531E20
    /// </code>
    /// </remarks>
    private void ChangeTurrets(Ship ship)
    {
        var owner = Owner;
        Say("포탑은 몇 개로 할건가?");

        int want = CountDialog.Ask(owner, "포탑수 결정", "포탑수", "문", ship.MaxTurrets, 1, true,
            new CountDialog.Gauge("최대포탑수", ship.MaxTurrets),
            new CountDialog.Gauge("현재의 포탑수", ship.Turrets));
        if (want < 0) return;
        if (want == ship.Turrets) { Say("자네와 장난칠 여유없네."); return; }

        int cost = Math.Max(0, want - ship.Turrets) * Cannon.TurretPrice;
        Say(cost > 0 ? $"금화 {cost}닢 받겠네." : "뗄 거라면 돈은 필요없네.");
        if (!_player.CanAfford(cost)) { Say("돈이 모자라네."); return; }
        if (!Ask("괜찮겠나?")) return;

        var was = ship.Snapshot();
        var gun = Cannon.Of(ship.Gun);
        _player.Pay(cost);

        int spilled = ship.SetTurrets(want);
        if (spilled > 0 && gun != null)
        {
            // 포탑을 줄여 <b>못 싣게 된</b> 대포다 — 대포 갈래를 바꿀 때의 말(0x00531F80)과
            // 다른 글이다(0x004960D4).
            Say("실을 수 없게 된 대포는 가격의 30프로로 사 주겠네.");
            _player.Earn(gun.Price * spilled * Cannon.BuyBackPercent / 100);
        }

        ShowRefit(owner, Refit.Turrets(was, ship.Snapshot()), ship);
    }

    /// <summary>
    /// 대포구입 — 포탑에 걸 대포를 골라 싣는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004963E0</c> 이다.
    /// <code>
    /// 49643e  포탑이 0 이면 "포탑이 없으면 대포는 실을 수 없네."   0x005320E8
    /// 496473  "어느 대포를 실을 건가?"                            0x00532110
    /// 4964ff  남는 무게 = 적재중량 - 실은무게 + 지금 대포 무게      ; 다 내렸다 치고 잰다
    /// 496520  실을 수 있는 문수 = min(포탑수, 남는무게 / 대포중량)
    /// 496532  같은 대포를 이미 다 실었으면 "이 대포는 더 이상 실을 수 없네."  0x00532128
    /// 49654b  단가보다 돈이 적으면 "돈이 모자라는군."               0x00532178
    /// 4965b7  "실을 수 있을 만큼 싣겠네."(예/아니오) → 아니면 "얼마나 싣겠나?"
    /// 4965f8  "%s%s %d문 실으면 금화 %d닢이네. 좋은가?"            0x005321F0
    /// </code>
    /// 갈래를 바꿔 실으면 실려 있던 것은 <b>30프로</b>로 되사 준다. 파는 대포는 마을마다 다르다(<see cref="CannonsSold"/>).
    /// </remarks>
    private void BuyCannon(Ship ship)
    {
        var owner = Owner;
        if (ship.Turrets <= 0) { Say("포탑이 없으면 대포는 실을 수 없네."); return; }

        // 이 마을이 파는 대포만 늘어놓는다(0x00443FD0 → 0x00429F30).
        var sold = CannonsSold();

        // 「어느 대포를 실을 건가?」는 <b>한 번만</b> 묻는다 — 막히거나 물리면 돌아가는 자리는
        // 그 말이 아니라 목록 그 자체다(0x00496473 뒤 0x004966A5 → 0x00496489).
        Say("어느 대포를 실을 건가?");
        while (true)
        {
            int pick = HintListDialog.Pick(owner,
                [.. sold.Select(i => Cannon.All[i]).Select(c => $"{GameUi.Pad(c.Name, 12)}{c.Price,6}닢{c.Weight,5}")],
                "대포 선택", "대포가 없네.", GunHead);
            if (pick < 0 || pick >= sold.Count) return;
            int at = sold[pick];

            var gun = Cannon.All[at];
            // 무게 한도는 <b>그 배의 적재중량 그대로</b>다 — 짐은 안 본다
            // (0x004964FF 가 0x0044C8B0(배) = 적재중량 − 실은 대포 무게 에 지금 대포 무게를
            //  도로 더한다). 함대 남는 중량이 아니다.
            int free = ship.Tonnage;
            int room = ship.RoomFor(at, free);
            bool same = at == ship.Gun && ship.Guns > 0;
            if (same) room -= ship.Guns;

            if (room <= 0)
            {
                Say(same ? "이 대포는 더 이상 실을 수 없네." : "이 대포는 무거워서 실을 수 없네.");
                continue;
            }
            if (!_player.CanAfford(gun.Price)) { Say("돈이 모자라는군."); continue; }

            // 같은 갈래를 이미 실었으면 대포 설명은 안 한다(0x00496557 → 0x004965B7).
            if (!same) Say(gun.Word);
            room = Math.Min(room, _player.Gold / gun.Price);

            int want;
            if (Ask("실을 수 있을 만큼 싣겠네.")) want = room;
            else
            {
                // 「얼마나 싣겠나?」는 말로 하고, 수 적기 창의 제목은 대포 이름이다(0x004965D2~0x00496617).
                Say("얼마나 싣겠나?");
                want = CountDialog.Ask(owner, gun.Name, "대포수", "문", room, 1, true,
                    new CountDialog.Gauge("최대대포수", room),
                    new CountDialog.Gauge("현재의 포수", same ? ship.Guns : 0));
            }
            if (want <= 0) continue;

            int cost = gun.Price * want;
            string who = gun.Name;
            if (!Ask($"{who}{GameUi.Josa(who, "을", "를")} {want}문 실으면 금화 {cost}닢이네. 좋은가?"))
                continue;
            if (!_player.Pay(cost)) { Say("돈이 모자라네."); continue; }

            var was = ship.Snapshot();

            // 갈래가 갈리면 실려 있던 것은 30프로로 되사 준다.
            if (at != ship.Gun && Cannon.Of(ship.Gun) is { } old && ship.Guns > 0)
            {
                // 0x0049632E. <b>얼마를 받았는지는 안 알린다</b> — 0x00531E58
                // 「금화 %ld닢을 벌었습니다.」는 EXE 에 있기만 하고 아무도 안 가리키는 죽은 글이다.
                Say("지금 싣고 있는 것은 가격의 30프로로 사 주겠네.");
                _player.Earn(old.Price * ship.Guns * Cannon.BuyBackPercent / 100);
                ship.Load(at, want);
            }
            else
            {
                ship.Load(at, ship.Guns + want);
            }

            ShowRefit(owner, Refit.Guns(was, ship.Snapshot()), ship);
            return;
        }
    }

    /// <summary>
    /// 이 마을 조선소가 파는 대포(<c>0x00429F30</c>) — 세이커포는 어디서나, 둘째는 규모 3 이상, 셋째는 4 이상,
    /// 넷째는 1490년부터 도시 0·7 · 1500년부터 도시 38 · 1510년부터 도시 15 에서만 판다.
    /// </summary>
    private List<int> CannonsSold()
    {
        var sold = new List<int> { 0 };
        int scale = _game.CityRows?.ScaleOf(_cityId) ?? 0;
        if (scale >= 3) sold.Add(1);
        if (scale >= 4) sold.Add(2);
        int year = _player.Date.Year;
        if ((year >= 1490 && _cityId is 0 or 7) || (year >= 1500 && _cityId == 38) || (year >= 1510 && _cityId == 15))
            sold.Add(3);
        return sold;
    }

    /// <summary>개조 결과 상자를 띄우고 개조 창을 다시 짓는다.</summary>
    private void ShowRefit(Window owner, Refit change, Ship ship)
    {
        if (change.Any)
            NoticeDialog.Show(owner, string.Join(Environment.NewLine,
                change.Lines.Select(l => $"{GameUi.Pad(l.Name, 12)}{l.Before,4} → {l.After,4}{l.Unit}")));

        // 결과 상자 뒤에 짐이 넘치면 짐 덜기 창이다 — 마스트·보강·포탑수·대포(0x00494BA1 · 0x00495A95 ·
        // 0x00496168 · 0x004963AF). 실을 곳을 늘리는 개조는 넘칠 일이 없어 모두에 걸어도 같다.
        CargoDropDialog.Force(owner, _game, _cityId);

        _menu.Pop();
        _menu.Push(() => RefitMenu(ship));
    }

    /// <summary>
    /// 선명변경 — 배 이름을 바꾼다. 값은 안 든다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x00495B90</c> → <c>0x00423BE0</c> 으로 <b>선명입력</b> 창을 띄운다.
    /// 미리 갖춰 둔 이름 스물하나가 먼저 뜨고(포인터 표 <c>0x0053C178</c>), 오른쪽 위 작은
    /// 단추를 누르면 글자판이 떠서 하나씩 찍어 지을 수 있다 —
    /// <see cref="ShipNameDialog"/> · <see cref="TextInputDialog"/> 가 그 둘이다.
    ///
    /// 게임은 배를 <b>살 때도</b> 같은 창으로 이름을 받는데 우리 조선소 구입은 아직 안 묻는다 —
    /// 그때는 안 쓴 이름을 하나 집어 준다(<c>Player.SuggestShipName</c>).
    /// </remarks>
    /// <remarks>
    /// 곧장 입력 창이다. 「배의 이름을 정해 주십시오」(<c>0x00531478</c>)는 <b>빈 이름으로 결정했을 때만</b>
    /// 얼굴 없이 내고 창을 다시 띄운다. 바꾸고 나서 알리는 말은 없다.
    /// </remarks>
    private void RenameShip(Ship ship)
    {
        var owner = Owner;
        while (ShipNameDialog.Ask(owner, ship.Name) is { } name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Notice("배의 이름을 정해 주십시오");
                continue;
            }
            if (name != ship.Name) ship.Rename(name);
            break;
        }
        _menu.Refresh();
    }

    /// <summary>
    /// 개조 한 줄을 치른다 — 값을 알리고, 물어보고, 고치고, 바뀐 값을 보여 준다.
    /// </summary>
    /// <remarks>
    /// 차례와 문구는 게임 것 그대로다(<c>0x004955D0</c> 벌).
    /// <code>
    ///   0x00531938  "금화 %ld닢이 드네."
    ///   0x005319A8  "돈이 모자라는 것 같군."
    ///   0x00531950  "용량과 함께 적재용량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?"
    ///   0x00531920  "이 이상은 무리로군."
    /// </code>
    /// 게임처럼 <b>돈 검사를 물어보기 앞</b>에 한다 — 시장 구입과는 차례가 반대다.
    /// </remarks>
    private void DoRefit(Ship ship, string item)
    {
        var owner = Owner;
        int cost = Shipyard.RefitCost(ship, _rate);

        Say($"금화 {cost}닢이 드네.");
        if (!_player.CanAfford(cost)) { Say("돈이 모자라는 것 같군."); return; }
        if (!Ask(Shipyard.RefitWarning(item))) return;

        _player.Pay(cost);
        var change = item switch
        {
            Facility.RefitTonnage => ship.GrowTonnage(),
            Facility.RefitReinforce => ship.Reinforce(),
            _ => ship.GrowCapacity(),
        };

        // 게임이 개조 뒤에 띄우는 "%-12s%4d → %4d" 상자. 배가 바뀌었으니 줄의 흐림도 다시 잡는다.
        ShowRefit(owner, change, ship);
    }

    /// <summary>
    /// 배 한 척을 줄로 적는다 — 이름과 내구·추진·적재를 붙인다. 상했으면 내구를 "지금/최대"로 낸다.
    /// </summary>
    /// <summary>
    /// 개조 목록의 머리글 — 게임 낱말 그대로다(<c>0x00545580</c> 벌: 선명 · 추진력 ·
    /// 「대포명  포문수」 · 돛종류).
    /// </summary>
    private const string RefitHead = "        선명  추진력      대포명  포문수  돛종류";

    /// <summary>
    /// 선수상 고르는 목록의 머리글(<c>0x00532380</c>, <c>0x00443D5D</c>).
    /// </summary>
    /// <remarks>
    /// EXE 글은 " 선두상 명    가격" 인데, 개조 줄 이름과 마찬가지로 화면에서 본 대로
    /// <b>선수상</b> 으로 적는다(<see cref="Engine.Models.Facility.RefitFigurehead"/> 참고).
    /// 안 쓰이는 벌 <c>0x0053C078</c> 도 "선수상     가격" 으로 적어 두고 있다.
    /// </remarks>
    private const string CarveHead = " 선수상 명    가격";

    /// <summary>대포 고르는 목록의 머리글(<c>0x005323A8</c>, <c>0x004441FD</c>).</summary>
    private const string GunHead = "    대포명   단가   중량";

    /// <summary>돛 한 자리를 글자로 — 없음 <c>＿</c> · 삼각 <c>△</c> · 사각 <c>□</c>(0x005455F0 벌).</summary>
    private static string SailMark(int sail) => sail switch
    {
        Ship.Lateen => "△",
        Ship.Square => "□",
        _ => "＿",
    };

    /// <summary>
    /// 개조 목록 한 줄 — 게임 칸 그대로다. 대포 칸은 <c>%12s %2d/%2d문</c>(<c>0x005455D8</c>)
    /// 이고, 안 실었으면 통째로 빈 칸이다.
    /// </summary>
    internal static string RefitLine(Ship ship, bool flag)
    {
        string gun = ship.Gun >= 0 && ship.Gun < Cannon.Count
            ? $"{GameUi.Pad(Cannon.All[ship.Gun].Name, 12)} {ship.Guns,2}/{ship.Turrets,2}문"
            : new string(' ', 19);
        string sails = string.Concat(ship.Sails.Select(SailMark));

        return $"{(flag ? "*" : " ")}{GameUi.Pad(ship.Name, 10)}"
             + $" {ship.MaxSpeed,3}/{ship.Hull.SpeedCeiling,3}"
             + $"  {gun}  {sails}";
    }

    internal static string ShipLine(Ship ship, bool flag)
    {
        string hp = ship.NeedsRepair ? $"{ship.Hp,3}/{ship.MaxHp,-3}" : $"{ship.MaxHp,3}    ";
        // 추진력도 상했으면 내구처럼 지금/최대로 낸다.
        string go = ship.Speed < ship.MaxSpeed ? $"{ship.Speed,3}/{ship.MaxSpeed,-3}" : $"{ship.MaxSpeed,3}    ";
        return $"{(flag ? "*" : " ")}{ship.Name}  내구{hp} 추진{go} 적재{ship.UsableCapacity,4}";
    }
}
