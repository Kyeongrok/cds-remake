// 대시보드 — /dashboard 가 이 HTML 을 그대로 내고, 페이지가 /v1/dashboard 에서 집계를 받아 그린다.
// 바깥 라이브러리 없이 표와 막대만 쓴다.

export const DASHBOARD_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>코스타 델 솔 — 게임 통계</title>
<style>
  :root { --bg: #0d1424; --panel: #16203a; --line: #2b3a63; --text: #e8ecf7; --dim: #93a0c2; --accent: #6ea8ff; --gold: #ffe070; --bar: #3b6fd6; --good: #5fd08a; --bad: #ff7a7a; }
  @media (prefers-color-scheme: light) { :root { --bg: #f4f6fb; --panel: #ffffff; --line: #d5dbea; --text: #1b2338; --dim: #5d6885; --accent: #2a5fd0; --gold: #a06a00; --bar: #8fb2f2; --good: #1d8a4c; --bad: #c23636; } }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.5 "Segoe UI", "Malgun Gothic", sans-serif; }
  main { max-width: 1100px; margin: 0 auto; padding: 20px 16px 60px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  h2 { font-size: 15px; margin: 0 0 10px; color: var(--gold); }
  h2 small { color: var(--dim); font-weight: 400; font-size: 13px; }
  .sub { color: var(--dim); margin-bottom: 16px; }
  .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 16px; }
  select, button { background: var(--panel); color: var(--text); border: 1px solid var(--line); border-radius: 6px; padding: 5px 10px; font: inherit; }
  button { cursor: pointer; }
  button.on { border-color: var(--accent); color: var(--accent); }
  .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 12px; margin-bottom: 16px; }
  .tile, section { background: var(--panel); border: 1px solid var(--line); border-radius: 10px; padding: 14px; }
  .tile b { display: block; font-size: 24px; font-variant-numeric: tabular-nums; }
  .tile span { color: var(--dim); }
  section { margin-bottom: 16px; overflow-x: auto; min-width: 0; }
  .two { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 16px; }
  .scroll { max-height: 560px; overflow-y: auto; }
  table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
  th, td { padding: 5px 8px; text-align: right; white-space: nowrap; border-bottom: 1px solid var(--line); }
  th:first-child, td:first-child, td.name, th.name { text-align: left; }
  th { color: var(--dim); font-weight: 600; position: sticky; top: 0; background: var(--panel); }
  th.sort { cursor: pointer; }
  th.sort.on { color: var(--gold); }
  td.wrap { white-space: normal; word-break: break-all; }
  tr.chapter td { background: var(--bg); font-weight: 600; border-top: 2px solid var(--line); text-align: left; }
  td.indent { padding-left: 22px; }
  td.bar { width: 28%; padding-right: 0; }
  td.bar i { display: block; height: 10px; background: var(--bar); border-radius: 3px; min-width: 1px; }
  .dim { color: var(--dim); }
  .good { color: var(--good); } .bad { color: var(--bad); }
  .tabs { display: flex; gap: 6px; margin-bottom: 10px; flex-wrap: wrap; }
  #error { color: var(--bad); }
</style>
</head>
<body>
<main>
  <h1>코스타 델 솔 — 게임 통계</h1>
  <div class="sub">익명으로 수집한 게임 셈입니다. 모드 옵션은 설치마다 마지막 값이라 버전으로 거르지 않습니다.</div>
  <div class="filters">
    <label>버전 <select id="version"><option value="">전체</option></select></label>
    <span class="dim" id="made"></span>
    <span id="error"></span>
  </div>
  <div class="tiles" id="tiles"></div>
  <section><h2>모드 옵션 <small>설치별 마지막 값</small></h2><div class="tabs" id="modtabs"></div><div class="scroll"><table id="mods"></table></div></section>
  <section><h2>발견물 순위</h2><div class="scroll"><table id="discoveries"></table></div></section>
  <div class="two">
    <section><h2>고용한 부하 <small>술집에서 고용</small></h2><div class="scroll"><table id="hires"></table></div></section>
    <section><h2>해고한 부하 <small>부하편성 해고 · 재계약 거절</small></h2><div class="scroll"><table id="dismisses"></table></div></section>
  </div>
  <section><h2>주인공 <small>판마다 처음 → 마지막 능력치 · 최근 받은 300판</small></h2><div class="scroll"><table id="heroes"></table></div></section>
  <section><h2>항해 <small>어디서 어디로 · 평균 날수 · 평균 선회 · v1.1.30 까지</small></h2><div class="scroll"><table id="voyages"></table></div></section>
  <section><h2>네비게이션 <small>자동항해에서 고른 것 · 끝난 꼴</small></h2><table id="navs"></table></section>
  <section><h2>전투</h2><table id="battles"></table></section>
  <section><h2>사건 <small>바다 · 육지에서 하루를 넘길 때 — 밑수(넘긴 날)와 견줘 본다</small></h2><table id="events"></table></section>
  <section><h2>오류</h2><div class="scroll"><table id="errors"></table></div></section>
  <section><h2>날짜별 · 버전별</h2><table id="days"></table></section>
</main>
<script>
const $ = (id) => document.getElementById(id);
const n = (v) => Number(v || 0).toLocaleString('ko-KR');
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const sum = (rows, k) => rows.reduce((s, r) => s + (r[k] || 0), 0);
const pct = (a, b) => b ? Math.round(100 * a / b) + '%' : '-';
const EMPTY = (cols) => '<tr><td colspan="' + cols + '" class="dim name">아직 자료가 없습니다.</td></tr>';
// 단계 옵션 — 게임(PlayStats.LevelMods)이 켬·끔이 아니라 값으로 보내는 것.
const LEVELS = ['InfoLevel', 'SeaRaidScale', 'MutinyRate', 'Resolution', 'PortDays', 'PersonRollDays', 'PersonMoveOdds', 'PersonRestDays', 'ModPreset', 'ScreenWidth', 'ScreenHeight', 'ScreenScale'];
// 값에 이름이 있는 단계 옵션 — 수 대신 이름으로 적는다. ModPreset 은 모드 창 「권장」에서 고른 단계다.
const LEVEL_NAMES = { ModPreset: ['오리지널', '초보', '중수', '고수', '파워업키트'], InfoLevel: ['기본', '일반', '상세'] };
// 옵션 이름 — 게임은 GameSettings 의 속성 이름으로 보낸다. 모드 창(ModDialog)에 적힌 글로 바꿔 보이고, 여기 없는 것은 속성 이름 그대로 나온다.
const MOD_NAMES = {
  ShowShipSpeed: '배 속도', FleetCard: '접근 함대 정보', ShowSeaDays: '항해 일수', ShowDiscoveryCount: '발견물 수', ShowFlowArrows: '바람·해류 화살표',
  ShowMiniMap: '미니맵', MiniMapFound: '찾은 발견물', MiniMapYet: '못 찾은 발견물', MiniMapCities: '도시', MiniMapWind: '풍향', MiniMapCurrent: '해류',
  ShowDiscoveryMapMenu: '발견물 지도', DiscoveryMapSpots: '발견물 지도 — 발견물', DiscoveryMapCities: '발견물 지도 — 도시', DiscoveryMapGrid: '발견물 지도 — 격자',
  DiscoveryMapWind: '발견물 지도 — 풍향', DiscoveryMapCurrent: '발견물 지도 — 해류',
  ShowBarmaidBookMenu: '여급 수첩', ShowPersonMoveMenu: '인물 이동', PlaceShipByCtrlClick: 'Ctrl+클릭 배 놓기',
  ShowContractHintOverlay: '현재 계약 힌트', ShowFleetOverlay: '현재 함대 선박 이름', ShowHintOverlay: '현재 힌트 목록', ShowSkillOverlay: '기능·언어',
  ShowCoordOverlay: '좌표 상자(개발)', ShowPeopleOverlay: '만난 사람 상자(개발)',
  AutoFlee: '자동 도망', AutoCrew: '선원 자동 모집', AskLendShips: '배 빌림 묻기', AutoSupply: '자동 보급', AutoSupplyMax: '자동 보급 — 최대로', AutoFundRaise: '자금 증가 기본',
  ExtraDiscoveries: '추가 발견물', AutoSaveOnPort: '도시 자동저장', AutoOpenShipMap: '켤 때 함대 창 바로 띄우기', AutoOpenWorldMap: '켤 때 함대 창 바로 띄우기', SteadyHelm: '자동항해 — 최소 조타',
  ShowVitalityInfo: '생명력 정보', RidiFont: '리디바탕 글꼴', RidiLoadList: '게임 로드 창 폰트', HintBrowser: '향상된 힌트 보기', PatronListEnhanced: '향상된 후원자 정보',
  EnhancedItemArt: '향상된 아이템 이미지', PersonInfoEnhanced: '향상된 인물정보 목록', PersonInfoList: '인물정보 고르기 목록', ItemListPictures: '아이템 창 개선',
  KeepCharacterDraft: '캐릭터 작성 기억', RerollOnJob: '직업 누르면 다시 굴림', WheelZoom: '휠 확대', NoWeight: '중량 없음', HandyInventory: '편리한 인벤토리', QuickStorage: '편리한 보관함',
  CargoPriceRank: '적하 시세 순위', SphinxHelper: '스핑크스 퀴즈 도우미',
  LandAutoWalk: '뭍 자동이동', CaptureMirror: '녹화용 창 합치기', ShipCentered: '배 중심', LandDetail: '뭍 세부 질감', PixelFilter: '도트 확대 필터', ShipWake: '배 항적',
  SmoothClouds: '부드러운 구름', SeaEffect: '바다 입체 효과', HiResSea: '고해상도 바다',
  Nobility: '작위', MateDismiss: '부하 해고', SkipRecontract: '보고 시 재계약 끄기', SuezCanal: '수에즈 운하', SpecialOrder: '특별주문', PlayOpeningMovie: '오프닝 동영상 틀기',
  BgmEnabled: '배경음악', SfxEnabled: '효과음', CustomBgmEnabled: '커스텀 BGM', DuelImmortal: '일기토 불사(개발)',
  InfoLevel: '정보 제공 등급', ModPreset: '권장 단계', SeaRaidScale: '해적·이슬람 함대 확률 배수(차례)', MutinyRate: '바다 반란 몫(%)', PortDays: '항구 드나드는 날수', PersonRollDays: '인물 이동 주기(0 매월 1일 · N일마다)', PersonMoveOdds: '인물 떠날 확률(N분의 1)', PersonRestDays: '인물 쉬는 날',
  Resolution: '게임 창 크기(고른 차례)', ScreenWidth: '모니터 너비(점)', ScreenHeight: '모니터 높이(점)', ScreenScale: '모니터 배율(%)',
};
// 옵션이 모드 창의 어느 탭에 있는지 — 탭 차례는 모드 창(ModDialog.Tabs)과 같다. 모드 창 밖에서 바꾸는 것은 「그 밖」 · 「화면」 · 「개발」로 모은다.
const MOD_TABS = [
  ['권장', ['ModPreset']],
  ['편의성', ['PlaceShipByCtrlClick', 'AutoFlee', 'AutoSupply', 'AutoSupplyMax', 'AutoCrew', 'AskLendShips', 'AutoFundRaise', 'KeepCharacterDraft', 'WheelZoom', 'NoWeight',
    'HandyInventory', 'QuickStorage', 'CargoPriceRank', 'SphinxHelper', 'PortDays']],
  ['정보', ['ShowShipSpeed', 'FleetCard', 'ShowSeaDays', 'ShowDiscoveryCount', 'ShowFlowArrows', 'ShowContractHintOverlay', 'ShowFleetOverlay', 'ShowHintOverlay', 'ShowSkillOverlay', 'InfoLevel']],
  ['미니맵', ['ShowMiniMap', 'MiniMapFound', 'MiniMapYet', 'MiniMapCities', 'MiniMapWind', 'MiniMapCurrent']],
  ['고해상도', ['SeaEffect', 'HiResSea', 'LandDetail', 'PixelFilter', 'ShipWake', 'SmoothClouds']],
  ['UI', ['ShowDiscoveryMapMenu', 'ShowBarmaidBookMenu', 'ShowPersonMoveMenu', 'ShowVitalityInfo', 'HintBrowser', 'PatronListEnhanced', 'EnhancedItemArt', 'PersonInfoEnhanced', 'ItemListPictures']],
  ['폰트', ['RidiFont']],
  ['캐릭터', ['MateDismiss', 'SkipRecontract', 'RerollOnJob']],
  ['발견물', ['ExtraDiscoveries']],
  ['저장', ['AutoSaveOnPort']],
  ['일반', ['CustomBgmEnabled', 'SuezCanal', 'SpecialOrder', 'PlayOpeningMovie', 'SeaRaidScale', 'PersonRollDays', 'PersonMoveOdds', 'PersonRestDays']],
  ['실험', ['LandAutoWalk', 'CaptureMirror', 'ShipCentered', 'Nobility', 'RidiLoadList']],
  ['그 밖', ['BgmEnabled', 'SfxEnabled', 'AutoOpenShipMap', 'AutoOpenWorldMap', 'SteadyHelm', 'PersonInfoList', 'DiscoveryMapSpots', 'DiscoveryMapCities', 'DiscoveryMapGrid', 'DiscoveryMapWind', 'DiscoveryMapCurrent']],
  ['화면', ['Resolution', 'ScreenWidth', 'ScreenHeight', 'ScreenScale']],
  ['개발', ['DuelImmortal', 'MutinyRate', 'ShowCoordOverlay', 'ShowPeopleOverlay']],
];
const OTHER_TAB = MOD_TABS.findIndex(([label]) => label === '그 밖');
const modTabOf = (name) => { const i = MOD_TABS.findIndex(([, names]) => names.includes(name)); return i < 0 ? OTHER_TAB : i; };
const modName = (name) => '<span title="' + esc(name) + '">' + esc(MOD_NAMES[name] || name) + '</span>';
const SEA = [['Won', '승리'], ['Defeated', '패배'], ['Escaped', '퇴각'], ['Surrendered', '항복']];

let data = null;
const state = { version: '' }, sorts = { discoveries: 'n', hires: 'n', dismisses: 'n', voyages: 'n', errors: 'n' };
let modTab = 'all';

const COLS = [['n', '횟수'], ['installs', '설치']];
const bar = (v, top) => '<td class="bar"><i style="width:' + (100 * v / Math.max(1, top)).toFixed(1) + '%"></i></td>';
const sortHead = (id) => COLS.map(([k, label]) => '<th class="sort ' + (k === sorts[id] ? 'on' : '') + '" data-k="' + k + '">' + label + (k === sorts[id] ? ' ▼' : '') + '</th>').join('');
const bindSort = (id) => $(id).querySelectorAll('th.sort').forEach((th) => th.onclick = () => { sorts[id] = th.dataset.k; render(); });
const tabs = (id, list, picked, pick) => {
  $(id).innerHTML = list.map(([k, label]) => '<button data-k="' + k + '" class="' + (k === picked ? 'on' : '') + '">' + label + '</button>').join('');
  $(id).querySelectorAll('button').forEach((b) => b.onclick = () => { pick(b.dataset.k); render(); });
};

// 순위 표 — 횟수 · 설치 머리를 눌러 줄 세운다.
function rankTable(id, label, rows, nameOf) {
  const k = sorts[id];
  rows = [...rows].sort((a, b) => b[k] - a[k] || b.n - a.n);
  const top = Math.max(1, ...rows.map((r) => r[k]));
  $(id).innerHTML = '<tr><th>#</th><th class="name">' + label + '</th>' + sortHead(id) + '<th></th></tr>'
    + (rows.length ? rows.map((r, i) => '<tr><td class="dim">' + (i + 1) + '</td><td class="name wrap">' + nameOf(r) + '</td><td>' + n(r.n) + '</td><td>' + n(r.installs) + '</td>' + bar(r[k], top) + '</tr>').join('')
      : EMPTY(5));
  bindSort(id);
}

// 모드 옵션 — 모드 창의 탭마다 묶는다. 묶음 안에서는 켬·끔 옵션을 켠 설치가 많은 차례로, 단계 옵션은 그 밑에 값마다 설치 수로 적는다.
function renderMods() {
  const groups = MOD_TABS.map(([label], i) => ({ label, i, rows: data.mods.filter((m) => modTabOf(m.name) === i) })).filter((g) => g.rows.length);
  if (modTab !== 'all' && !groups.some((g) => String(g.i) === modTab)) modTab = 'all';
  tabs('modtabs', [['all', '전체 (' + data.mods.length + ')'], ...groups.map((g) => [String(g.i), g.label + ' (' + g.rows.length + ')'])], modTab, (k) => { modTab = k; });
  const shown = groups.filter((g) => modTab === 'all' || String(g.i) === modTab);
  $('mods').innerHTML = '<tr><th>#</th><th class="name">옵션</th><th>켠 설치</th><th>설치</th><th>비율</th><th></th></tr>'
    + (shown.length ? shown.map((g) => '<tr class="chapter"><td colspan="6">' + esc(g.label) + ' <span class="dim">' + g.rows.length + '</span></td></tr>'
        + g.rows.map((m, i) => '<tr><td class="dim">' + (i + 1) + '</td><td class="name">' + modName(m.name) + '</td>' + (LEVELS.includes(m.name)
          ? '<td class="dim">단계</td><td>' + n(m.installs) + '</td><td colspan="2" class="name wrap">'
            + (data.modValues || []).filter((v) => v.name === m.name).map((v) => '<b>' + esc((LEVEL_NAMES[m.name] || [])[v.value] || n(v.value)) + '</b> <span class="dim">× ' + n(v.installs) + '</span>').join(' &nbsp;·&nbsp; ')
            + ' <span class="dim">(평균 ' + (Math.round(m.average * 100) / 100) + ')</span></td>'
          : '<td>' + n(m.enabled) + '</td><td>' + n(m.installs) + '</td><td>' + pct(m.enabled, m.installs) + '</td>' + bar(m.enabled, m.installs)) + '</tr>').join('')).join('') : EMPTY(6));
}

// 항해 — 구간마다 횟수 · 설치 수와, 날수 · 선회의 합을 횟수로 나눈 평균.
function renderVoyages() {
  const k = sorts.voyages, rows = [...(data.voyages || [])].sort((a, b) => b[k] - a[k] || b.n - a.n);
  const top = Math.max(1, ...rows.map((r) => r[k])), avg = (sum, count) => (count ? (Math.round(sum / count * 10) / 10).toLocaleString('ko-KR') : '-');
  $('voyages').innerHTML = '<tr><th>#</th><th class="name">구간</th>' + sortHead('voyages') + '<th>평균 날수</th><th>평균 선회</th><th></th></tr>'
    + (rows.length ? rows.map((r, i) => '<tr><td class="dim">' + (i + 1) + '</td><td class="name wrap">' + esc(r.name || r.key) + '</td><td>' + n(r.n) + '</td><td>' + n(r.installs) + '</td><td>'
        + avg(r.days, r.n) + '</td><td>' + avg(r.turns, r.n) + '</td>' + bar(r[k], top) + '</tr>').join('') : EMPTY(7));
  bindSort('voyages');
}

// 주인공 — 판마다 처음 받은 능력치와 마지막 능력치. 맨 위 줄은 처음 능력치의 평균이다.
const ABILITIES = ['체력', '지력', '무력', '매력', '운', '신앙심'];
function renderHeroes() {
  const rows = data.heroes || [];
  const avg = (i) => (rows.length ? Math.round(rows.reduce((s, h) => s + h.first[i], 0) / rows.length) : '-');
  const cell = (a, b) => (a === b ? n(b) : '<span class="dim">' + n(a) + ' →</span> ' + n(b));
  $('heroes').innerHTML = '<tr><th class="name">이름</th><th class="name">나라</th><th class="name">게임 날짜</th>' + ABILITIES.map((a) => '<th>' + a + '</th>').join('') + '<th class="name">버전</th></tr>'
    + (rows.length ? '<tr><td class="name dim" colspan="3">처음 능력치 평균 (' + n(rows.length) + '판)</td>' + ABILITIES.map((_, i) => '<td class="dim">' + avg(i) + '</td>').join('') + '<td></td></tr>'
        + rows.map((h) => '<tr><td class="name">' + esc(h.name) + '</td><td class="name">' + esc(h.nation) + '</td><td class="name">' + esc(h.first_date === h.date ? h.date : h.first_date + ' → ' + h.date)
          + '</td>' + ABILITIES.map((_, i) => '<td>' + cell(h.first[i], h.now[i]) + '</td>').join('') + '<td class="name dim">' + esc(h.version) + '</td></tr>').join('') : EMPTY(10));
}

// 네비게이션 — 열쇠는 「묶음:값」이다. 묶음마다 그 안에서의 몫을 낸다.
const NAV_GROUPS = [['trip', '항해 한 번'], ['pick', '고른 길'], ['helm', '자동이동 모드'], ['from', '건 곳'], ['end', '끝난 꼴']];
const NAV_NAMES = { 'trip:auto': '자동 항해', 'trip:manual': '수동 항해', 'pick:quick': '가장 빠른 길', 'pick:short': '가장 짧은 길',
  'pick:land-fast': '내륙 — 가장 빠른 길', 'pick:land-lesswalk': '내륙 — 덜 걷는 길', 'pick:land-near': '내륙 — 가장 가까운 해안', 'pick:map': '지도를 찍어서',
  'helm:fast': '속도 중시', 'helm:steady': '최소 조타', 'from:city': '도시 안에서', 'from:sea': '바다에서',
  'end:arrived': '도착', 'end:stopped': '손으로 멈춤', 'end:stuck': '길이 막혀 멈춤' };
function renderNavs() {
  const rows = data.navs || [];
  $('navs').innerHTML = '<tr><th class="name">묶음</th><th class="name">값</th><th>횟수</th><th>설치</th><th>몫</th><th></th></tr>'
    + (rows.length ? NAV_GROUPS.map(([group, label]) => {
        const mine = rows.filter((r) => r.key.startsWith(group + ':')).sort((a, b) => b.n - a.n), all = sum(mine, 'n');
        return mine.map((r, i) => '<tr><td class="name">' + (i ? '' : label) + '</td><td class="name">' + esc(NAV_NAMES[r.key] || r.key) + '</td><td>' + n(r.n) + '</td><td>' + n(r.installs) + '</td><td>'
          + pct(r.n, all) + '</td>' + bar(r.n, all) + '</tr>').join('');
      }).join('') : EMPTY(6));
}

function renderBattles() {
  const of = (key) => data.battles.find((b) => b.key === key)?.n || 0;
  const seaAll = sum(data.battles.filter((b) => b.key.startsWith('sea:')), 'n'), landAll = sum(data.battles.filter((b) => b.key.startsWith('land:')), 'n');
  const seaKnown = SEA.reduce((s, [k]) => s + of('sea:' + k), 0), landWin = of('land:win'), landLose = of('land:lose');
  const row = (label, all, win, lose, esc2, sur, other) => '<tr><td class="name">' + label + '</td><td>' + n(all) + '</td><td class="good">' + n(win) + '</td><td class="bad">' + n(lose) + '</td><td>'
    + esc2 + '</td><td>' + sur + '</td><td>' + n(other) + '</td><td>' + pct(win, all) + '</td></tr>';
  $('battles').innerHTML = '<tr><th class="name">갈래</th><th>판 수</th><th>승</th><th>패</th><th>퇴각</th><th>항복</th><th>그 밖</th><th>승률</th></tr>'
    + (seaAll + landAll ? row('해전', seaAll, of('sea:Won'), of('sea:Defeated'), n(of('sea:Escaped')), n(of('sea:Surrendered')), seaAll - seaKnown)
        + row('육상전', landAll, landWin, landLose, '-', '-', landAll - landWin - landLose) : EMPTY(8));
}

// 날 밑수(sea:day · land:day · sea:eligible)는 v1.1.27 까지만 받았다 — 그 뒤 재해는 늘어도 밑수는 그대로라 「넘긴 날 가운데」가 조금씩 부푼다.
const EVENT_NAMES = { 'sea:day': '바다에서 넘긴 날(v1.1.27 까지)', 'sea:eligible': '재해 주사위를 굴릴 수 있던 날(항해 열흘째부터 · v1.1.27 까지)', 'land:day': '육지에서 넘긴 날(v1.1.27 까지)',
  'sea:Storm': '폭풍', 'sea:Blizzard': '눈보라', 'sea:Rats': '쥐', 'sea:Scurvy': '괴혈병', 'sea:Plague': '전염병', 'sea:Weakening': '괴혈병 귀띔',
  'sea:StrangeIllness': '전염병 귀띔', 'sea:BarleyByMe': '보리로 막음(제독)', 'sea:BarleyByMate': '보리로 막음(부관)', 'sea:RatsKilled': '쥐 퇴치(부관)',
  'sea:Mutiny': '반란', 'sea:Siren': '세이렌', 'sea:Iceberg': '빙산', 'land:Heat': '더위', 'land:Cold': '추위', 'land:HotSpring': '온천',
  'land:Rockfall': '낙석', 'land:Swamp': '늪', 'land:Quicksand': '유사', 'land:Beast': '짐승 · 습격', 'land:Meteor': '유성', 'land:Tornado': '회오리' };
function renderEvents() {
  const rows = data.events || [], of = (key) => rows.find((r) => r.key === key)?.n || 0;
  const base = { sea: of('sea:day'), land: of('land:day') };
  $('events').innerHTML = '<tr><th class="name">곳</th><th class="name">일</th><th>횟수</th><th>설치</th><th>넘긴 날 가운데</th></tr>'
    + (rows.length ? ['sea', 'land'].map((where) => rows.filter((r) => r.key.startsWith(where + ':')).sort((a, b) => b.n - a.n)
        .map((r, i) => '<tr><td class="name">' + (i ? '' : where === 'sea' ? '바다' : '육지') + '</td><td class="name">' + esc(EVENT_NAMES[r.key] || r.key) + '</td><td>' + n(r.n)
          + '</td><td>' + n(r.installs) + '</td><td>' + (base[where] ? (100 * r.n / base[where]).toFixed(2) + '%' : '-') + '</td></tr>').join('')).join('') : EMPTY(5));
}

function render() {
  const kind = (k) => (data.kinds || []).find((r) => r.kind === k) || { n: 0, keys: 0 };
  $('tiles').innerHTML = [[data.total?.installs, '설치'], [data.total?.batches, '받은 덩이'], [kind('discovery').n, '발견'], [kind('hire').n, '부하 고용'], [kind('dismiss').n, '부하 해고'],
      [(data.heroes || []).length, '주인공'], [kind('battle').n, '전투'], [kind('error').n, '오류']]
    .map(([v, label]) => '<div class="tile"><b>' + n(v) + '</b><span>' + label + '</span></div>').join('');

  renderMods();
  rankTable('discoveries', '발견물', data.discoveries, (r) => esc(r.name || '#' + r.key));
  rankTable('hires', '부하', data.hires || [], (r) => esc(r.name || '#' + r.key));
  rankTable('dismisses', '부하', data.dismisses || [], (r) => esc(r.name || '#' + r.key));
  renderHeroes();
  renderVoyages();
  renderNavs();
  renderBattles();
  renderEvents();
  rankTable('errors', '오류 갈래 @ 난 자리', data.errors, (r) => { const [type, at] = r.key.split('@'); return esc(type) + (at ? ' <span class="dim">@ ' + esc(at) + '</span>' : ''); });

  $('days').innerHTML = '<tr><th class="name">날짜</th><th class="name">버전</th><th>설치</th><th>덩이</th></tr>'
    + (data.days.length ? data.days.map((d) => '<tr><td class="name">' + esc(d.day) + '</td><td class="name">' + esc(d.version) + '</td><td>' + n(d.installs) + '</td><td>' + n(d.batches) + '</td></tr>').join('') : EMPTY(4));
}

async function load() {
  $('error').textContent = '';
  try {
    const res = await fetch('/v1/dashboard?' + new URLSearchParams({ version: state.version }));
    if (!res.ok) throw new Error('HTTP ' + res.status);
    data = await res.json();
    const sel = $('version'), keep = state.version;
    sel.innerHTML = '<option value="">전체</option>' + data.versions.map((v) => '<option>' + esc(v) + '</option>').join('');
    sel.value = keep;
    $('made').textContent = data.madeAt ? '집계 시각 ' + new Date(data.madeAt).toLocaleString('ko-KR') + ' · 한 시간마다 새로 셉니다' : '';
    render();
  } catch (e) { $('error').textContent = '불러오지 못했습니다: ' + e.message; }
}
$('version').onchange = (e) => { state.version = e.target.value; load(); };
load();
</script>
</body>
</html>`;
