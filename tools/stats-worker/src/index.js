// 게임 통계를 받는 Cloudflare Worker — 게임(CdsHelper.Game/Local/Helpers/PlayStats.cs)이 몇 분마다, 그리고 끌 때 셈 한 덩이를 보낸다.
//
//   POST /v1/play        셈 한 덩이(JSON)를 D1 의 합계 표(tally)에 더한다. 같은 batch 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/dashboard   집계(?period=day|week|month|all · ?version= 으로 거른다) — 발견물 · 부하 고용 · 해고 · 항해 · 네비게이션 · 전투 · 사건 · 오류 · 모드 옵션. 한 시간에 한 번만 새로 센다.
//   GET  /dashboard      대시보드 페이지(dashboard.js). 지금은 누구나 볼 수 있다.
//   GET  /embed.js       대시보드 화면 부품 <cds-dashboard> — /dashboard 와 창세기전3 통계 대시보드가 불러 쓴다.
//   POST /v1/hardcore    하드코어 판 하나가 끝난 기록 — 판 표지(gameId)마다 한 번만 들어간다.
//   GET  /v1/ranking     하드코어 랭킹(발견물 수 차례) 상위 100.
//   GET  /ranking        하드코어 랭킹 페이지.
//   POST /v1/bug         버그 리포트 한 건(게임의 「버그 리포트」 창, CdsHelper.Game/Local/Helpers/BugReport.cs) — {id} 를 돌려준다. 설치마다 한 시간에 10 건까지.
//   GET  /v1/bugs?since=<id>&status=new&order=desc   받은 버그 리포트(관리자만 — Authorization: Bearer <BUG_ADMIN_KEY>)
//   POST /v1/bugs/<id>/status {status, note}         버그 리포트의 상태를 바꾼다(관리자만)
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다. 이름은 주인공 이름(heroes · hardcore)만 받는다.

import { DASHBOARD_HTML, EMBED_JS } from './dashboard.js';
import { RANKING_HTML } from './ranking.js';

