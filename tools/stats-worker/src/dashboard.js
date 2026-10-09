// 대시보드 — /dashboard 가 이 HTML 을 그대로 내고, 페이지가 /v1/dashboard 에서 집계를 받아 그린다.
// 바깥 라이브러리 없이 표와 막대만 쓴다.

export const DASHBOARD_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>코스타 델 솔 — 놀이 통계</title>
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
  tr.chapter td { background: var(--bg); font-weight: 600; border-top: 2px solid var(--line); cursor: pointer; }
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
  <h1>코스타 델 솔 — 놀이 통계</h1>
  <div class="sub">익명으로 수집한 놀이 셈입니다. 모드 옵션은 설치마다 마지막 값이라 버전으로 거르지 않습니다.</div>
  <div class="filters">
    <label>버전 <select id="version"><option value="">전체</option></select></label>
    <span id="error"></span>
  </div>
  <div class="tiles" id="tiles"></div>
  <section><h2>모드 옵션 <small>설치별 마지막 값</small></h2><div class="tabs" id="modtabs"></div><div class="scroll"><table id="mods"></table></div></section>
  <div class="two">
    <section><h2>도시 순위</h2><div class="scroll"><table id="cities"></table></div></section>
    <section><h2>발견물 순위</h2><div class="scroll"><table id="discoveries"></table></div></section>
  </div>
  <section><h2>메뉴</h2><div class="tabs" id="menutabs"></div><div class="scroll"><table id="menus"></table></div></section>
  <section><h2>전투</h2><table id="battles"></table></section>
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
const LEVELS = ['InfoLevel', 'SeaRaidScale', 'MutinyRate', 'Resolution', 'PortDays', 'ScreenWidth', 'ScreenHeight', 'ScreenScale'];
const SEA = [['Won', '승리'], ['Defeated', '패배'], ['Escaped', '퇴각'], ['Surrendered', '항복']];

let data = null;
const state = { version: '' }, sorts = { cities: 'n', discoveries: 'n', menus: 'n', errors: 'n' }, closed = new Set();
let modTab = 'bool', menuTab = 'window';

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

function renderMods() {
  const bools = data.mods.filter((m) => !LEVELS.includes(m.name)), levels = data.mods.filter((m) => LEVELS.includes(m.name));
  tabs('modtabs', [['bool', '켬 · 끔 옵션 (' + bools.length + ')'], ['level', '단계 옵션 (' + levels.length + ')']], modTab, (k) => { modTab = k; });
  if (modTab === 'bool') {
    $('mods').innerHTML = '<tr><th>#</th><th class="name">옵션</th><th>켠 설치</th><th>설치</th><th>비율</th><th></th></tr>'
      + (bools.length ? bools.map((m, i) => '<tr><td class="dim">' + (i + 1) + '</td><td class="name">' + esc(m.name) + '</td><td>' + n(m.enabled) + '</td><td>' + n(m.installs) + '</td><td>'
          + pct(m.enabled, m.installs) + '</td>' + bar(m.enabled, m.installs) + '</tr>').join('') : EMPTY(6));
    return;
  }
  $('mods').innerHTML = '<tr><th class="name">옵션</th><th>설치</th><th>평균값</th><th class="name">값마다 설치 수</th></tr>'
    + (levels.length ? levels.map((m) => '<tr><td class="name">' + esc(m.name) + '</td><td>' + n(m.installs) + '</td><td>' + (Math.round(m.average * 100) / 100) + '</td><td class="name wrap">'
        + (data.modValues || []).filter((v) => v.name === m.name).map((v) => '<b>' + n(v.value) + '</b> <span class="dim">× ' + n(v.installs) + '</span>').join(' &nbsp;·&nbsp; ') + '</td></tr>').join('') : EMPTY(4));
}

// 메뉴는 「창 제목/줄 글」이라 창으로 묶는다 — 창 줄에는 그 창의 합계, 누르면 접힌다. 설치 수는 줄마다 센 것이라 창 줄에는 그중 가장 큰 값.
function renderMenus() {
  tabs('menutabs', [['window', '창별'], ['line', '줄별']], menuTab, (k) => { menuTab = k; });
  if (menuTab === 'line') { rankTable('menus', '창 / 줄', data.menus, (r) => esc(r.key)); return; }
  const k = sorts.menus, groups = new Map();
  for (const m of data.menus) {
    const cut = m.key.indexOf('/'), win = cut < 0 ? '(창 제목 없음)' : m.key.slice(0, cut).trim(), line = cut < 0 ? m.key : m.key.slice(cut + 1);
    if (!groups.has(win)) groups.set(win, { n: 0, installs: 0, rows: [] });
    const g = groups.get(win);
    g.n += m.n; g.installs = Math.max(g.installs, m.installs); g.rows.push({ ...m, line });
  }
  const list = [...groups.entries()].sort((a, b) => b[1][k] - a[1][k] || b[1].n - a[1].n);
  const top = Math.max(1, ...list.map(([, g]) => g[k]));
  $('menus').innerHTML = '<tr><th class="name">창 · 줄</th>' + sortHead('menus') + '<th></th></tr>'
    + (list.length ? list.map(([win, g]) => '<tr class="chapter" data-win="' + esc(win) + '"><td class="name">' + (closed.has(win) ? '▸ ' : '▾ ') + esc(win) + ' <span class="dim">' + g.rows.length + '줄</span></td><td>'
        + n(g.n) + '</td><td>' + n(g.installs) + '</td>' + bar(g[k], top) + '</tr>'
        + (closed.has(win) ? '' : g.rows.sort((a, b) => b[k] - a[k] || b.n - a.n).map((r) => '<tr><td class="name indent wrap">' + esc(r.line) + '</td><td>' + n(r.n) + '</td><td>' + n(r.installs) + '</td>' + bar(r[k], top) + '</tr>').join(''))).join('')
      : EMPTY(4));
  bindSort('menus');
  $('menus').querySelectorAll('tr.chapter').forEach((tr) => tr.onclick = () => { const w = tr.dataset.win; closed.has(w) ? closed.delete(w) : closed.add(w); render(); });
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

function render() {
  const kind = (k) => (data.kinds || []).find((r) => r.kind === k) || { n: 0, keys: 0 };
  $('tiles').innerHTML = [[data.total?.installs, '설치'], [data.total?.batches, '받은 덩이'], [kind('city').n, '도시 방문'], [kind('discovery').n, '발견'],
      [kind('menu').n, '메뉴 누름'], [kind('battle').n, '전투'], [kind('error').n, '오류']]
    .map(([v, label]) => '<div class="tile"><b>' + n(v) + '</b><span>' + label + '</span></div>').join('');

  renderMods();
  rankTable('cities', '도시', data.cities, (r) => esc(r.name || '#' + r.key));
  rankTable('discoveries', '발견물', data.discoveries, (r) => esc(r.name || '#' + r.key));
  renderMenus();
  renderBattles();
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
    render();
  } catch (e) { $('error').textContent = '불러오지 못했습니다: ' + e.message; }
}
$('version').onchange = (e) => { state.version = e.target.value; load(); };
load();
</script>
</body>
</html>`;
