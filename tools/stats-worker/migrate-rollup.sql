-- 누적 집계 표 — 대시보드의 설치 수 · 덩이 수 · 날짜별 탭 · 버전 목록을 daily 를 훑지 않고 여기서 읽는다.
-- 예전에는 집계를 새로 셀 때마다 daily 를 통째로 네 번 훑어(하루 · 주 · 달 묶기 + 전체 합) D1 하루 읽기 한도를 넘겼다(2026-11-24).
-- 표를 만들고, 지금까지의 daily 로 한 번 다시 채운다. 다시 돌려도 된다(지우고 다시 채운다 — daily 와 같은 묶음에서 더해지므로 어긋나지 않는다).
-- npx wrangler d1 execute cds-remake-stats --remote --file=migrate-rollup.sql
CREATE TABLE IF NOT EXISTS rollup (
  grain    TEXT NOT NULL,          -- day · week · month · all
  key      TEXT NOT NULL,          -- yyyy-MM-dd · 그 주 월요일 yyyy-MM-dd · yyyy-MM · ''(all)
  version  TEXT NOT NULL,          -- 게임 버전, ''는 모든 버전
  installs INTEGER NOT NULL,       -- 그 칸에서 겹치지 않게 센 설치 수
  batches  INTEGER NOT NULL,       -- 받은 덩이 수
  PRIMARY KEY (grain, key, version)
) WITHOUT ROWID;

-- 그 칸에서 이미 센 설치 — 처음 보는 설치일 때만 rollup.installs 를 하나 더한다. 지난 날 · 주 · 달의 줄은 Worker 가 지운다(pruneDaily).
CREATE TABLE IF NOT EXISTS rollup_seen (
  grain   TEXT NOT NULL,
  key     TEXT NOT NULL,
  version TEXT NOT NULL,
  install TEXT NOT NULL,
  PRIMARY KEY (grain, key, version, install)
) WITHOUT ROWID;

DELETE FROM rollup;
DELETE FROM rollup_seen;

INSERT INTO rollup SELECT 'day', day, version, COUNT(*), SUM(batches) FROM daily GROUP BY day, version;
INSERT INTO rollup SELECT 'day', day, '', COUNT(DISTINCT install), SUM(batches) FROM daily GROUP BY day;
INSERT INTO rollup SELECT 'week', date(day, 'weekday 0', '-6 days'), version, COUNT(DISTINCT install), SUM(batches) FROM daily GROUP BY 2, 3;
INSERT INTO rollup SELECT 'week', date(day, 'weekday 0', '-6 days'), '', COUNT(DISTINCT install), SUM(batches) FROM daily GROUP BY 2;
INSERT INTO rollup SELECT 'month', substr(day, 1, 7), version, COUNT(DISTINCT install), SUM(batches) FROM daily GROUP BY 2, 3;
INSERT INTO rollup SELECT 'month', substr(day, 1, 7), '', COUNT(DISTINCT install), SUM(batches) FROM daily GROUP BY 2;
INSERT INTO rollup SELECT 'all', '', version, COUNT(DISTINCT install), SUM(batches) FROM daily GROUP BY version;
INSERT INTO rollup SELECT 'all', '', '', COUNT(DISTINCT install), SUM(batches) FROM daily;

-- 아직 더해질 칸(오늘 · 이번 주 · 이번 달 · 전체)의 본 설치만 채운다. 날은 UTC 다.
INSERT INTO rollup_seen SELECT DISTINCT 'day', day, version, install FROM daily WHERE day = date('now');
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'day', day, '', install FROM daily WHERE day = date('now');
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'week', date(day, 'weekday 0', '-6 days'), version, install FROM daily WHERE day >= date('now', 'weekday 0', '-6 days');
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'week', date(day, 'weekday 0', '-6 days'), '', install FROM daily WHERE day >= date('now', 'weekday 0', '-6 days');
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'month', substr(day, 1, 7), version, install FROM daily WHERE day >= date('now', 'start of month');
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'month', substr(day, 1, 7), '', install FROM daily WHERE day >= date('now', 'start of month');
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'all', '', version, install FROM daily;
INSERT OR IGNORE INTO rollup_seen SELECT DISTINCT 'all', '', '', install FROM daily;

-- 묵혀 둔 집계를 버려 다음에 열 때 새 표로 세게 한다.
DELETE FROM snapshots;
