// 대시보드 페이지 — /v1/dashboard 에서 집계를 받아 표로 그린다.
export const DASHBOARD_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>놀이 통계</title>
<style>
  :root { --bg: #f6f3ec; --fg: #2a2320; --dim: #7a6f66; --line: #d9d1c3; --card: #fffdf8; --bar: #b5553c; }
  @media (prefers-color-scheme: dark) {
    :root { --bg: #1c1917; --fg: #ece4d2; --dim: #a3978a; --line: #3a332e; --card: #26211e; --bar: #d97a5c; }
  }
  body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 14px/1.5 system-ui, sans-serif; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  h2 { font-size: 15px; margin: 0 0 8px; }
  .dim { color: var(--dim); }
  .head { display: flex; flex-wrap: wrap; gap: 12px; align-items: baseline; margin-bottom: 16px; }
  select { font: inherit; padding: 2px 6px; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 16px; }
  section { background: var(--card); border: 1px solid var(--line); border-radius: 6px; padding: 12px; min-width: 0; }
  .scroll { max-height: 420px; overflow: auto; }
  table { width: 100%; border-collapse: collapse; }
  th, td { text-align: left; padding: 3px 6px; border-bottom: 1px solid var(--line); white-space: nowrap; }
  th { position: sticky; top: 0; background: var(--card); color: var(--dim); font-weight: 600; }
  td.n, th.n { text-align: right; font-variant-numeric: tabular-nums; }
  td.k { white-space: normal; word-break: break-all; }
  .bar { display: inline-block; height: 8px; background: var(--bar); border-radius: 2px; vertical-align: middle; }
</style>
</head>
<body>
<div class="head">
  <h1>놀이 통계</h1>
  <span id="total" class="dim"></span>
  <label>버전 <select id="version"><option value="">전체</option></select></label>
</div>
<div class="grid">
  <section><h2>전투</h2><div class="scroll" id="battles"></div></section>
  <section><h2>모드 옵션 <span class="dim">(설치별 마지막 값)</span></h2><div class="scroll" id="mods"></div></section>
  <section><h2>도시</h2><div class="scroll" id="cities"></div></section>
  <section><h2>발견물</h2><div class="scroll" id="discoveries"></div></section>
  <section><h2>메뉴</h2><div class="scroll" id="menus"></div></section>
  <section><h2>오류</h2><div class="scroll" id="errors"></div></section>
  <section><h2>날짜별</h2><div class="scroll" id="days"></div></section>
</div>
<script>
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const BATTLE = { 'sea:Won': '해전 · 승리', 'sea:Defeated': '해전 · 패배', 'sea:Escaped': '해전 · 퇴각', 'sea:Surrendered': '해전 · 항복',
                 'land:win': '육상전 · 승리', 'land:lose': '육상전 · 패배' };

function table(el, heads, rows) {
  if (!rows.length) { document.getElementById(el).innerHTML = '<p class="dim">아직 없습니다.</p>'; return; }
  document.getElementById(el).innerHTML = '<table><thead><tr>' +
    heads.map((h) => '<th class="' + (h.n ? 'n' : '') + '">' + esc(h.t) + '</th>').join('') + '</tr></thead><tbody>' +
    rows.map((r) => '<tr>' + r.map((c, i) => '<td class="' + (heads[i].n ? 'n' : 'k') + '">' + c + '</td>').join('') + '</tr>').join('') +
    '</tbody></table>';
}

function counted(el, label, rows, nameOf) {
  const max = Math.max(1, ...rows.map((r) => r.n));
  table(el, [{ t: label }, { t: '횟수', n: 1 }, { t: '설치', n: 1 }, { t: '' }],
    rows.map((r) => [esc(nameOf(r)), r.n, r.installs, '<span class="bar" style="width:' + Math.round(80 * r.n / max) + 'px"></span>']));
}

async function load() {
  const version = document.getElementById('version').value;
  const d = await (await fetch('/v1/dashboard' + (version ? '?version=' + encodeURIComponent(version) : ''))).json();

  const sel = document.getElementById('version');
  if (sel.options.length === 1) for (const v of d.versions) sel.add(new Option(v, v));
  document.getElementById('total').textContent = '설치 ' + (d.total?.installs ?? 0) + ' · 받은 덩이 ' + (d.total?.batches ?? 0);

  const sea = d.battles.filter((b) => b.key.startsWith('sea:')).reduce((s, b) => s + b.n, 0);
  const land = d.battles.filter((b) => b.key.startsWith('land:')).reduce((s, b) => s + b.n, 0);
  counted('battles', '전투 (해전 ' + sea + ' · 육상전 ' + land + ')', d.battles, (r) => BATTLE[r.key] ?? r.key);
  counted('cities', '도시', d.cities, (r) => r.name || '#' + r.key);
  counted('discoveries', '발견물', d.discoveries, (r) => r.name || '#' + r.key);
  counted('menus', '창 / 줄', d.menus, (r) => r.key);
  counted('errors', '오류 갈래 @ 난 자리', d.errors, (r) => r.key);

  table('mods', [{ t: '옵션' }, { t: '켠 설치', n: 1 }, { t: '설치', n: 1 }, { t: '비율', n: 1 }, { t: '평균값', n: 1 }],
    d.mods.map((m) => [esc(m.name), m.enabled, m.installs, Math.round(100 * m.enabled / Math.max(1, m.installs)) + '%',
                       Math.round(m.average * 100) / 100]));
  table('days', [{ t: '날짜' }, { t: '버전' }, { t: '설치', n: 1 }, { t: '덩이', n: 1 }],
    d.days.map((r) => [esc(r.day), esc(r.version), r.installs, r.batches]));
}

document.getElementById('version').addEventListener('change', load);
load();
</script>
</body>
</html>`;
