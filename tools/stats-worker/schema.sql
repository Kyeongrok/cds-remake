-- 놀이 통계 D1 표 — wrangler d1 execute cds-remake-stats --remote --file=schema.sql

-- 게임이 보낸 덩이 하나에 한 줄(다시 보내도 한 번만 들어가게).
CREATE TABLE IF NOT EXISTS batches (
  batch       TEXT PRIMARY KEY,   -- 게임이 덩이마다 만든 번호
  received_at TEXT NOT NULL,
  install     TEXT NOT NULL,      -- 설치할 때 만든 무작위 번호
  version     TEXT NOT NULL
);

-- 셈 — 무엇을 몇 번. 덩이마다 줄을 쌓지 않고 (갈래 · 열쇠 · 버전 · 설치)마다 한 줄에 더한다.
-- 대시보드가 이 표를 통째로 훑으므로, 줄이 날마다 늘면 D1 무료 읽기 한도(하루 오백만 줄)를 넘는다.
-- 예전에는 counts 표에 덩이마다 한 줄씩 쌓았다 — 그 표는 옛 자료째 남아 있고 이제 안 쓴다(옮기지 않았다).
CREATE TABLE IF NOT EXISTS tally (
  kind    TEXT NOT NULL,          -- discovery · city · voyage · voyage_days · voyage_turns · nav · battle · error (옛 줄: menu)
  key     TEXT NOT NULL,          -- 발견물 번호 · 도시 번호 · 「떠난 도시>닿은 도시」 · pick:quick / end:arrived … · sea:Won / land:win … · 「오류 갈래@클래스.메서드」
  version TEXT NOT NULL,
  install TEXT NOT NULL,
  name    TEXT NOT NULL,          -- 발견물 · 도시 이름, 항해는 「리스본 > 세비야」(그 밖은 빈 글)
  n       INTEGER NOT NULL,
  PRIMARY KEY (kind, key, version, install)
) WITHOUT ROWID;

-- 대시보드 집계를 묵혀 두는 곳 — 버전(전체는 빈 글)마다 한 줄, 한 시간이 지나면 새로 센다.
CREATE TABLE IF NOT EXISTS snapshots (
  version TEXT PRIMARY KEY,
  made_at TEXT NOT NULL,
  body    TEXT NOT NULL
);

-- 설치마다 지금 켜 둔 옵션 — 보낼 때마다 마지막 값으로 덮는다.
CREATE TABLE IF NOT EXISTS mods (
  install    TEXT NOT NULL,
  name       TEXT NOT NULL,       -- GameSettings 의 속성 이름
  value      INTEGER NOT NULL,    -- 참·거짓은 1·0, 단계 옵션은 그 값
  version    TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (install, name)
);
