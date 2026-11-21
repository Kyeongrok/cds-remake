// 커스텀 발견물 공유 — 게임(CdsHelper.Game/Local/Helpers/DiscoveryShare.cs)의 「클라우드에 올리기」 · 「공유 발견물」 창이 부른다.
//
//   POST /v1/shares                         올리기(multipart: meta JSON · file .cdsdiscovery · picture PNG(없어도 된다)) — 같은 UUID 면 고친다(올린 설치만)
//                                           meta.official — 올린 분이 정식 버전(게임 기본 발견물)에 넣어도 좋다고 했는지
//                                           meta.note — 고친 내용(200자, 판 이력에 남는다) · 답은 { ok, updated, rev }
//   GET  /v1/shares?install=<id>            목록(숨긴 것 빼고, 새로 고친 차례) — install 을 주면 제 것에 mine: true
//   GET  /v1/shares/<uuid>/file             파일 받기 — 받은 수가 오른다
//   GET  /v1/shares/<uuid>/picture          미리보기 그림
//   GET  /v1/shares/<uuid>/history          판 이력(새 판부터) — { revisions: [{ rev, note, size, created_at }] }
//   POST /v1/shares/<uuid>/delete {install} 지우기 — 올린 설치 또는 관리자(Authorization: Bearer <BUG_ADMIN_KEY>)
//   POST /v1/shares/<uuid>/report {install, reason}  신고 — 설치마다 한 번, 다섯 곳이 신고하면 숨는다
//   POST /v1/shares/<uuid>/hide {hidden}    숨기기 · 되살리기(관리자만)
//
// 파일은 KV(SHARES)에, 목록은 D1(shared_discoveries)에 둔다. 올린 설치 번호는 목록에 안 낸다.

const MAX_FILE = 8 * 1024 * 1024;        // 동영상 5MB + 그림 · 대본 · 아이템
const MAX_PICTURE = 512 * 1024;
const PER_DAY = 30, ALL_PER_DAY = 900;   // 설치 하나 · 모두 합쳐 하루 올리기(고치기 포함) — KV 무료 쓰기는 하루 1000번이다
const REPORTS_TO_HIDE = 5;
const isUuid = (v) => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);

export async function routeShares(request, env, url, { json, text, int, isId, isAdmin }) {
  const { pathname } = url;
  if (pathname === '/v1/shares') {
    if (request.method === 'GET') return listShares(url, env, isId);
    if (request.method === 'POST') return postShare(request, env, json, text, int, isId);
    return json({ error: 'not found' }, 404);
  }
  const m = pathname.match(/^\/v1\/shares\/([0-9a-fA-F-]{36})\/(file|picture|history|delete|report|hide)$/);
  if (!m || !isUuid(m[1])) return json({ error: 'not found' }, 404);
  const uuid = m[1].toLowerCase(), action = m[2];
  if (request.method === 'GET' && action === 'file') return getFile(uuid, env, json);
  if (request.method === 'GET' && action === 'picture') return getPicture(uuid, env, json);
  if (request.method === 'GET' && action === 'history') return getHistory(uuid, env, json);
  if (request.method !== 'POST') return json({ error: 'not found' }, 404);
  let b = {};
  try { b = (await request.json()) || {}; } catch { b = {}; }
  if (action === 'delete') return deleteShare(uuid, b, request, env, json, isId, isAdmin);
  if (action === 'report') return reportShare(uuid, b, env, json, text, isId);
  if (action === 'hide') {
    if (!isAdmin(request, env)) return json({ error: 'unauthorized' }, 401);
    await env.DB.prepare('UPDATE shared_discoveries SET hidden = ? WHERE uuid = ?').bind(b.hidden ? 1 : 0, uuid).run();
    return json({ ok: true });
  }
  return json({ error: 'not found' }, 404);
}

