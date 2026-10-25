-- 주인공 표에 기함 선체와 교역소 투자를 더한다 — wrangler d1 execute cds-remake-stats --remote --file=migrate-invest.sql
-- v1.1.57 부터 게임이 주인공 한 벌에 기함 선체 이름 · 투자 합 · {도시 이름: 금액} 을 싣는다. 옛 줄은 빈 기함 · 투자 0 이다.
ALTER TABLE heroes ADD COLUMN first_flagship TEXT NOT NULL DEFAULT '';
ALTER TABLE heroes ADD COLUMN flagship TEXT NOT NULL DEFAULT '';
ALTER TABLE heroes ADD COLUMN invested INTEGER NOT NULL DEFAULT 0;
ALTER TABLE heroes ADD COLUMN investments TEXT NOT NULL DEFAULT '{}';
