-- 공유 발견물 좋아요 — wrangler d1 execute cds-remake-stats --remote --file=migrate-share-likes.sql
-- 워커를 올리기 전에 먼저 돌린다(목록이 likes 칸을 읽는다).
ALTER TABLE shared_discoveries ADD COLUMN likes INTEGER NOT NULL DEFAULT 0;

-- 좋아요 — 설치마다 한 번. 누른 줄을 지우면 취소다.
CREATE TABLE IF NOT EXISTS share_likes (
  uuid       TEXT NOT NULL,
  install    TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (uuid, install)
);
