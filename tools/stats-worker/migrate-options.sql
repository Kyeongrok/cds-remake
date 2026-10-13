-- 옵션마다 한 줄이던 mods 표를 설치마다 한 줄인 options 표로 옮긴다 — 한 번만 돌린다.
-- wrangler d1 execute cds-remake-stats --remote --file=migrate-options.sql
-- 설치 수만큼만 쓰인다. 하루 쓰기 한도가 찬 날에는 안 들어가므로 한도가 풀린 뒤(09:00 KST)에 돌린다.
CREATE TABLE IF NOT EXISTS options (
  install    TEXT PRIMARY KEY,
  version    TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  body       TEXT NOT NULL
) WITHOUT ROWID;

INSERT OR IGNORE INTO options (install, version, updated_at, body)
SELECT install, MAX(version), MAX(updated_at), json_group_object(name, value)
FROM (SELECT install, name, value, version, updated_at FROM mods ORDER BY install, name)
GROUP BY install;
