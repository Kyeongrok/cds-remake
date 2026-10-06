-- 놀이 통계 D1 표 — wrangler d1 execute cds-remake-stats --remote --file=schema.sql

-- 게임이 보낸 덩이 하나에 한 줄(다시 보내도 한 번만 들어가게).
CREATE TABLE IF NOT EXISTS batches (
  batch       TEXT PRIMARY KEY,   -- 게임이 덩이마다 만든 번호
  received_at TEXT NOT NULL,
  install     TEXT NOT NULL,      -- 설치할 때 만든 무작위 번호
  version     TEXT NOT NULL
);

-- 덩이 안의 셈 — 무엇을 몇 번.
CREATE TABLE IF NOT EXISTS counts (
  batch TEXT NOT NULL,
  kind  TEXT NOT NULL,            -- discovery · city · menu · battle · error
  key   TEXT NOT NULL,            -- 발견물 번호 · 도시 번호 · 「창 제목/줄 글」 · sea:Won / land:win … · 「오류 갈래@클래스.메서드」
  name  TEXT NOT NULL,            -- 발견물 · 도시 이름(그 밖은 빈 글)
  n     INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS counts_batch ON counts (batch);
CREATE INDEX IF NOT EXISTS counts_kind ON counts (kind, key);

-- 설치마다 지금 켜 둔 옵션 — 보낼 때마다 마지막 값으로 덮는다.
CREATE TABLE IF NOT EXISTS mods (
  install    TEXT NOT NULL,
  name       TEXT NOT NULL,       -- GameSettings 의 속성 이름
  value      INTEGER NOT NULL,    -- 참·거짓은 1·0, 단계 옵션은 그 값
  version    TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (install, name)
);