const MAX_BODY = 128 * 1024, MAX_COUNTS = 2000, MAX_MODS = 300, MAX_KEY = 96;
// 차림표 줄(menu) · 들른 도시(city)는 이제 안 받는다 — 옛 판이 보내도 여기서 걸러진다. 표에 남은 옛 줄은 집계에도 안 든다.
// 항해(voyage · voyage_days · voyage_turns — 「떠난 도시>닿은 도시」 횟수 · 날수 · 선회)도 v1.1.31 부터 안 받는다. 옛 줄은 집계에 그대로 든다.
const KINDS = ['discovery', 'hire', 'dismiss', 'nav', 'battle', 'event', 'error'];
// 이제 안 받는 셈 — 메시지마다 끼어 D1 쓰기 몫만 먹었다(하루 십만 줄 한도의 8할). 답은 이미 나왔고, 옛 판이 보내도 여기서 걸러진다.
//   event 의 날 밑수(sea:day · sea:eligible · land:day) · nav 의 수동/자동(trip:* — 항해 횟수와 겹친다) · 조타(helm:*) · 건 자리(from:*)
const DROPPED = (kind, key) =>
  (kind === 'event' && (key === 'sea:day' || key === 'sea:eligible' || key === 'land:day'))
  || (kind === 'nav' && /^(trip|helm|from):/.test(key));

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
  // 덩이 줄과 셈은 한 묶음(트랜잭션)으로 쓴다 — 셈이 안 들어갔는데 덩이 줄만 남으면, 게임이 다시 보내도 「이미 받았다」가 되어 그 셈을 잃는다
  // (D1 하루 쓰기 한도가 찼을 때 그랬다). 이미 받은 덩이면 셈 · 옵션은 고르는 줄이 없어 안 쓰이고, 덩이 줄은 맨 끝에 넣는다.
  const fresh = 'NOT EXISTS (SELECT 1 FROM batches WHERE batch = ?)';
  // 셈은 덩이마다 줄을 쌓지 않고 (갈래 · 열쇠 · 버전 · 설치)마다 한 줄에 더한다 — 대시보드가 훑을 줄이 날마다 늘지 않게.
  // days · turns 칸은 옛 항해 줄의 것이다 — 이제 받는 갈래는 0 으로 둔다.
  const merged = new Map();
  for (const c of b.counts) {
    if (!c || typeof c !== 'object' || !KINDS.includes(c.kind) || typeof c.key !== 'string' || c.key.length === 0) continue;
    if (DROPPED(c.kind, c.key)) continue;
    const kind = c.kind, key = text(c.key, MAX_KEY);
    const id = kind + '\u0000' + key;
    const row = merged.get(id) ?? { kind, key, name: '', n: 0, days: 0, turns: 0 };
    row.n += int(c.n, 1_000_000);
    row.name = text(c.name, MAX_KEY) || row.name;
    merged.set(id, row);
  }
  const counts = [...merged.values()];
  const rows = [];
  if (counts.length)
    rows.push(env.DB.prepare(
      `INSERT INTO tally (kind, key, version, install, name, n, days, turns)
       SELECT json_extract(value, '$.kind'), json_extract(value, '$.key'), ?, ?, json_extract(value, '$.name'), json_extract(value, '$.n'),
              json_extract(value, '$.days'), json_extract(value, '$.turns')
       FROM json_each(?) WHERE ${fresh}
       ON CONFLICT (kind, key, version, install) DO UPDATE SET n = n + excluded.n, days = days + excluded.days,
                                                              turns = turns + excluded.turns, name = excluded.name`)
      .bind(version, b.install, JSON.stringify(counts), b.batch));
  // 같은 셈을 받은 날(UTC)마다도 더한다 — 대시보드의 하루 · 주 · 달 거르기가 이것을 센다. tally 에는 날짜가 없다.
  // 기간 거르기는 길어야 한 달이라 PERIOD_KEEP_DAYS 가 지난 줄은 집계할 때 지운다(pruneDaily).
  if (counts.length)
    rows.push(env.DB.prepare(
      `INSERT INTO tally_daily (day, kind, key, version, install, name, n)
       SELECT ?, json_extract(value, '$.kind'), json_extract(value, '$.key'), ?, ?, json_extract(value, '$.name'), json_extract(value, '$.n')
       FROM json_each(?) WHERE ${fresh}
       ON CONFLICT (day, kind, key, version, install) DO UPDATE SET n = n + excluded.n, name = excluded.name`)
      .bind(now.slice(0, 10), version, b.install, JSON.stringify(counts), b.batch));

  // 모드 옵션은 설치마다 마지막 값 한 벌을 한 줄(JSON)로 남긴다. 그대로면 안 쓴다.
  // 예전에는 옵션마다 한 줄(mods 표)이라 새 설치 하나에 백 줄 넘게 쓰였고, 그것으로 D1 하루 쓰기 한도(십만 줄)가 찼다.
  if (b.mods && typeof b.mods === 'object' && !Array.isArray(b.mods)) {
    const mods = Object.entries(b.mods).slice(0, MAX_MODS)
      .filter(([name, value]) => /^[A-Za-z0-9_]{1,48}$/.test(name) && Number.isFinite(value))
      .map(([name, value]) => [name, int(value, 1_000_000)])
      .sort((a, b) => (a[0] < b[0] ? -1 : 1));   // 차례를 맞춰야 「그대로」를 글로 견줄 수 있다
    if (mods.length)
      rows.push(env.DB.prepare(
        `INSERT INTO options (install, version, updated_at, body)
         SELECT ?, ?, ?, ? WHERE ${fresh}
         ON CONFLICT (install) DO UPDATE SET version = excluded.version, updated_at = excluded.updated_at, body = excluded.body
         WHERE options.body <> excluded.body`)
        .bind(b.install, version, now, JSON.stringify(Object.fromEntries(mods)), b.batch));
  }

  // 주인공은 판(game)마다 한 줄 — 처음 받은 값(first_*)은 두고 마지막 값만 덮는다. 게임은 이름 · 나라 · 능력치 · 기함 · 투자가 바뀌었을 때만 싣는다.
  // 기함 선체 · 투자는 v1.1.57 부터 온다(migrate-invest.sql) — 처음 기함(first_flagship)도 남긴다.
  // 남의 판 표지로 덮지 못하게 같은 설치일 때만 고친다.
  const hero = heroOf(b.hero);
  if (hero)
    rows.push(env.DB.prepare(
      `INSERT INTO heroes (game_id, install, version, first_at, updated_at, name, nation, first_date, date, first_abilities, abilities,
                          first_flagship, flagship, invested, investments)
       SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ? WHERE ${fresh}
       ON CONFLICT (game_id) DO UPDATE SET version = excluded.version, updated_at = excluded.updated_at, name = excluded.name,
                                          nation = excluded.nation, date = excluded.date, abilities = excluded.abilities,
                                          first_flagship = CASE WHEN heroes.first_flagship = '' THEN excluded.flagship ELSE heroes.first_flagship END,
                                          flagship = excluded.flagship, invested = excluded.invested, investments = excluded.investments
       WHERE heroes.install = excluded.install`)
      .bind(hero.game, b.install, version, now, now, hero.name, hero.nation, hero.date, hero.date, hero.abilities, hero.abilities,
            hero.flagship, hero.flagship, hero.invested, hero.investments, b.batch));

  // 날마다 설치 하나에 한 줄 — 대시보드의 설치 수 · 덩이 수 · 날짜별 표 · 버전 목록은 batches 대신 이 작은 표를 센다.
  rows.push(env.DB.prepare(
    `INSERT INTO daily (day, version, install, batches) SELECT ?, ?, ?, 1 WHERE ${fresh}
     ON CONFLICT (day, version, install) DO UPDATE SET batches = batches + 1`)
    .bind(now.slice(0, 10), version, b.install, b.batch));

  rows.push(env.DB.prepare(`INSERT OR IGNORE INTO batches (batch, received_at, install, version) VALUES (?, ?, ?, ?)`)
    .bind(b.batch, now, b.install, version));
  const done = await env.DB.batch(rows);
  return json(done[done.length - 1].meta.changes ? { ok: true } : { ok: true, duplicate: true });   // 안 들어갔으면 다시 보낸 것이다
}

