// 놀이 통계를 받는 Cloudflare Worker — 게임(CdsHelper.Game/Local/Helpers/PlayStats.cs)이 몇 분마다, 그리고 끌 때 셈 한 덩이를 보낸다.
//
//   POST /v1/play        셈 한 덩이(JSON)를 D1 에 넣는다. 같은 batch 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/dashboard   집계(?version= 으로 거른다) — 발견물 · 도시 · 메뉴 · 전투 · 오류 · 모드 옵션.
//   GET  /dashboard      대시보드 페이지(dashboard.js). 지금은 누구나 볼 수 있다.
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

import { DASHBOARD_HTML } from './dashboard.js';

const MAX_BODY = 128 * 1024, MAX_COUNTS = 2000, MAX_MODS = 300, MAX_KEY = 96;
const KINDS = ['discovery', 'city', 'menu', 'battle', 'error'];

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

  const row = env.DB.prepare(`INSERT INTO counts (batch, kind, key, name, n) VALUES (?, ?, ?, ?, ?)`);
  const rows = b.counts
    .filter((c) => c && typeof c === 'object' && KINDS.includes(c.kind) && typeof c.key === 'string' && c.key.length > 0)
    .map((c) => row.bind(b.batch, c.kind, text(c.key, MAX_KEY), text(c.name, MAX_KEY), int(c.n, 1_000_000)));

  // 모드 옵션은 설치마다 마지막 값 하나만 남긴다.
  if (b.mods && typeof b.mods === 'object' && !Array.isArray(b.mods)) {
    const mod = env.DB.prepare(
      `INSERT INTO mods (install, name, value, version, updated_at) VALUES (?, ?, ?, ?, ?)
       ON CONFLICT (install, name) DO UPDATE SET value = excluded.value, version = excluded.version, updated_at = excluded.updated_at`);
    for (const [name, value] of Object.entries(b.mods).slice(0, MAX_MODS))
      if (/^[A-Za-z0-9_]{1,48}$/.test(name) && Number.isFinite(value))
        rows.push(mod.bind(b.install, name, int(value, 1_000_000), version, now));
  }

  // D1 은 한 번에 묶는 수에 끝이 있어 백 줄씩 끊어 넣는다.
  for (let i = 0; i < rows.length; i += 100) await env.DB.batch(rows.slice(i, i + 100));
  return json({ ok: true });
}

// 대시보드 집계 — 버전(없으면 전체)으로 거른다. 모드 옵션은 설치마다 마지막 값이라 버전으로 안 거른다.
async function getDashboard(url, env) {
  const version = (url.searchParams.get('version') || '').slice(0, 32);
  const cond = version ? 'AND b.version = ?' : '', args = version ? [version] : [];
  const top = (kind, limit) => env.DB.prepare(
    `SELECT c.key, MAX(c.name) AS name, SUM(c.n) AS n, COUNT(DISTINCT b.install) AS installs
     FROM counts c JOIN batches b ON b.batch = c.batch
     WHERE c.kind = ? ${cond} GROUP BY c.key ORDER BY n DESC, c.key LIMIT ${limit}`)
    .bind(kind, ...args).all().then((r) => r.results);

  const [total, discoveries, cities, menus, battles, errors, mods, days, versions] = await Promise.all([
    env.DB.prepare(`SELECT COUNT(*) AS batches, COUNT(DISTINCT install) AS installs FROM batches b WHERE 1 = 1 ${cond}`).bind(...args).first(),
    top('discovery', 400), top('city', 300), top('menu', 300), top('battle', 50), top('error', 200),
    env.DB.prepare(
      `SELECT name, COUNT(*) AS installs, SUM(value <> 0) AS enabled, AVG(value) AS average
       FROM mods GROUP BY name ORDER BY enabled DESC, name`).all().then((r) => r.results),
    env.DB.prepare(
      `SELECT substr(b.received_at, 1, 10) AS day, b.version, COUNT(DISTINCT b.install) AS installs, COUNT(*) AS batches
       FROM batches b WHERE 1 = 1 ${cond} GROUP BY day, b.version ORDER BY day DESC, b.version DESC LIMIT 120`).bind(...args).all().then((r) => r.results),
    env.DB.prepare('SELECT DISTINCT version FROM batches ORDER BY version DESC LIMIT 50').all().then((r) => r.results.map((v) => v.version)),
  ]);
  return json({ total, discoveries, cities, menus, battles, errors, mods, days, versions });
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
