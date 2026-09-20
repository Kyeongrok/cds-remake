
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Models;

/// <summary>도시 그림에서 눌러 들어갈 수 있는 시설.</summary>
public enum FacilityKind
{
    Harbor,       // 항구
    Shipyard,     // 조선소
    Tavern,       // 술집
    Inn,          // 여관
    Market,       // 시장
    TradingPost,  // 교역소
    Church,       // 교회
    Palace,       // 왕궁
    Library,      // 도서관
    Guild,        // 조합
    Home,         // 자택 — 내 집이다(저택 = 귀족 저택은 딴 건물이다)
    Gate,         // 성문
    Other,        // 그 밖(저택·상관·모스크·사원 …) — 아직 흉내내지 않는다
}

/// <summary>
/// 시설 한 채 — 이름과 명령 창에 늘어놓을 줄, 그리고 들어가 있는 동안 도는 곡.
/// </summary>
/// <remarks>
/// 게임도 같은 모양이다. 시설 객체 표(<c>0x56A038</c>) 하나를 공통 실행(<c>0x4A2530</c>)이
/// 돌리고, 시설마다 다른 것은 가상함수표의 메뉴 칸(<c>+0x34~+0x48</c>)뿐이다. 그래서 여기서도
/// 창을 시설마다 만들지 않고 이 표로 짓는다.
///
/// 메뉴 문구는 CDS_95.EXE 에서 그대로 읽어 온 것이다(볼트 <c>15.분석-시설 화면 엔진</c>).
/// 차례는 게임 화면에서 본 대로 맞췄다.
/// </remarks>
public sealed record Facility(FacilityKind Kind, string Name, string[] Menu)
{
    /// <summary>이 줄을 누르면 명령 창이 닫힌다. 시설마다 말이 다르다.</summary>
    public string ExitItem => Menu[^1];

    /// <summary>
    /// 게임에 있는 시설들. 지금 도시 그림에서 자리를 아는 것은 항구·조선소·술집 셋이고,
    /// 나머지는 자리를 찾으면 그대로 뜬다(<see cref="CityBuildings"/>).
    /// </summary>
    public static readonly Facility[] All =
    [
        // "발표" 는 발표할 발견물이 있을 때만 뜬다 — 게임도 그 줄의 <b>보임</b> 쪽을
        // 조건(0x00476DE0)으로 켠다(CityPicDialog.BuildMenu 가 없으면 뺀다).
        new(FacilityKind.Harbor, "항구",
            ["출항", "보급", "함대편성", "선원편성", "마을정보", Announce, "마을로 돌아간다"]),

        new(FacilityKind.Shipyard, "조선소",
            ["구입", "매각", "수리", "개조", "조선소를 나온다"]),

        // 파는 술은 여기 안 적는다 — 고장마다 다르므로 술 표(<see cref="DrinkTable"/>)에서
        // 그 고장 것을 골라 <see cref="TownWorks.LinesOf"/> 가 맨 앞에 붙인다.
        new(FacilityKind.Tavern, "술집",
            ["포카를 권한다", "부하편성", "술집을 나온다"]),

        // 차례는 게임 차림표 표(<c>0x005692E8</c>) 그대로다 — <b>부하편성이 허드렛일보다 위</b>다.
        // 표는 (글, 보임, 누를 수 있음) 셋씩이고, 허드렛일의 보임 칸을 0x0047FE38 이
        // 「소지금 100 이하」로 켠다.
        new(FacilityKind.Inn, "여관",
            ["숙박", "부하편성", "허드렛일", "기능", "여관을 나온다"]),

        new(FacilityKind.Market, "시장",
            ["구입", "매각", "시장을 나온다"]),

        new(FacilityKind.TradingPost, "교역소",
            ["매매", "회화", "교역소를 나온다"]),

        new(FacilityKind.Church, "교회",
            ["수련", "교회를 나온다"]),

        // "설득" 은 여기 적지 않는다 — 그 건물에 후원자가 앉아 있을 때만 붙는 줄이라
        // 도시마다 다르다(CityPicDialog.BuildMenu 가 맨 앞에 끼워 넣는다).
        //
        // "감찰관을 매수"(조건 0x44EA30)와 "배를 빌린다"(조건 0x44EA80)도 조건이 맞을 때만
        // 나온다. 게임은 조건이 어긋난 줄을 흐리게 두지 않고 아예 감춘다 — 두카레 궁전
        // 화면에도 설득과 나가기 두 줄만 떴다. 조건은 이렇다:
        //   감찰관을 매수 — 계약한 뒤 한 자리에서 발견물이 둘 이상 나왔을 때
        //                   (코드도 개수를 세어 cmp eax,1 / jle 로 둘 미만이면 끈다)
        //   배를 빌린다   — 계약할 때 스폰서가 배를 빌리겠냐 물었는데 아니라고 답했을 때
        //                   (그 답이 플래그 한 비트로 남아 test byte [eax+0x1C],1 로 본다)
        new(FacilityKind.Palace, "왕궁",
            ["왕궁을 나온다"]),

        // 게임 도서관 줄은 셋이지만(검색 0x00544B48 · 열람 · 나온다) <b>「검색」은 걷었다</b> —
        // 고를 책을 모으는 칸(책 +0x40)을 세우는 코드가 원본 어디에도 없어 목록이 늘 비고,
        // 사서가 묻기만 하고 끝나는 죽은 줄이다(까닭은 LibraryMenu.Search 주석에 적어 둔다).
        new(FacilityKind.Library, "도서관",
            ["열람", "도서관을 나온다"]),

        new(FacilityKind.Guild, "조합",
            ["수련", "조합을 나온다"]),

        // 자택은 내 집이다. "저택"(귀족 저택)은 딴 건물이라 이 줄이 아니다 — 건물 표에서도
        // 자택이 코드 11, 저택이 12 로 갈려 있다.
        new(FacilityKind.Home, "자택",
            ["휴양", "보관", "저금", "후손을 남긴다", "교육", "세대교체",
             "백과사전을 본다", "연표를 본다", "은퇴한다", "기능", "밖으로 나간다"]),

        new(FacilityKind.Gate, "성문",
            ["탐험을 떠난다", "마을로 돌아간다"]),
    ];

