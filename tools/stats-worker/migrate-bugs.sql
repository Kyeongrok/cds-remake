-- 버그 리포트 표(게임의 「버그 리포트」 창이 보낸 글) — 이미 있는 DB 에 더한다. 다시 돌려도 괜찮다(IF NOT EXISTS).
--   npx wrangler d1 execute cds-remake-stats --remote --file=migrate-bugs.sql
CREATE TABLE IF NOT EXISTS bug_reports (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,               -- 받은 때(ISO, UTC)
  version    TEXT NOT NULL,               -- 게임 버전
  install    TEXT NOT NULL,               -- 설치할 때 만든 무작위 번호(게임 통계와 같은 것)
  text       TEXT NOT NULL,               -- 사용자가 쓴 글(8000자까지)
  context    TEXT NOT NULL,               -- 게임이 붙인 것(JSON: 버전 · OS · 화면 · 지금 자리 · 모드 · 오류 기록 끝)
  status     TEXT NOT NULL DEFAULT 'new', -- new · seen · fixed · wontfix
  note       TEXT                         -- 고친 사람의 메모
);

CREATE INDEX IF NOT EXISTS bug_reports_status ON bug_reports (status, id);
-- 한 시간에 받는 수를 셀 때 표를 다 훑지 않게 한다.
CREATE INDEX IF NOT EXISTS bug_reports_created ON bug_reports (created_at, install);