// 주인공 한 벌 — 판 표지 · 이름 · 나라 · 게임 날짜 · 보이는 능력치 여섯(체력 · 지력 · 무력 · 매력 · 운 · 신앙심, 1~100). 말이 안 되면 버린다.
function heroOf(h) {
  if (!h || typeof h !== 'object' || !isId(h.game)) return null;
  const name = text(h.name, 32).trim(), date = text(h.date, 10);
  if (!name || !/^1[4-9]\d\d-\d\d-\d\d$/.test(date) || !Array.isArray(h.abilities) || h.abilities.length !== 6
      || !h.abilities.every((v) => Number.isInteger(v) && v >= 1 && v <= 100)) return null;
  return { game: h.game, name, nation: text(h.nation, 32), date, abilities: JSON.stringify(h.abilities),
           flagship: text(h.flagship, 32).trim(), ...investOf(h.investments) };
}

// 교역소 투자 — {도시 이름: 누적 금액}. 옛 판은 안 싣는다(합 0 · 빈 표). 도시는 MAX_INVEST_CITIES 곳, 금액은 1 ~ MAX_INVEST 만 받는다.
const MAX_INVEST_CITIES = 30, MAX_INVEST = 1e10;
function investOf(v) {
  const kept = {};
  if (v && typeof v === 'object' && !Array.isArray(v))
    for (const [city, amount] of Object.entries(v).slice(0, MAX_INVEST_CITIES)) {
      const name = text(city, 24).trim();
      if (name && Number.isSafeInteger(amount) && amount > 0 && amount <= MAX_INVEST) kept[name] = (kept[name] || 0) + amount;
    }
  return { invested: Object.values(kept).reduce((s, a) => s + a, 0), investments: JSON.stringify(kept) };
}

// 하드코어 판 하나가 끝났다 — 이름은 NEW GAME 에서 지은 주인공 이름이다. 판마다 한 줄만 받고, 말이 안 되는 값(발견물 수 · 날짜)은 거른다.
// 기록은 게임에 박힌 열쇠로 서명되어 온다(HMAC-SHA256, CdsHelper.Game/Engine/HardcoreRules.cs) — 이 Worker 의 비밀 HARDCORE_KEY 로 대 본다.
// marks 는 그 판이 한 번이라도 켠 · 줄인 모드와 쓴 개발 기능이다(「tampered」는 세이브 봉인이 안 맞았다는 뜻).
const MAX_DISCOVERIES = 400, MAX_NAME = 32, MAX_MARKS = 64, MARK = /^[A-Za-z0-9_:=\-\uAC00-\uD7A3]{1,48}$/;