async function listShares(url, env, isId) {
  const install = url.searchParams.get('install') || '';
  const { results } = await env.DB.prepare(
    `SELECT uuid, name, category, author, license, description, size, has_movie, has_picture, version,
            created_at, updated_at, rev, downloads, wants_official, owner = ? AS mine
       FROM shared_discoveries WHERE hidden = 0 ORDER BY updated_at DESC LIMIT 1000`).bind(isId(install) ? install : '').all();
  const shares = results.map(r => ({ ...r, mine: !!r.mine, wants_official: !!r.wants_official, has_movie: !!r.has_movie, has_picture: !!r.has_picture }));
  return new Response(JSON.stringify({ shares }),
    { headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' } });
}

async function postShare(request, env, json, text, int, isId) {
  const length = Number(request.headers.get('content-length') || 0);
  if (length > MAX_FILE + MAX_PICTURE + 64 * 1024) return json({ error: 'too large' }, 413);
  let form;
  try { form = await request.formData(); } catch { return json({ error: 'bad form' }, 400); }
  let meta;
  try { meta = JSON.parse(form.get('meta')); } catch { return json({ error: 'bad meta' }, 400); }
  if (!meta || meta.v !== 1 || !isId(meta.install) || !isUuid(meta.uuid) || typeof meta.name !== 'string' || !meta.name.trim())
    return json({ error: 'bad meta' }, 400);
  const file = form.get('file');
  if (!file || typeof file === 'string') return json({ error: 'no file' }, 400);
  if (file.size > MAX_FILE) return json({ error: 'file too large' }, 413);
  const bytes = await file.arrayBuffer();
  const head = new Uint8Array(bytes, 0, Math.min(4, bytes.byteLength));
  if (head.length < 4 || head[0] !== 0x50 || head[1] !== 0x4b) return json({ error: 'not a zip' }, 400);
  const picture = form.get('picture');
  let pic = null;
  if (picture && typeof picture !== 'string' && picture.size > 0) {
    if (picture.size > MAX_PICTURE) return json({ error: 'picture too large' }, 413);
    pic = await picture.arrayBuffer();
    const p = new Uint8Array(pic, 0, Math.min(4, pic.byteLength));
    if (p.length < 4 || p[0] !== 0x89 || p[1] !== 0x50) return json({ error: 'picture not png' }, 400);
  }

  const uuid = meta.uuid.toLowerCase(), now = new Date().toISOString();
  const had = await env.DB.prepare('SELECT owner, hidden, rev FROM shared_discoveries WHERE uuid = ?').bind(uuid).first();
  if (had && had.owner !== meta.install) return json({ error: 'not yours' }, 403);
  if (had && had.hidden) return json({ error: 'hidden' }, 403);

  // 하루 올리기 — 같은 날 고친 것도 센다(KV 쓰기 몫). 날마다 셈 열쇠 하나.
  const day = now.slice(0, 10);
  let counts = {};
  try { counts = JSON.parse((await env.SHARES.get(`count:${day}`)) || '{}'); } catch { counts = {}; }
  if ((counts[meta.install] || 0) >= PER_DAY || (counts._all || 0) >= ALL_PER_DAY) return json({ error: 'rate limited' }, 429);

  await env.SHARES.put(`file:${uuid}`, bytes);
  if (pic) await env.SHARES.put(`pic:${uuid}`, pic);
  else if (had) await env.SHARES.delete(`pic:${uuid}`);
  counts[meta.install] = (counts[meta.install] || 0) + 1;
  counts._all = (counts._all || 0) + 1;
  await env.SHARES.put(`count:${day}`, JSON.stringify(counts), { expirationTtl: 3 * 86400 });

  const row = [text(meta.name.trim(), 80), int(meta.category, 99), text(meta.author || '', 60), text(meta.license || '', 200),
               text(meta.description || '', 300), bytes.byteLength, meta.hasMovie ? 1 : 0, pic ? 1 : 0, text(meta.version || '', 32),
               meta.official ? 1 : 0];
  const rev = had ? had.rev + 1 : 1;
  if (had)
    await env.DB.prepare(`UPDATE shared_discoveries SET name = ?, category = ?, author = ?, license = ?, description = ?, size = ?,
                            has_movie = ?, has_picture = ?, version = ?, wants_official = ?, updated_at = ?, rev = rev + 1 WHERE uuid = ?`)
      .bind(...row, now, uuid).run();
  else
    await env.DB.prepare(`INSERT INTO shared_discoveries (uuid, name, category, author, license, description, size, has_movie, has_picture,
                            version, wants_official, owner, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
      .bind(uuid, ...row, meta.install, now, now).run();
  await env.DB.prepare('INSERT OR REPLACE INTO share_revisions (uuid, rev, note, size, created_at) VALUES (?, ?, ?, ?, ?)')
    .bind(uuid, rev, text(meta.note || '', 200), bytes.byteLength, now).run();
  return json({ ok: true, updated: !!had, rev });
}

async function getFile(uuid, env, json) {
  const row = await env.DB.prepare('SELECT hidden FROM shared_discoveries WHERE uuid = ?').bind(uuid).first();
  if (!row || row.hidden) return json({ error: 'not found' }, 404);
  const bytes = await env.SHARES.get(`file:${uuid}`, 'arrayBuffer');
  if (!bytes) return json({ error: 'not found' }, 404);
  await env.DB.prepare('UPDATE shared_discoveries SET downloads = downloads + 1 WHERE uuid = ?').bind(uuid).run();
  return new Response(bytes, { headers: { 'content-type': 'application/zip', 'cache-control': 'no-store' } });
}

async function getHistory(uuid, env, json) {
  const row = await env.DB.prepare('SELECT hidden FROM shared_discoveries WHERE uuid = ?').bind(uuid).first();
  if (!row || row.hidden) return json({ error: 'not found' }, 404);
  const { results } = await env.DB.prepare(
    'SELECT rev, note, size, created_at FROM share_revisions WHERE uuid = ? ORDER BY rev DESC LIMIT 100').bind(uuid).all();
  return new Response(JSON.stringify({ revisions: results }),
    { headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' } });
}

async function getPicture(uuid, env, json) {
  const bytes = await env.SHARES.get(`pic:${uuid}`, 'arrayBuffer');
  if (!bytes) return json({ error: 'not found' }, 404);
  return new Response(bytes, { headers: { 'content-type': 'image/png', 'cache-control': 'public, max-age=300' } });
}

async function deleteShare(uuid, b, request, env, json, isId, isAdmin) {
  const row = await env.DB.prepare('SELECT owner FROM shared_discoveries WHERE uuid = ?').bind(uuid).first();
  if (!row) return json({ error: 'not found' }, 404);
  if (!isAdmin(request, env) && !(isId(b.install) && b.install === row.owner)) return json({ error: 'not yours' }, 403);
  await env.SHARES.delete(`file:${uuid}`);
  await env.SHARES.delete(`pic:${uuid}`);
  await env.DB.batch([
    env.DB.prepare('DELETE FROM shared_discoveries WHERE uuid = ?').bind(uuid),
    env.DB.prepare('DELETE FROM share_reports WHERE uuid = ?').bind(uuid),
    env.DB.prepare('DELETE FROM share_revisions WHERE uuid = ?').bind(uuid),
  ]);
  return json({ ok: true });
}

async function reportShare(uuid, b, env, json, text, isId) {
  if (!isId(b.install)) return json({ error: 'bad install' }, 400);
  const row = await env.DB.prepare('SELECT owner FROM shared_discoveries WHERE uuid = ?').bind(uuid).first();
  if (!row) return json({ error: 'not found' }, 404);
  const put = await env.DB.prepare('INSERT OR IGNORE INTO share_reports (uuid, install, reason, created_at) VALUES (?, ?, ?, ?)')
    .bind(uuid, b.install, text(b.reason || '', 500), new Date().toISOString()).run();
  if (put.meta.changes > 0)
    await env.DB.prepare(`UPDATE shared_discoveries SET reports = reports + 1,
                            hidden = CASE WHEN reports + 1 >= ? THEN 1 ELSE hidden END WHERE uuid = ?`).bind(REPORTS_TO_HIDE, uuid).run();
  return json({ ok: true });
}
