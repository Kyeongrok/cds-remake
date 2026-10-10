// 놀이 통계를 받는 Cloudflare Worker — 게임(CdsHelper.Game/Local/Helpers/PlayStats.cs)이 몇 분마다, 그리고 끌 때 셈 한 덩이를 보낸다.
//
//   POST /v1/play        셈 한 덩이(JSON)를 D1 의 합계 표(tally)에 더한다. 같은 batch 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/dashboard   집계(?version= 으로 거른다) — 발견물 · 도시 · 항해 · 네비게이션 · 전투 · 오류 · 모드 옵션. 한 시간에 한 번만 새로 센다.
//   GET  /dashboard      대시보드 페이지(dashboard.js). 지금은 누구나 볼 수 있다.
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

import { DASHBOARD_HTML } from './dashboard.js';

const MAX_BODY = 128 * 1024, MAX_COUNTS = 2000, MAX_MODS = 300, MAX_KEY = 96;
// 차림표 줄(menu)은 이제 안 받는다 — 옛 판이 보내도 여기서 걸러진다. 표에 남은 옛 줄은 집계에도 안 든다.
// 항해는 같은 열쇠(「떠난 도시>닿은 도시」)로 갈래 셋에 나눠 온다 — 횟수 · 날수의 합 · 선회의 합. 평균은 집계가 나눠서 낸다.
const KINDS = ['discovery', 'city', 'voyage', 'voyage_days', 'voyage_turns', 'nav', 'battle', 'error'];

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8' } });

const int = (v, max = 2_000_000_000) => (Number.isFinite(v) ? Math.max(0, Math.min(max, Math.trunc(v))) : 0);
const text = (v, max) => (typeof v === 'string' ? v.slice(0, max) : '');
const isId = (v) => typeof v === 'string' && /^[0-9a-f-]{32,36}$/i.test(v);

async function postPlay(request, env) {
  const raw = await request.text();
  if (raw.length > MAX_BODY) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || b.v !== 1 || !isId(b.batch) || !isId(b.install) || !Array.isArray(b.counts) || b.counts.length > MAX_COUNTS)
    return json({ error: 'bad batch' }, 400);

  const now = new Date().toISOString(), version = text(b.version, 32);
  const head = await env.DB.prepare(`INSERT OR IGNORE INTO batches (batch, received_at, install, version) VALUES (?, ?, ?, ?)`)
    .bind(b.batch, now, b.install, version).run();
  if (!head.meta.changes) return json({ ok: true, duplicate: true });   // 이미 받은 것 — 다시 보낸 것이다

  // 셈은 덩이마다 줄을 쌓지 않고 (갈래 · 열쇠 · 버전 · 설치)마다 한 줄에 더한다 — 대시보드가 훑을 줄이 날마다 늘지 않게.
  const counts = b.counts
    .filter((c) => c && typeof c === 'object' && KINDS.includes(c.kind) && typeof c.key === 'string' && c.key.length > 0)
    .map((c) => ({ kind: c.kind, key: text(c.key, MAX_KEY), name: text(c.name, MAX_KEY), n: int(c.n, 1_000_000) }));
  const rows = [];
  if (counts.length)
    rows.push(env.DB.prepare(
      `INSERT INTO tally (kind, key, version, install, name, n)
       SELECT json_extract(value, '$.kind'), json_extract(value, '$.key'), ?, ?, json_extract(value, '$.name'), json_extract(value, '$.n')
       FROM json_each(?) WHERE true
       ON CONFLICT (kind, key, version, install) DO UPDATE SET n = n + excluded.n, name = excluded.name`)
      .bind(version, b.install, JSON.stringify(counts)));

  // 모드 옵션은 설치마다 마지막 값 하나만 남긴다. 값이 그대로면 안 쓴다.
  if (b.mods && typeof b.mods === 'object' && !Array.isArray(b.mods)) {
    const mods = Object.entries(b.mods).slice(0, MAX_MODS)
      .filter(([name, value]) => /^[A-Za-z0-9_]{1,48}$/.test(name) && Number.isFinite(value))
      .map(([name, value]) => ({ name, value: int(value, 1_000_000) }));
    if (mods.length)
      rows.push(env.DB.prepare(
        `INSERT INTO mods (install, name, value, version, updated_at)
         SELECT ?, json_extract(value, '$.name'), json_extract(value, '$.value'), ?, ? FROM json_each(?) WHERE true
         ON CONFLICT (install, name) DO UPDATE SET value = excluded.value, version = excluded.version, updated_at = excluded.updated_at
         WHERE mods.value <> excluded.value`)
        .bind(b.install, version, now, JSON.stringify(mods)));
  }

  if (rows.length) await env.DB.batch(rows);
  return json({ ok: true });
}

// 대시보드 집계는 한 번 세면 이만큼 묵힌다 — 열 때마다 표를 훑으면 D1 무료 읽기 한도(하루 오백만 줄)가 금세 찬다.
const SNAPSHOT_TTL = 60 * 60 * 1000;
const LIMITS = { discovery: 400, city: 300, voyage: 400, voyage_days: 100000, voyage_turns: 100000, nav: 100, battle: 50, error: 200 };

