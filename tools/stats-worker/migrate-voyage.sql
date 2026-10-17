-- 항해의 날수 · 선회를 횟수와 한 줄에 더한다 — wrangler d1 execute cds-remake-stats --remote --file=migrate-voyage.sql
-- 예전에는 항해 하나가 voyage · voyage_days · voyage_turns 세 줄로 쓰여, 셈 표 쓰기의 큰 몫이 이것이었다(D1 하루 쓰기 한도).
-- 옛 voyage_days · voyage_turns 줄은 그대로 두고 집계가 함께 더한다. 칸을 더하는 것은 줄을 다시 쓰지 않는다.
ALTER TABLE tally ADD COLUMN days INTEGER NOT NULL DEFAULT 0;
ALTER TABLE tally ADD COLUMN turns INTEGER NOT NULL DEFAULT 0;
