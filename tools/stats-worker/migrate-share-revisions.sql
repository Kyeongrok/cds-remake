-- 판 이력 표만 더한다(이미 shared_discoveries 가 있는 DB) — wrangler d1 execute cds-remake-stats --remote --file=migrate-share-revisions.sql

-- 판 이력 — 올릴 때마다 한 줄(판 번호 · 고친 내용). 파일은 마지막 판만 KV 에 둔다.
CREATE TABLE IF NOT EXISTS share_revisions (
  uuid       TEXT NOT NULL,
  rev        INTEGER NOT NULL,
  note       TEXT NOT NULL,        -- 올린 분이 적은 고친 내용(200자)
  size       INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (uuid, rev)
);
