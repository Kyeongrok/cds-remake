-- 옛 counts 표(덩이마다 한 줄)를 tally(갈래 · 열쇠 · 버전 · 설치마다 한 줄)로 옮긴다.
-- 한 번만 돌린다 — 두 번 돌리면 두 번 더해진다. 차림표 줄(menu)은 이제 안 세므로 안 옮긴다.
-- 묵혀 둔 대시보드 집계도 지워, 다음에 열 때 새로 세게 한다.
--   npx wrangler d1 execute cds-remake-stats --remote --file=migrate-counts.sql
INSERT INTO tally (kind, key, version, install, name, n)
SELECT c.kind, c.key, b.version, b.install, MAX(c.name), SUM(c.n)
FROM counts c JOIN batches b ON b.batch = c.batch
WHERE c.kind IN ('discovery', 'city', 'battle', 'error')
GROUP BY c.kind, c.key, b.version, b.install
ON CONFLICT (kind, key, version, install) DO UPDATE SET n = n + excluded.n;

DELETE FROM snapshots;
