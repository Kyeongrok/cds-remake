using CdsHelper.Game.Engine.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 도시에서 할 수 있는 일 하나. 시설 명령 창의 줄 하나가 곧 이것이다.
/// </summary>
/// <remarks>
/// 이름이 아니라 <b>일</b>로 가린다. 같은 이름이 자리마다 딴 일인 것이 있고("구입" 은
/// 조선소에서 배, 시장에서 교역품이다), 같은 일이 여러 자리에 나오는 것도 있다
/// (수련은 조합·교회·학자 저택, 부하편성은 여관·술집, 기능은 자택·여관).
/// </remarks>
public enum TownWork
{
    /// <summary>표에 없는 줄. 아직 흉내내지 않아 흐리게 두는 것도 이것이다.</summary>
    None,

    /// <summary>그 시설을 나온다. 문구는 시설마다 다르다(<see cref="Facility.ExitItem"/>).</summary>
    Exit,

    // ── 항구 ──────────────────────────────────────────────────────────────
    Sail, Supply, FleetForm, CrewForm, CityInfo, Announce,

    // ── 조선소 ────────────────────────────────────────────────────────────
    BuyShip, SellShip, RepairShip, RefitShip,

    // ── 시장 · 교역소 ─────────────────────────────────────────────────────
    BuyGoods, SellGoods, Trade, Talk,

    // ── 여관 · 술집 ───────────────────────────────────────────────────────
    Stay, OddJob, MateForm, Treat,

    // ── 자택 ──────────────────────────────────────────────────────────────
    Rest, Store, Savings, Heir, Educate, Succeed, Encyclopedia, Chronicle, Retire, BribeInspector, BorrowShips,

    // ── 자리를 가리지 않는 것들 ───────────────────────────────────────────

    /// <summary>기술을 배운다. 건물 표의 가르침 비트가 선 자리면 어디든 나온다.</summary>
    Train,

    /// <summary>저장·로드·게임 종료. 자택과 여관에 있다.</summary>
    System,

    /// <summary>후원자가 앉은 자리에 붙는 세 줄. 왕궁만이 아니라 총독부·상관·저택도 그렇다.</summary>
    Persuade, Report, BreakContract,

    // ── 그 밖 ─────────────────────────────────────────────────────────────
    Read, Explore,

    /// <summary>발견한 건물의 <b>해설</b>. 발견하고 나서야 줄이 붙는다.</summary>
    Comment,

    /// <summary>술집의 <b>정보를 듣는다</b>. 계약을 맺고 있을 때만 줄이 붙는다.</summary>
    Info,
}

/// <summary>일 하나가 <b>어느 자리에 무슨 이름으로</b> 나오는지.</summary>
/// <param name="Work">하는 일.</param>
/// <param name="Name">명령 창에 뜨는 줄 이름. 게임 문구 그대로다.</param>
/// <param name="At">그 이름으로 이 일이 나오는 시설들.</param>
public sealed record TownCommand(TownWork Work, string Name, params FacilityKind[] At);