async function hmacHex(key, message) {
  const enc = new TextEncoder();
  const k = await crypto.subtle.importKey('raw', enc.encode(key), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const sig = new Uint8Array(await crypto.subtle.sign('HMAC', k, enc.encode(message)));
  return [...sig].map((x) => x.toString(16).padStart(2, '0')).join('');
}

async function postHardcore(request, env) {
  const raw = await request.text();
  if (raw.length > 4096) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  const name = text(b && b.name, MAX_NAME).trim(), died = text(b && b.died, 10);
  if (!b || b.v !== 1 || !isId(b.gameId) || !isId(b.install) || !name
      || !Number.isInteger(b.discoveries) || b.discoveries < 0 || b.discoveries > MAX_DISCOVERIES
      || !/^1[4-9]\d\d-\d\d-\d\d$/.test(died) || died < '1480-01-01'
      || !Array.isArray(b.marks) || b.marks.length > MAX_MARKS || !b.marks.every((m) => typeof m === 'string' && MARK.test(m))
      || typeof b.sig !== 'string')
    return json({ error: 'bad record' }, 400);

  // 게임과 같은 차례로 이어 붙여 서명을 대 본다 — 열쇠가 없으면(아직 안 걸었으면) 아무것도 안 받는다.
  if (!env.HARDCORE_KEY) return json({ error: 'not configured' }, 503);
  const nation = text(b.nation, MAX_NAME), cause = int(b.cause, 99);
  const marks = [...new Set(b.marks)].sort((x, y) => (x < y ? -1 : x > y ? 1 : 0));
  const want = await hmacHex(env.HARDCORE_KEY, [b.gameId, name, nation, b.discoveries, died, cause, marks.join(',')].join('|'));
  if (want !== b.sig.toLowerCase()) return json({ error: 'bad signature' }, 400);

  const done = await env.DB.prepare(
    `INSERT OR IGNORE INTO hardcore (game_id, install, version, name, nation, discoveries, died, cause, marks, received_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .bind(b.gameId, b.install, text(b.version, 32), name, nation, b.discoveries, died, cause, JSON.stringify(marks),
          new Date().toISOString())
    .run();
  return json(done.meta.changes ? { ok: true } : { ok: true, duplicate: true });
}

// 하드코어 랭킹 — 색인(hardcore_rank)을 따라 앞 100줄만 읽는다.
async function getRanking(env) {
  const rows = await env.DB.prepare(
    `SELECT name, nation, discoveries, died, cause, marks, version, received_at AS at
     FROM hardcore ORDER BY discoveries DESC, died LIMIT 100`).all();
  return json({ ranking: rows.results.map((r) => ({ ...r, marks: JSON.parse(r.marks) })) });
}

// ── 버그 리포트 ─────────────────────────────────────────────────────────────
// 게임이 보내는 꼴: { v: 1, install, version, text, context: {...} }. context 는 게임이 붙인 것(버전 · OS · 화면 · 지금 자리 · 모드 · 오류 기록 끝).
const BUG_MAX_BODY = 48 * 1024, BUG_MAX_TEXT = 8000, BUG_MAX_CONTEXT = 32 * 1024;
const BUG_PER_HOUR = 10, BUG_ALL_PER_HOUR = 300;   // 설치 하나 · 모두 합쳐 한 시간에 받는 수

async function postBug(request, env) {
  const raw = await request.text();
  if (raw.length > BUG_MAX_BODY) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || b.v !== 1 || !isId(b.install) || typeof b.text !== 'string') return json({ error: 'bad report' }, 400);
  const body = b.text.trim();
  if (!body) return json({ error: 'empty' }, 400);
  if (body.length > BUG_MAX_TEXT) return json({ error: 'text too long' }, 413);
  const context = JSON.stringify(b.context && typeof b.context === 'object' && !Array.isArray(b.context) ? b.context : {});
  if (context.length > BUG_MAX_CONTEXT) return json({ error: 'context too large' }, 413);

  const hourAgo = new Date(Date.now() - 3600_000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS n, COALESCE(SUM(install = ?), 0) AS mine FROM bug_reports WHERE created_at > ?`).bind(b.install, hourAgo).first();
  if (recent.mine >= BUG_PER_HOUR || recent.n >= BUG_ALL_PER_HOUR) return json({ error: 'rate limited' }, 429);

  const row = await env.DB.prepare(
    `INSERT INTO bug_reports (created_at, version, install, text, context) VALUES (?, ?, ?, ?, ?) RETURNING id`)
    .bind(new Date().toISOString(), text(b.version, 32), b.install, body, context).first();
  return json({ ok: true, id: row.id });
}

// 관리자 키 — Worker 비밀(wrangler secret put BUG_ADMIN_KEY). 없으면 아무도 못 본다. 견줄 때 걸린 시간으로 새지 않게 끝까지 본다.
function isAdmin(request, env) {
  const key = env.BUG_ADMIN_KEY;
  const got = (request.headers.get('authorization') || '').replace(/^Bearer\s+/i, '');
  if (typeof key !== 'string' || key.length < 16 || got.length !== key.length) return false;
  let diff = 0;
  for (let i = 0; i < key.length; i++) diff |= key.charCodeAt(i) ^ got.charCodeAt(i);
  return diff === 0;
}

const STATUS = /^[a-z][a-z_-]{0,19}$/;

async function listBugs(url, env) {
  const since = int(Number(url.searchParams.get('since') || 0));
  const status = url.searchParams.get('status') || 'new';
  const limit = Math.max(1, Math.min(200, int(Number(url.searchParams.get('limit') || 100))));
  if (status !== 'all' && !STATUS.test(status)) return json({ error: 'bad status' }, 400);
  const sql = `SELECT id, created_at, version, install, text, context, status, note FROM bug_reports WHERE id > ?`
    + (status === 'all' ? '' : ' AND status = ?') + (url.searchParams.get('order') === 'desc' ? ' ORDER BY id DESC LIMIT ?' : ' ORDER BY id LIMIT ?');   // desc — 대시보드는 새 것부터
  const args = status === 'all' ? [since, limit] : [since, status, limit];
  const { results } = await env.DB.prepare(sql).bind(...args).all();
  const bugs = results.map((r) => {
    let context = {};
    try { context = JSON.parse(r.context); } catch { context = { raw: r.context }; }
    return { ...r, context };
  });
  return json({ bugs });
}

async function setBugStatus(id, request, env) {
  let b;
  try { b = JSON.parse(await request.text()); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || typeof b.status !== 'string' || !STATUS.test(b.status)) return json({ error: 'bad status' }, 400);
  const note = typeof b.note === 'string' ? b.note.slice(0, 2000) : null;
  const done = await env.DB.prepare(`UPDATE bug_reports SET status = ?, note = COALESCE(?, note) WHERE id = ?`).bind(b.status, note, id).run();
  if (!done.meta.changes) return json({ error: 'not found' }, 404);
  return json({ ok: true, id, status: b.status });
}

// 대시보드 집계는 한 번 세면 이만큼 묵힌다 — 열 때마다 표를 훑으면 D1 무료 읽기 한도(하루 오백만 줄)가 금세 찬다.
const SNAPSHOT_TTL = 60 * 60 * 1000;
const LIMITS = { discovery: 400, hire: 300, dismiss: 300, voyage: 400, nav: 100, battle: 50, event: 100, error: 200 };
// 집계가 읽는 갈래 — tally 의 주키가 갈래로 시작하므로 이렇게 거르면 옛 voyage_days · voyage_turns · city 줄은 아예 안 읽는다.
// 옛 항해 날수 · 선회는 voyage_legacy 에 버전마다 한 줄로 묶어 두었다(migrate-reads.sql).
const READ_KINDS = Object.keys(LIMITS);
const HERO_LIMIT = 300;
// 대시보드 기간 — 받은 날(UTC) 기준으로 오늘을 넣어 며칠 거슬러 오르는지. all 은 거르지 않는다(tally 를 센다).
// 하루는 어제부터다 — 날이 UTC 로 바뀌어(한국 오전 9시) 오늘만 보면 아침에는 거의 비어 있다.
const PERIODS = { day: 1, week: 7, month: 30, all: 0 };
const DEFAULT_PERIOD = 'week';
// tally_daily 를 남겨 두는 날수 — 가장 긴 기간(달)보다 조금 길게.
const PERIOD_KEEP_DAYS = 35;
const sinceDay = (days) => new Date(Date.now() - days * 86400_000).toISOString().slice(0, 10);

// 대시보드 집계 — 기간과 버전(없으면 전체)으로 거른다. 모드 옵션은 설치마다 마지막 값이라 버전으로 안 거른다.
async function getDashboard(url, env) {
  const version = (url.searchParams.get('version') || '').slice(0, 32);
  const asked = url.searchParams.get('period') || DEFAULT_PERIOD;
  const period = Object.hasOwn(PERIODS, asked) ? asked : DEFAULT_PERIOD;
  const all = await snapshot(env, period, '', null);
  if (!version) return json(all);
  // 모르는 버전으로는 표를 안 훑는다 — 주소만 바꿔 가며 부르면 그때마다 새로 세게 된다.
  if (!all.versions.includes(version))
    return json({ ...all, total: { batches: 0, installs: 0 }, kinds: [], discoveries: [], hires: [], dismisses: [], voyages: [], navs: [], battles: [], events: [], errors: [], days: [], periods: { day: [], week: [], month: [] }, heroes: [],
                  nations: [], flagships: [], investCities: [] });
  return json(await snapshot(env, period, version, all));
}

// 묵혀 둔 집계를 내고, 없거나 낡았으면 새로 세어 적어 둔다. 전체 기간은 예전처럼 버전 글 그대로, 다른 기간은 「기간:버전」을 열쇠로 둔다.
async function snapshot(env, period, version, whole) {
  const slot = period === 'all' ? version : period + ':' + version;
  const kept = await env.DB.prepare('SELECT made_at, body FROM snapshots WHERE version = ?').bind(slot).first();
  if (kept && Date.now() - Date.parse(kept.made_at) < SNAPSHOT_TTL) return JSON.parse(kept.body);

  // 새로 세거나 적어 두다 막히면(D1 하루 한도) 대시보드째 죽지 않게 한다 — 못 세면 낡은 것이라도 내고, 못 적으면 센 것만 낸다.
  let body;
  try { body = await aggregate(env, period, version, whole); }
  catch (e) { if (!kept) throw e; console.error(e && e.stack || e); return JSON.parse(kept.body); }
  try {
    await env.DB.prepare('INSERT OR REPLACE INTO snapshots (version, made_at, body) VALUES (?, ?, ?)')
      .bind(slot, body.madeAt, JSON.stringify(body)).run();
  } catch (e) { console.error(e && e.stack || e); }
  return body;
}

// 표를 훑는 것은 여기뿐이다 — 셈 표 한 번(읽는 갈래만), 날마다 표 세 번, 전체일 때는 모드 표까지.
// D1 은 훑은 줄을 모두 읽기로 센다. 예전에는 덩이 표(batches)를 세 번, 모드 표를 json_each 로 펼쳐(설치 × 옵션 수) 세어
// 한 번 셀 때 오십만 줄 가까이 읽었다 — 그래서 덩이 표 대신 daily, 모드는 몸통만 읽어 여기서 센다.
// 모드 옵션 · 버전 목록은 버전으로 안 거르므로, 버전 집계는 전체 집계(whole)의 것을 그대로 쓴다 — 모드 표를 버전마다 또 훑지 않게.
async function aggregate(env, period, version, whole) {
  const cond = version ? 'AND version = ?' : '', args = version ? [version] : [];
  const all = (sql, ...bound) => env.DB.prepare(sql).bind(...bound).all().then((r) => r.results);
  // 기간 — 설치 수 · 덩이(daily)는 받은 날로, 셈은 tally_daily 로, 주인공은 마지막으로 받은 때로, 모드 옵션은 그 기간에 보낸 설치로 거른다.
  // 날짜별 탭(days · periods)은 제 날짜 거르기가 있어 기간으로 안 거른다.
  const since = PERIODS[period] ? sinceDay(PERIODS[period]) : '';
  const dayCond = since ? 'AND day >= ?' : '', dayArgs = since ? [since] : [];
  if (period === 'month' && !version) await pruneDaily(env);

  const [total, tally, legacy, modValues, days, known, heroes, byDay, byWeek, byMonth] = await Promise.all([
    env.DB.prepare(`SELECT COALESCE(SUM(batches), 0) AS batches, COUNT(DISTINCT install) AS installs FROM daily WHERE 1 = 1 ${dayCond} ${cond}`)
      .bind(...dayArgs, ...args).first(),
    since
      ? all(`SELECT kind, key, MAX(name) AS name, SUM(n) AS n, 0 AS days, 0 AS turns, COUNT(DISTINCT install) AS installs
             FROM tally_daily WHERE day >= ? AND kind IN (${READ_KINDS.map(() => '?').join(', ')}) ${cond} GROUP BY kind, key`, since, ...READ_KINDS, ...args)
      : all(`SELECT kind, key, MAX(name) AS name, SUM(n) AS n, SUM(days) AS days, SUM(turns) AS turns, COUNT(DISTINCT install) AS installs
             FROM tally WHERE kind IN (${READ_KINDS.map(() => '?').join(', ')}) ${cond} GROUP BY kind, key`, ...READ_KINDS, ...args),
    // 옛 항해 날수 · 선회는 v1.1.30 까지 받은 것이라 기간을 거르면 안 든다.
    since ? [] : all(`SELECT body FROM voyage_legacy WHERE 1 = 1 ${cond}`, ...args),
    whole ? whole.modValues
      : since ? all('SELECT body FROM options WHERE install IN (SELECT install FROM daily WHERE day >= ?)', since).then(countModValues)
      : all('SELECT body FROM options').then(countModValues),
    all(`SELECT day, version, COUNT(*) AS installs, SUM(batches) AS batches
         FROM daily WHERE 1 = 1 ${cond} GROUP BY day, version ORDER BY day DESC, version DESC LIMIT 120`, ...args),
    whole ? whole.versions : all('SELECT DISTINCT version FROM daily ORDER BY version DESC LIMIT 50').then((r) => r.map((v) => v.version)),
    // 주인공 — 마지막으로 받은 차례로 앞 HERO_LIMIT 판만(색인 heroes_recent 를 따라 그만큼만 읽는다). 버전은 마지막으로 보낸 판의 것이다.
    all(`SELECT name, nation, first_date, date, first_abilities, abilities, first_flagship, flagship, invested, version, updated_at
         FROM heroes WHERE updated_at >= ? ${cond} ORDER BY updated_at DESC LIMIT ${HERO_LIMIT}`, since, ...args)
      .then((r) => r.map(({ first_abilities, abilities, ...h }) => ({ ...h, first: JSON.parse(first_abilities), now: JSON.parse(abilities) }))),
    // 날짜별 탭의 묶음 — 하루 · 주(월요일 시작) · 달마다 설치 수(겹치지 않게) · 덩이 수. daily 의 day 는 UTC 날짜다.
    ...[['day', 400], [`date(day, 'weekday 0', '-6 days')`, 200], ['substr(day, 1, 7)', 120]].map(([key, limit]) =>
      all(`SELECT ${key} AS key, COUNT(DISTINCT install) AS installs, SUM(batches) AS batches
           FROM daily WHERE 1 = 1 ${cond} GROUP BY 1 ORDER BY 1 DESC LIMIT ${limit}`, ...args)),
  ]);
  const { nations, flagships, investCities } = await nationsOf(all, 'AND updated_at >= ? ' + cond, [since, ...args]);

  // 갈래마다 합계와 순위 — 순위는 줄 수에 끝이 있어, 타일의 셈은 자르기 전에 따로 낸다.
  const kinds = [], tops = {};
  for (const kind of READ_KINDS) {
    const rows = tally.filter((r) => r.kind === kind)
      .map(({ key, name, n, days, turns, installs }) => (kind === 'voyage' ? { key, name, n, days, turns, installs } : { key, name, n, installs }));
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

  // 항해 — 구간마다 횟수에 날수 · 선회의 합을 붙인다. 새 줄은 voyage 줄의 days · turns 칸에, 옛 줄은 voyage_legacy 에 {구간: [날수, 선회]} 로 있다.
  const old = new Map();
  for (const { body } of legacy)
    for (const [key, [d, t]] of Object.entries(JSON.parse(body))) {
      const s = old.get(key) ?? [0, 0];
      s[0] += d; s[1] += t;
      old.set(key, s);
    }
  const voyages = tops.voyage.map((r) => ({ ...r, days: (r.days || 0) + (old.get(r.key)?.[0] || 0), turns: (r.turns || 0) + (old.get(r.key)?.[1] || 0) }));

  return { madeAt: new Date().toISOString(), period, since, total, kinds, discoveries: tops.discovery, hires: tops.hire, dismisses: tops.dismiss, voyages, navs: tops.nav,
           battles: tops.battle, events: tops.event, errors: tops.error, mods, modValues, days, periods: { day: byDay, week: byWeek, month: byMonth },
           versions: known, heroes,
           nations, flagships, investCities };
}

// 기간 거르기가 안 쓰는 날의 셈을 지운다 — 한 달 집계를 새로 셀 때(한 시간에 한 번 넘지 않게) 같이 한다.
async function pruneDaily(env) {
  try { await env.DB.prepare('DELETE FROM tally_daily WHERE day < ?').bind(sinceDay(PERIOD_KEEP_DAYS)).run(); }
  catch (e) { console.error(e && e.stack || e); }
}

// 나라 · 기함 · 투자 — 판마다 한 줄인 heroes 를 통째로 센다(한 시간에 한 번). 기함 · 투자는 v1.1.57 부터 온 판에만 있다.
//   nations      나라마다 판 수 · 투자한 판 수 · 투자 합 · 가장 많이 넣은 판
//   flagships    지금 기함 선체마다 판 수(나라별로 쪼개어)
//   investCities 투자한 도시마다 판 수 · 합(investments 몸통을 여기서 펼친다 — json_each 로 펼치면 펼친 줄이 다 읽기로 센다)
async function nationsOf(all, cond, args) {
  const [byNation, byHull, bodies] = await Promise.all([
    all(`SELECT nation, COUNT(*) AS games, SUM(invested > 0) AS investing, SUM(invested) AS invested, MAX(invested) AS top
         FROM heroes WHERE 1 = 1 ${cond} GROUP BY nation ORDER BY games DESC`, ...args),
    all(`SELECT flagship, nation, COUNT(*) AS games FROM heroes WHERE flagship <> '' ${cond} GROUP BY flagship, nation`, ...args),
    all(`SELECT investments FROM heroes WHERE invested > 0 ${cond}`, ...args),
  ]);
  const hulls = new Map();
  for (const { flagship, nation, games } of byHull) {
    const h = hulls.get(flagship) ?? { flagship, games: 0, nations: {} };
    h.games += games; h.nations[nation || '-'] = games;
    hulls.set(flagship, h);
  }
  const cities = new Map();
  for (const { investments } of bodies)
    for (const [city, amount] of Object.entries(JSON.parse(investments || '{}'))) {
      const c = cities.get(city) ?? { city, games: 0, invested: 0 };
      c.games++; c.invested += amount;
      cities.set(city, c);
    }
  return {
    nations: byNation,
    flagships: [...hulls.values()].sort((a, b) => b.games - a.games),
    investCities: [...cities.values()].sort((a, b) => b.invested - a.invested).slice(0, 100),
  };
}

// 단계 옵션은 평균만으로는 안 보여 값마다 설치 수를 낸다 — 설치마다 한 줄인 몸통(JSON)을 읽어 여기서 센다.
// SQL 의 json_each 로 펼치면 펼친 줄(설치 × 옵션 수)이 모두 읽기로 세어진다.
function countModValues(rows) {
  const tally = new Map();
  for (const { body } of rows) {
    let mods;
    try { mods = JSON.parse(body); } catch { continue; }
    for (const [name, value] of Object.entries(mods)) {
      const id = name + '\u0000' + value;
      const v = tally.get(id) ?? { name, value, installs: 0 };
      v.installs++;
      tally.set(id, v);
    }
  }
  return [...tally.values()].sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : a.value - b.value));
}

