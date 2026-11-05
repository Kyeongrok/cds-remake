-- 버그 리포트에 「답」을 단다 — 보낸 사람이 게임의 「내 리포트」에서 보는 글이다(메모 note 는 관리자끼리만 본다).
-- 한 번만 돌린다(ALTER TABLE 은 다시 돌리면 「duplicate column」으로 멈춘다 — 그러면 이미 된 것이다).
--   npx wrangler d1 execute cds-remake-stats --remote --file=migrate-reply.sql
ALTER TABLE bug_reports ADD COLUMN reply TEXT;
-- 설치 하나가 보낸 리포트를 고를 때 표를 다 훑지 않게 한다.
CREATE INDEX IF NOT EXISTS bug_reports_install ON bug_reports (install, id);