/// <summary>
/// 도시의 일 표 — 어느 시설의 어느 줄이 무슨 일인지.
/// </summary>
/// <remarks>
/// 줄의 <b>차례와 문구</b>는 <see cref="Facility.All"/> 이 든다(게임 EXE 에서 읽어 온 것이라
/// 그쪽이 근거다). 이 표는 그 줄이 <b>무슨 일인지</b>와 <b>어느 자리에 나오는지</b>를 든다.
/// 화면은 일마다 손을 달아 주면 된다 — 손이 안 달린 일은 줄이 흐리게 나온다.
///
/// 술집 앞 세 줄(와인·브랜디·럼주)은 여기 없다. 그것은 일이 아니라 <b>그 도시가 파는 술</b>
/// 이라 도시마다 문구가 갈린다 — 게임도 술 표(<c>0x4FF978</c>)에서 도시 번호로 골라 붙인다.
/// </remarks>
public static class TownWorks
{
    public static readonly TownCommand[] All =
    [
        // ── 항구 ──────────────────────────────────────────────────────────
        new(TownWork.Sail, "출항", FacilityKind.Harbor),
        new(TownWork.Supply, "보급", FacilityKind.Harbor),
        new(TownWork.FleetForm, "함대편성", FacilityKind.Harbor),
        new(TownWork.CrewForm, "선원편성", FacilityKind.Harbor),
        new(TownWork.CityInfo, "마을정보", FacilityKind.Harbor),
        new(TownWork.Announce, Facility.Announce, FacilityKind.Harbor),

        // ── 조선소 ────────────────────────────────────────────────────────
        // "구입" · "매각" 은 시장에도 있는 이름이다. 그래서 자리로 가린다.
        new(TownWork.BuyShip, "구입", FacilityKind.Shipyard),
        new(TownWork.SellShip, "매각", FacilityKind.Shipyard),
        new(TownWork.RepairShip, "수리", FacilityKind.Shipyard),
        new(TownWork.RefitShip, "개조", FacilityKind.Shipyard),

        // ── 시장 · 교역소 ─────────────────────────────────────────────────
        new(TownWork.BuyGoods, "구입", FacilityKind.Market),
        new(TownWork.SellGoods, "매각", FacilityKind.Market),
        new(TownWork.Trade, "매매", FacilityKind.TradingPost),
        new(TownWork.Talk, "회화", FacilityKind.TradingPost),

        // ── 여관 · 술집 ───────────────────────────────────────────────────
        new(TownWork.Stay, "숙박", FacilityKind.Inn),
        new(TownWork.OddJob, "허드렛일", FacilityKind.Inn),
        new(TownWork.MateForm, "부하편성", FacilityKind.Inn, FacilityKind.Tavern),
        new(TownWork.Treat, "포카를 권한다", FacilityKind.Tavern),
        new(TownWork.Info, "정보를 듣는다", FacilityKind.Tavern),

        // ── 자택 ──────────────────────────────────────────────────────────
        new(TownWork.Rest, "휴양", FacilityKind.Home),
        new(TownWork.Store, "보관", FacilityKind.Home),
        new(TownWork.Savings, "저금", FacilityKind.Home),
        new(TownWork.Heir, "후손을 남긴다", FacilityKind.Home),
        new(TownWork.Educate, "교육", FacilityKind.Home),
        new(TownWork.Succeed, "세대교체", FacilityKind.Home),
        new(TownWork.Encyclopedia, "백과사전을 본다", FacilityKind.Home),
        new(TownWork.Chronicle, "연표를 본다", FacilityKind.Home),
        new(TownWork.Retire, "은퇴한다", FacilityKind.Home),

        // ── 그 밖 ─────────────────────────────────────────────────────────
        new(TownWork.Read, "열람", FacilityKind.Library),
        new(TownWork.Explore, "탐험을 떠난다", FacilityKind.Gate),
        // 자리를 안 적는다 — 발견물이 된 건물이면 어느 시설에나 붙는다(0x004733B1).
        new(TownWork.Comment, "해설"),
        new(TownWork.System, "기능", FacilityKind.Home, FacilityKind.Inn),

        // 수련은 조합·교회에 줄이 박혀 있고, 학자 저택처럼 줄에 없는 건물에도 건물 표의
        // 가르침 비트가 서 있으면 맨 앞에 붙는다. 그래서 자리는 <see cref="Teaches"/> 가 본다.
        new(TownWork.Train, "수련",
            FacilityKind.Guild, FacilityKind.Church, FacilityKind.Other),

        // 후원자가 앉은 자리에만 붙는 세 줄. 자리는 후원자가 정하므로 여기서는 안 가린다.
        new(TownWork.Persuade, Facility.Persuade),
        new(TownWork.Report, Facility.Report),
        new(TownWork.BreakContract, Facility.Break),
        new(TownWork.BribeInspector, Facility.Bribe),
        new(TownWork.BorrowShips, Facility.Borrow),
    ];

    /// <summary>가르치는 건물이면 그 자리가 어디든 수련이 뜬다.</summary>
    public static bool Teaches(uint teachMask) => teachMask != 0;

    /// <summary>그 일이 명령 창에 뜨는 이름. 표에 없으면 빈 문자열.</summary>
    public static string NameOf(TownWork work) =>
        Array.Find(All, row => row.Work == work)?.Name ?? "";

