-- 커스텀 발견물 공유 — wrangler d1 execute cds-remake-stats --remote --file=migrate-shares.sql
-- 파일 자체는 KV(SHARES)에 있다: file:<uuid> = .cdsdiscovery, pic:<uuid> = 미리보기 PNG.
CREATE TABLE IF NOT EXISTS shared_discoveries (
  uuid        TEXT PRIMARY KEY,   -- 발견물 UUID(게임의 UserDiscoveries.Entry.Uuid) — 다시 올리면 같은 줄을 고친다
  name        TEXT NOT NULL,
  category    INTEGER NOT NULL,
  author      TEXT NOT NULL,
  license     TEXT NOT NULL,
  description TEXT NOT NULL,      -- 설명 앞 300자
  size        INTEGER NOT NULL,   -- 파일 바이트
  has_movie   INTEGER NOT NULL,
  has_picture INTEGER NOT NULL,
  version     TEXT NOT NULL,      -- 올린 게임 버전
  owner       TEXT NOT NULL,      -- 올린 설치(install) — 고치기 · 지우기는 이 설치만. 목록에는 안 낸다
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  rev         INTEGER NOT NULL DEFAULT 1,
  downloads   INTEGER NOT NULL DEFAULT 0,
  reports     INTEGER NOT NULL DEFAULT 0,
  hidden      INTEGER NOT NULL DEFAULT 0,  -- 관리자가 숨겼거나 신고가 쌓여 숨은 것
  wants_official INTEGER NOT NULL DEFAULT 0  -- 올린 분이 정식 버전에 넣어도 좋다고 했는지(올리기 창 체크박스)
);
CREATE INDEX IF NOT EXISTS shared_discoveries_updated ON shared_discoveries (updated_at);

-- 신고 — 설치마다 한 번만 센다.
CREATE TABLE IF NOT EXISTS share_reports (
  uuid       TEXT NOT NULL,
  install    TEXT NOT NULL,
  reason     TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (uuid, install)
);
