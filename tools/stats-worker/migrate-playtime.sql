-- 누적 표(rollup)에 플레이 시간 칸(play, 초)을 더한다 — /v1/playtime 이 tally_daily 의 지난 30일(약 6만 줄)을 훑지 않고 이 칸 30줄 남짓만 읽게(2026-11-25).
-- 1단계는 칸만 더한다 — Worker 를 올리기 전에 돌려야 한다(올린 Worker 가 받을 때 이 칸에 더한다).
--   npx wrangler d1 execute cds-remake-stats --remote --command "ALTER TABLE rollup ADD COLUMN play INTEGER NOT NULL DEFAULT 0"
-- 2단계(이 파일)는 지난 것을 채운다 — 날 줄은 남아 있는 tally_daily(최근 35일)로, 전체 줄은 tally 로. 주 · 달 줄은 앞으로 받는 것만 더해진다.
-- 다시 돌려도 된다(덮어쓴다). 다만 Worker 를 올린 뒤 이것을 돌리면 그사이 받은 것도 tally_daily 에 있으니 어긋나지 않는다.
--   npx wrangler d1 execute cds-remake-stats --remote --file=migrate-playtime.sql
UPDATE rollup SET play = COALESCE((SELECT SUM(t.n) FROM tally_daily t WHERE t.day = rollup.key AND t.kind = 'play'
                                   AND (rollup.version = '' OR t.version = rollup.version)), 0)
WHERE grain = 'day' AND key >= (SELECT MIN(day) FROM tally_daily);
UPDATE rollup SET play = COALESCE((SELECT SUM(t.n) FROM tally t WHERE t.kind = 'play' AND (rollup.version = '' OR t.version = rollup.version)), 0)
WHERE grain = 'all' AND key = '';