    /// <summary>
    /// 줄을 늘리고 줄이는 조건. 게임은 조건이 어긋난 줄을 <b>흐리게 두지 않고 아예 감춘다</b>.
    /// </summary>
    /// <param name="Teaches">건물 표에 가르침 비트가 섰는지 — 수련이 붙는다.</param>
    /// <param name="Poor">주머니가 가벼운지 — 여관 허드렛일이 그때만 나온다.</param>
    /// <param name="CanAnnounce">알릴 발견물이 있는지 — 항구 발표가 그때만 나온다.</param>
    /// <param name="PatronRow">
    /// 후원자가 앉았으면 그 줄(설득 · 보고 · 계약중단). 없으면 null.
    /// </param>
    /// <param name="PatronBribe">
    /// 「감찰관을 매수」 줄이 설 조건인지(<c>0x0044EA30</c>) — 그 줄은 후원자 줄 <b>바로 밑</b>이다.
    /// </param>
    /// <param name="PatronBorrow">
    /// 「배를 빌린다」 줄이 설 조건인지(<c>0x0044EA80</c>) — 매수 줄 밑이다.
    /// </param>
    /// <param name="Commented">
    /// 이 건물이 발견물이고 <b>이미 발견했는지</b> — 그때만 "해설" 줄이 붙는다.
    /// </param>
    /// <param name="Contracted">계약을 맺고 있는지 — 술집의 「정보를 듣는다」가 그때 붙는다.</param>
    /// <param name="HasHeir">
    /// 아이가 있는지. 없으면 자택의 <b>교육 · 세대교체</b> 줄이 아예 안 붙는다 —
    /// 가르칠 아이도, 물려줄 아이도 없기 때문이다.
    /// </param>
    /// <param name="Wed">
    /// 아내가 있는지. 없으면 <b>후손을 남긴다</b> 줄이 안 붙는다 — 혼자서는 아이가
    /// 생길 길이 없다(<see cref="Home.CanLeaveHeir"/>).
    /// </param>
    public readonly record struct TownState(bool Teaches, bool Poor, bool CanAnnounce,
                                            string? PatronRow, bool Commented = false,
                                            IReadOnlyList<string>? Drinks = null,
                                            bool Contracted = false, bool HasHeir = false,
                                            bool Wed = false, bool PatronBribe = false,
                                            bool PatronBorrow = false, bool HasSon = false,
                                            bool HasBooks = true);

