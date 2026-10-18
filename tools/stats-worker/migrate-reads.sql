-- 대시보드 집계가 읽는 줄을 줄인다 — 표를 만들고(schema.sql 끝의 daily · voyage_legacy) Worker 를 올린 *뒤에*
--   wrangler d1 execute cds-remake-stats --remote --file=migrate-reads.sql
-- daily 는 batches 에서 다시 세어 덮는다(올린 뒤에 들어온 덩이까지 batches 가 다 가지고 있어 겹쳐 세지 않는다).
-- 옛 voyage_days · voyage_turns 줄(칠만여 줄)은 버전마다 한 줄로 묶는다. tally 의 그 줄은 지우지 않는다(지우면 하루 쓰기 한도를 넘는다) — 집계가 갈래로 걸러 안 읽는다.
INSERT OR REPLACE INTO daily (day, version, install, batches)
  SELECT substr(received_at, 1, 10), version, install, COUNT(*) FROM batches GROUP BY 1, 2, 3;

INSERT OR REPLACE INTO voyage_legacy (version, body)
  SELECT version, json_group_object(key, json_array(d, t)) FROM (
    SELECT version, key, SUM(CASE kind WHEN 'voyage_days' THEN n ELSE 0 END) AS d, SUM(CASE kind WHEN 'voyage_turns' THEN n ELSE 0 END) AS t
    FROM tally WHERE kind IN ('voyage_days', 'voyage_turns') GROUP BY version, key)
  GROUP BY version;
