-- 대시보드 기간(하루 · 주 · 달) 거르기용 셈 — 받은 날(UTC)마다 한 줄. tally 에는 날짜가 없어 기간으로 못 거른다.
-- 이 표는 올린 날부터 쌓인다(옛 셈은 날짜를 몰라 못 옮긴다). 35일 지난 줄은 Worker 가 한 달 집계를 셀 때 지운다.
-- npx wrangler d1 execute cds-remake-stats --remote --file=migrate-period.sql
CREATE TABLE IF NOT EXISTS tally_daily (
  day     TEXT NOT NULL,          -- 받은 날(UTC, yyyy-MM-dd)
  kind    TEXT NOT NULL,
  key     TEXT NOT NULL,
  version TEXT NOT NULL,
  install TEXT NOT NULL,
  name    TEXT NOT NULL,
  n       INTEGER NOT NULL,
  PRIMARY KEY (day, kind, key, version, install)
) WITHOUT ROWID;