// 대시보드 집계 — 버전(없으면 전체)으로 거른다. 모드 옵션은 설치마다 마지막 값이라 버전으로 안 거른다.
async function getDashboard(url, env) {
  const version = (url.searchParams.get('version') || '').slice(0, 32);
  const all = await snapshot(env, '', null);
  if (!version) return json(all);
  // 모르는 버전으로는 표를 안 훑는다 — 주소만 바꿔 가며 부르면 그때마다 새로 세게 된다.
  if (!all.versions.includes(version))
    return json({ ...all, total: { batches: 0, installs: 0 }, kinds: [], discoveries: [], cities: [], voyages: [], navs: [], battles: [], errors: [], days: [] });
  return json(await snapshot(env, version, all.versions));
}

// 묵혀 둔 집계를 내고, 없거나 낡았으면 새로 세어 적어 둔다.
async function snapshot(env, version, versions) {
  const kept = await env.DB.prepare('SELECT made_at, body FROM snapshots WHERE version = ?').bind(version).first();
  if (kept && Date.now() - Date.parse(kept.made_at) < SNAPSHOT_TTL) return JSON.parse(kept.body);

  const body = await aggregate(env, version, versions);
  await env.DB.prepare('INSERT OR REPLACE INTO snapshots (version, made_at, body) VALUES (?, ?, ?)')
    .bind(version, body.madeAt, JSON.stringify(body)).run();
  return body;
}

// 표를 훑는 것은 여기뿐이다 — 셈 표 한 번, 모드 표 한 번, 덩이 표 두 번(전체일 때는 버전 목록까지 세 번).
async function aggregate(env, version, versions) {
  const cond = version ? 'AND version = ?' : '', args = version ? [version] : [];
  const all = (sql, ...bound) => env.DB.prepare(sql).bind(...bound).all().then((r) => r.results);

  const [total, tally, modValues, days, known] = await Promise.all([
    env.DB.prepare(`SELECT COUNT(*) AS batches, COUNT(DISTINCT install) AS installs FROM batches WHERE 1 = 1 ${cond}`).bind(...args).first(),
    all(`SELECT kind, key, MAX(name) AS name, SUM(n) AS n, COUNT(DISTINCT install) AS installs
         FROM tally WHERE 1 = 1 ${cond} GROUP BY kind, key`, ...args),
    // 단계 옵션은 평균만으로는 안 보여 값마다 설치 수를 낸다.
    all('SELECT name, value, COUNT(*) AS installs FROM mods GROUP BY name, value ORDER BY name, value'),
    all(`SELECT substr(received_at, 1, 10) AS day, version, COUNT(DISTINCT install) AS installs, COUNT(*) AS batches
         FROM batches WHERE 1 = 1 ${cond} GROUP BY day, version ORDER BY day DESC, version DESC LIMIT 120`, ...args),
    versions ?? all('SELECT DISTINCT version FROM batches ORDER BY version DESC LIMIT 50').then((r) => r.map((v) => v.version)),
  ]);

  // 갈래마다 합계와 순위 — 순위는 줄 수에 끝이 있어, 타일의 셈은 자르기 전에 따로 낸다.
  const kinds = [], tops = {};
  for (const kind of KINDS) {
    const rows = tally.filter((r) => r.kind === kind).map(({ key, name, n, installs }) => ({ key, name, n, installs }));
    if (rows.length) kinds.push({ kind, n: rows.reduce((s, r) => s + r.n, 0), keys: rows.length });
    tops[kind] = rows.sort((a, b) => b.n - a.n || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0)).slice(0, LIMITS[kind]);
  }

  // 모드 옵션 — 값마다 설치 수에서 켠 설치 · 평균을 낸다.
  const byName = new Map();
  for (const v of modValues) {
    const m = byName.get(v.name) ?? { name: v.name, installs: 0, enabled: 0, sum: 0 };
    m.installs += v.installs; m.enabled += v.value ? v.installs : 0; m.sum += v.value * v.installs;
    byName.set(v.name, m);
  }
  const mods = [...byName.values()].map(({ sum, ...m }) => ({ ...m, average: sum / m.installs }))
    .sort((a, b) => b.enabled - a.enabled || (a.name < b.name ? -1 : 1));

  // 항해 — 구간마다 횟수에 날수 · 선회의 합을 붙인다.
  const sumOf = (kind) => new Map(tops[kind].map((r) => [r.key, r.n]));
  const dayOf = sumOf('voyage_days'), turnOf = sumOf('voyage_turns');
  const voyages = tops.voyage.map((r) => ({ ...r, days: dayOf.get(r.key) || 0, turns: turnOf.get(r.key) || 0 }));

  return { madeAt: new Date().toISOString(), total, kinds, discoveries: tops.discovery, cities: tops.city, voyages, navs: tops.nav,
           battles: tops.battle, errors: tops.error, mods, modValues, days, versions: known };
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url), { pathname } = url;
    try {
      if (request.method === 'POST' && pathname === '/v1/play') return await postPlay(request, env);
      if (request.method === 'GET' && pathname === '/v1/dashboard') return await getDashboard(url, env);
      if (request.method === 'GET' && (pathname === '/dashboard' || pathname === '/'))
        return new Response(DASHBOARD_HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      return json({ error: 'server error' }, 500);
    }
  },
};