    /// <summary>
    /// 어느 시설에서 "기능" 을 골랐을 때 뜨는 줄들. 게임은 여기서 저장·로드까지 한다.
    /// </summary>
    public static readonly string[] SystemMenu = ["저장", "로드", "게임 종료", "게임 재개"];

    /// <summary>
    /// 항구에서 "함대편성" 을 골랐을 때 뜨는 줄들. 게임처럼 제목 없이 줄만 쌓인다.
    /// </summary>
    public static readonly string[] FleetMenu =
        ["기함 변경", "선박 편입", "선박 삭제", "선박 파기", FleetExit];

    /// <summary>함대편성 창을 접고 항구 창으로 돌아가는 줄.</summary>
    public const string FleetExit = "편성 종료";

    /// <summary>
    /// 조선소에서 배를 고른 뒤 뜨는 <b>개조</b> 창의 줄. 게임 것 열한 줄 그대로다
    /// (<c>0x00532228</c> 벌 — <c>0x004966E0</c> 이 쌓는다).
    /// </summary>
    /// <remarks>
    /// 게임도 할 수 없는 줄은 <b>흐리게</b> 둔다(줄마다 켜짐 칸이 둘씩 딸린다). 우리는 줄을
    /// 빼지 않고 그대로 늘어놓되, 못 하는 줄도 <b>골라 보고 까닭을 듣게</b> 한다 — 게임도
    /// 그 말을 갖춰 두었다(<see cref="ShipyardMenu"/> 의 <c>RefitBlocked</c>).
    /// 빌린 배의 「선명변경」만은 처음부터 흐리다(<c>0x004967C2</c>).
    /// </remarks>
    public static readonly string[] RefitMenu =
    [
        RefitMast, RefitSailKind, RefitSail, RefitCapacity, RefitTonnage, RefitReinforce,
        RefitFigurehead, RefitTurrets, RefitCannon, RefitRename, RefitExit,
    ];

    /// <summary>개조 창에서 우리가 흉내낸 줄.</summary>
    /// <remarks>
    /// 선수상 줄은 이 판 EXE 에 <c>0x00532278</c> "선두상" 으로 적혀 있는데, 같은 EXE 가
    /// 안 쓰이는 <c>0x0053C068</c> "선수상 선택" 벌도 함께 들고 있다. 화면에서 본 대로
    /// <b>선수상</b> 으로 적는다.
    /// </remarks>
    public const string RefitMast = "마스트 추가",
                        RefitSailKind = "돛종류 변경",
                        RefitSail = "돛 추가",
                        RefitCapacity = "용량증가",
                        RefitTonnage = "부력증가",
                        RefitReinforce = "보강",
                        RefitTurrets = "포탑수변경",
                        RefitCannon = "대포구입",
                        RefitRename = "선명변경",
                        RefitFigurehead = "선수상";

