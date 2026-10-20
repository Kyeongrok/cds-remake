-- 주인공 표를 더한다 — wrangler d1 execute cds-remake-stats --remote --file=migrate-heroes.sql
-- v1.1.31 부터 게임이 덩이에 주인공 한 벌(이름 · 나라 · 게임 날짜 · 능력치 여섯)을 싣는다. 항해(voyage)는 그때부터 안 받는다.
CREATE TABLE IF NOT EXISTS heroes (
  game_id         TEXT PRIMARY KEY,   -- 판 표지(Player.GameId)
  install         TEXT NOT NULL,
  version         TEXT NOT NULL,      -- 마지막으로 보낸 판의 버전
  first_at        TEXT NOT NULL,      -- 처음 받은 때
  updated_at      TEXT NOT NULL,      -- 마지막으로 받은 때
  name            TEXT NOT NULL,      -- 주인공 이름
  nation          TEXT NOT NULL,      -- 나라 이름
  first_date      TEXT NOT NULL,      -- 처음 받았을 때의 게임 날짜(yyyy-MM-dd)
  date            TEXT NOT NULL,      -- 마지막으로 받았을 때의 게임 날짜
  first_abilities TEXT NOT NULL,      -- 처음 받은 능력치 여섯(JSON — 체력 · 지력 · 무력 · 매력 · 운 · 신앙심, 보이는 값)
  abilities       TEXT NOT NULL       -- 마지막으로 받은 능력치 여섯
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS heroes_recent ON heroes (updated_at DESC);