// 다른 곳에서 부르는 대시보드 — 창세기전3 통계 대시보드가 대항해시대 화면을 제 페이지 안에 넣고 이 API 를 부른다.
// 버그 리포트는 관리자 키를 머리(Authorization)에 실어 보내므로, 미리 묻는 요청(OPTIONS)에 답하고 아는 곳만 받아 준다.
const CORS_ORIGINS = ['https://war-of-genesis-stats.oceanfog1.workers.dev'];
function withCors(request, response) {
  const origin = request.headers.get('origin');
  if (!origin || !CORS_ORIGINS.includes(origin)) return response;
  const headers = new Headers(response.headers);
  headers.set('access-control-allow-origin', origin);
  headers.append('vary', 'origin');
  return new Response(response.body, { status: response.status, headers });
}

export default {
  async fetch(request, env) {
    const res = await route(request, env);
    return withCors(request, res);
  },
};

async function route(request, env) {
    const url = new URL(request.url), { pathname } = url;
    if (request.method === 'OPTIONS' && pathname.startsWith('/v1/'))
      return new Response(null, { status: 204, headers: { 'access-control-allow-methods': 'GET, POST', 'access-control-allow-headers': 'authorization, content-type',
                                                           'access-control-max-age': '86400' } });
    try {
      if (request.method === 'POST' && pathname === '/v1/play') return await postPlay(request, env);
      if (request.method === 'GET' && pathname === '/v1/dashboard') return await getDashboard(url, env);
      if (request.method === 'POST' && pathname === '/v1/hardcore') return await postHardcore(request, env);
      if (request.method === 'GET' && pathname === '/v1/ranking') return await getRanking(env);
      if (request.method === 'POST' && pathname === '/v1/bug') return await postBug(request, env);
      if (pathname === '/v1/bugs' || pathname.startsWith('/v1/bugs/')) {
        if (!isAdmin(request, env)) return json({ error: 'unauthorized' }, 401);
        if (request.method === 'GET' && pathname === '/v1/bugs') return await listBugs(url, env);
        const m = pathname.match(/^\/v1\/bugs\/(\d{1,12})\/status$/);
        if (request.method === 'POST' && m) return await setBugStatus(Number(m[1]), request, env);
        return json({ error: 'not found' }, 404);
      }
      if (request.method === 'GET' && pathname === '/ranking')
        return new Response(RANKING_HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } });
      if (request.method === 'GET' && (pathname === '/dashboard' || pathname === '/'))
        return new Response(DASHBOARD_HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } });
      if (request.method === 'GET' && pathname === '/embed.js')
        return new Response(EMBED_JS, { headers: { 'content-type': 'text/javascript; charset=utf-8', 'cache-control': 'public, max-age=300' } });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      console.error(e && e.stack || e);
      return json({ error: 'server error' }, 500);
    }
}