    /// <summary>개조 창을 접고 배 고르기로 돌아가는 줄.</summary>
    public const string RefitExit = "개조를 그만둔다";

    /// <summary>
    /// 선원편성 창의 줄. 게임 것 그대로다(<c>0x00545250</c> 벌 — 항구 <c>0x004774E0</c>).
    /// </summary>
    public static readonly string[] CrewMenu = ["선원모집", "선원해고", CrewExit];

    /// <summary>선원편성 창을 접고 항구 창으로 돌아가는 줄.</summary>
    public const string CrewExit = "돌아간다";

    /// <summary>항구에서 발견물을 알리는 줄. 알릴 것이 없으면 아예 안 뜬다.</summary>
    public const string Announce = "발표";

    /// <summary>
    /// 후원자가 앉은 건물의 첫 줄. 계약 상태로 갈린다.
    /// </summary>
    /// <remarks>
    /// 게임은 그 자리를 문자열 풀 <c>0x00549DF8</c> 에서 시설 객체 <c>+0xB0</c>(계약 상태)로
    /// 골라 넣는다(<c>0x0044EAE0</c>) — 0 설득 · 1 계약중단 · <b>2 보고</b> · 3 계약중단.
    /// 상태를 정하는 곳은 <c>0x0044E630</c> 이고, -1 이면 그 줄 자체가 없다.
    /// 셋 다 옮겼다 — 계약이 없으면 설득, 계약 중인데 보고할 것이 없으면 계약중단,
    /// 보고할 것이 있으면 보고다(<c>0x0044E9A0</c>·<c>0x0044E9E0</c>·<c>0x0044EA00</c>).
    /// </remarks>
    public const string Persuade = "설득", Report = "보고", Break = "계약중단";

    /// <summary>후원자가 앉은 자리의 둘째 줄(<c>0x005324C0</c>) — 감찰관에게 뇌물을 준다.</summary>
    public const string Bribe = "감찰관을 매수";

    /// <summary>셋째 줄(<c>0x005324D0</c>) — 계약 중에 배를 더 빌린다.</summary>
    public const string Borrow = "배를 빌린다";

    /// <summary>
    /// 자택 휴양 창의 줄. 게임 것 그대로다(<c>0x00539778</c> 벌 — 휴양 <c>0x00460660</c>).
    /// </summary>
    public static readonly string[] RestMenu = ["한 달 휴양", "장기 휴양", RestExit];

    /// <summary>휴양 창을 접고 자택 창으로 돌아가는 줄.</summary>
    public const string RestExit = "취소";

    /// <summary>
    /// 자택 저금 창의 줄. 게임 것 그대로다(<c>0x005398D0</c> 벌 — 저금 <c>0x004609C0</c>).
    /// </summary>
    public static readonly string[] SavingsMenu = ["저금한다", "꺼낸다", SavingsExit];

    /// <summary>저금 창을 접고 자택 창으로 돌아가는 줄.</summary>
    public const string SavingsExit = "중지한다";

    /// <summary>
    /// 건물 코드 0~11 이 가리키는 시설 — 게임은 종류 이름이 아니라 <b>코드</b>로 건물 갈래를 고른다.
    /// </summary>
    /// <remarks>
    /// 그래서 코드 2 인 「성」·「총독부」·「황궁」도 왕궁 갈래라 「왕궁을 나온다」(<c>0x00544D08</c>)가
    /// 뜬다. 코드 12~15(모스크·사원·상관·저택·학자 저택·관청·대사관 …)는 후원자가 앉는 자리로
    /// 「%s에서 나온다」(<c>0x00568DC0</c>)다.
    /// </remarks>
    private static readonly string[] CodeKinds =
        ["항구", "교역소", "왕궁", "교회", "술집", "여관", "조선소", "시장", "도서관", "조합", "성문", "자택"];

    /// <summary>
    /// 건물의 시설을 찾는다 — 코드가 0~11 이면 그 갈래, 아니면 종류 이름으로 찾고, 그래도 없으면
    /// 나가기 한 줄만 있는 창을 준다.
    /// </summary>
    public static Facility For(string kind, int code = -1)
    {
        if (code >= 0 && code < CodeKinds.Length) kind = CodeKinds[code];
        foreach (var f in All)
            if (f.Name == kind) return f;
        return new Facility(FacilityKind.Other, kind, [$"{kind}에서 나온다"]);
    }
}
