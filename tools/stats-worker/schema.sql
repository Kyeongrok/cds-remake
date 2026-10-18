-- 게임 통계 D1 표 — wrangler d1 execute cds-remake-stats --remote --file=schema.sql

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
  kind    TEXT NOT NULL,          -- discovery · hire · dismiss · voyage · voyage_days · voyage_turns · nav · battle · error (옛 줄: menu · city)
  key     TEXT NOT NULL,          -- 발견물 번호 · 인물 번호 · 「떠난 도시>닿은 도시」 · pick:quick / end:arrived … · sea:Won / land:win … · 「오류 갈래@클래스.메서드」
  version TEXT NOT NULL,
  install TEXT NOT NULL,
  name    TEXT NOT NULL,          -- 발견물 · 부하 이름, 항해는 「리스본 > 세비야」(그 밖은 빈 글)
  n       INTEGER NOT NULL,
  days    INTEGER NOT NULL DEFAULT 0,   -- 항해(voyage)의 날수 합 — 횟수와 한 줄에 더한다(migrate-voyage.sql). 옛 줄은 voyage_days 갈래에 있다
  turns   INTEGER NOT NULL DEFAULT 0,   -- 항해의 선회 합. 옛 줄은 voyage_turns 갈래에 있다
  PRIMARY KEY (kind, key, version, install)
) WITHOUT ROWID;

-- 대시보드 집계를 묵혀 두는 곳 — 버전(전체는 빈 글)마다 한 줄, 한 시간이 지나면 새로 센다.
CREATE TABLE IF NOT EXISTS snapshots (
  version TEXT PRIMARY KEY,
  made_at TEXT NOT NULL,
  body    TEXT NOT NULL
);

-- 설치마다 지금 켜 둔 옵션 — 한 설치에 한 줄, 보낼 때마다 마지막 값 한 벌로 덮는다.
-- 예전에는 mods 표에 옵션마다 한 줄씩 적었다 — 새 설치 하나에 백 줄 넘게 쓰여 D1 하루 쓰기 한도(십만 줄)가 찼다.
-- 그 표는 옛 자료째 남아 있고 이제 안 쓴다(migrate-options.sql 로 옮겼다).
CREATE TABLE IF NOT EXISTS options (
  install    TEXT PRIMARY KEY,
  version    TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  body       TEXT NOT NULL        -- {"GameSettings 의 속성 이름": 값} — 참·거짓은 1·0, 단계 옵션은 그 값
) WITHOUT ROWID;

-- 하드코어 랭킹 — 하드코어 판이 게임 오버가 될 때 게임이 한 판에 한 줄 보낸다(같은 판은 한 번만 들어간다).
-- 순위는 발견물 수가 많은 차례, 같으면 더 이른 게임 날짜에 끝난 차례다.
CREATE TABLE IF NOT EXISTS hardcore (
  game_id     TEXT PRIMARY KEY,   -- 판 표지(Player.GameId)
  install     TEXT NOT NULL,
  version     TEXT NOT NULL,
  name        TEXT NOT NULL,      -- NEW GAME 에서 지은 주인공 이름
  nation      TEXT NOT NULL,      -- 나라 이름(포르투갈 · 에스파니아 …)
  discoveries INTEGER NOT NULL,
  died        TEXT NOT NULL,      -- 게임 오버가 된 게임 날짜(yyyy-MM-dd)
  cause       INTEGER NOT NULL,   -- 게임 오버 그림 — 11 반란 · 일기토 · 감옥 따위, 12 함대 · 해전, 13 육상전
  marks       TEXT NOT NULL,      -- 한 번이라도 켠 · 줄인 모드와 쓴 개발 기능(JSON 배열, HardcoreRules 의 열쇠). [] 이면 순수
  received_at TEXT NOT NULL
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS hardcore_rank ON hardcore (discoveries DESC, died);

-- 날마다 · 버전마다 설치 하나에 한 줄(그날 받은 덩이 수) — 대시보드의 설치 수 · 덩이 수 · 날짜별 표 · 버전 목록을 batches 대신 여기서 센다.
-- batches 는 덩이마다 한 줄이라 날마다 늘고, 집계가 그것을 세 번 훑어 D1 하루 읽기 한도(오백만 줄)의 큰 몫을 먹었다.
CREATE TABLE IF NOT EXISTS daily (
  day     TEXT NOT NULL,          -- 받은 날(UTC, yyyy-MM-dd)
  version TEXT NOT NULL,
  install TEXT NOT NULL,
  batches INTEGER NOT NULL,
  PRIMARY KEY (day, version, install)
) WITHOUT ROWID;

-- 옛 판이 voyage_days · voyage_turns 갈래로 따로 적은 항해 날수 · 선회 — 버전마다 한 줄로 묶어 둔다(migrate-reads.sql).
-- 집계는 tally 의 그 줄들을 안 읽고 이것을 읽는다. 새 항해는 voyage 줄의 days · turns 칸에 더해지므로 이 표는 더 늘지 않는다.
CREATE TABLE IF NOT EXISTS voyage_legacy (
  version TEXT PRIMARY KEY,
  body    TEXT NOT NULL           -- {"떠난 도시>닿은 도시": [날수 합, 선회 합]}
) WITHOUT ROWID;