    /// <summary>
    /// 그 시설의 명령 창에 늘어놓을 줄들. 차례와 문구는 <see cref="Facility.Menu"/> 것이고,
    /// 조건이 붙는 줄만 여기서 붙이고 뗀다.
    /// </summary>
    public static List<string> LinesOf(Facility facility, TownState state)
    {
        var items = facility.Menu.ToList();

        // 술집이 파는 술은 맨 앞에 붙는다. 고장마다 가짓수가 달라 표에서 골라 온다.
        if (facility.Kind == FacilityKind.Tavern && state.Drinks is { Count: > 0 } drinks)
            items.InsertRange(0, drinks);

        // 「정보를 듣는다」는 <b>술보다도 위</b>다 — 계약을 맺고 있을 때만 붙는다.
        if (facility.Kind == FacilityKind.Tavern && state.Contracted)
            items.Insert(0, NameOf(TownWork.Info));

        // 가르치는 건물인데 줄에 수련이 없으면(학자 저택 따위) 맨 앞에 붙여 준다.
        // 가르칠 것이 없으면 수련 줄은 <b>아예 없다</b>(0x00490D60 이 보임 칸을 0 으로 둔다) — 흐리게 남기지 않는다.
        string train = NameOf(TownWork.Train);
        if (state.Teaches && !items.Contains(train)) items.Insert(0, train);
        if (!state.Teaches) items.Remove(train);

        // 여관 허드렛일은 주머니가 가벼울 때만 나온다.
        if (facility.Kind == FacilityKind.Inn && !state.Poor)
            items.Remove(NameOf(TownWork.OddJob));

        // 항구의 발표는 모항이고 알릴 발견물이 있을 때만 뜬다
        // (게임도 0x00477974 가 0x00476DE0 의 값을 그 줄의 보임 칸에 넣는다).
        if (facility.Kind == FacilityKind.Harbor && !state.CanAnnounce)
            items.Remove(NameOf(TownWork.Announce));

        // 아이가 없으면 교육·세대교체는 <b>줄에서 뺀다</b>. 가르칠 아이도 물려줄 아이도
        // 없는데 줄만 서 있으면 눌러 보고서야 알게 된다.
        if (facility.Kind == FacilityKind.Home && !state.HasHeir)
        {
            items.Remove(NameOf(TownWork.Educate));
            items.Remove(NameOf(TownWork.Succeed));
        }

        // 세대교체는 <b>아들</b>이 있어야 줄이 선다(0x0046245C — 0x004AB790(0, 0), 딸만으로는 안 선다).
        if (facility.Kind == FacilityKind.Home && !state.HasSon)
            items.Remove(NameOf(TownWork.Succeed));

        // 도서관 「열람」은 그 해 이 서가에 꽂힌 책이 있어야 줄이 선다(0x004B3630 → 0x004B3470).
        if (facility.Kind == FacilityKind.Library && !state.HasBooks)
            items.Remove(NameOf(TownWork.Read));

        // 아내가 없으면 후손을 남길 길도 없다.
        if (facility.Kind == FacilityKind.Home && !state.Wed)
            items.Remove(NameOf(TownWork.Heir));

        // 발견한 건물이면 "해설" 이 나가기 줄 바로 앞에 붙는다.
        if (state.Commented) items.Insert(Math.Max(0, items.Count - 1), NameOf(TownWork.Comment));

        // 후원자가 앉은 건물이면 그 줄이 맨 앞에 붙는다 — 왕궁만이 아니라 총독부·상관·
        // 학자 저택 어디든 그렇다. 계약을 맺은 자리이고 맡은 것을 찾아 왔으면 "보고" 다
        // (게임도 같은 자리를 계약 상태로 갈아 끼운다 — 0x0044EAE0).
        if (state.PatronRow is { Length: > 0 } row) items.Insert(0, row);

        // 「감찰관을 매수」는 후원자 줄 바로 밑이다(0x0044EAE0 이 둘째 칸에 넣는다).
        int patronAt = state.PatronRow is { Length: > 0 } ? 1 : 0;
        if (state.PatronBribe) items.Insert(patronAt++, NameOf(TownWork.BribeInspector));
        if (state.PatronBorrow) items.Insert(patronAt, NameOf(TownWork.BorrowShips));

        return items;
    }

    /// <summary>
    /// 그 시설의 그 줄이 무슨 일인지. 표에 없으면 <see cref="TownWork.None"/> 이다.
    /// </summary>
    /// <param name="facility">지금 들어와 있는 시설. 나가는 줄의 문구를 여기서 얻는다.</param>
    /// <param name="item">명령 창에서 고른 줄.</param>
    /// <param name="teaches">건물 표에 가르침 비트가 섰는지(<see cref="Teaches"/>).</param>
    /// <param name="patronHere">이 건물에 후원자가 앉아 있는지.</param>
    public static TownWork WorkOf(Facility facility, string item, bool teaches, bool patronHere)
    {
        if (item == facility.ExitItem) return TownWork.Exit;

        foreach (var row in All)
        {
            if (row.Name != item) continue;

            // 해설도 자리를 안 적어 두었다 — 발견물이 된 건물이면 왕궁이든 교회든 어디든
            // 붙기 때문이다. 그러니 아래의 "자리가 없으면 후원자 줄" 규칙보다 먼저 걸러야
            // 한다. 예전에는 이 줄이 없어서, 후원자가 안 앉은 건물에서 해설이 늘 흐렸다
            // (알함브라 궁전이 그랬다 — 붙기는 붙는데 눌리지 않았다).
            if (row.Work == TownWork.Comment) return TownWork.Comment;

            // 자리를 안 적어 둔 줄(설득·보고·계약중단)은 후원자가 앉았을 때만 산다.
            if (row.At.Length == 0) return patronHere ? row.Work : TownWork.None;
            if (row.Work == TownWork.Train) return teaches ? TownWork.Train : TownWork.None;
            if (Array.IndexOf(row.At, facility.Kind) >= 0) return row.Work;
        }
        return TownWork.None;
    }
}
