# 게임 통계 받는 곳 (Cloudflare Worker + D1)

게임이 도시를 나설 때, 그리고 끌 때 보내는 셈(무엇을 발견했고 누구를 고용 · 해고했고 얼마나 싸웠나)과 주인공(이름 · 능력치)을 받아 D1 에 쌓는다.
보내는 쪽은 `CdsHelper.Game/Local/Helpers/PlayStats.cs`.

## 처음 한 번 올리기

```
cd tools/stats-worker
npx wrangler login
npx wrangler d1 create cds-remake-stats          # 찍히는 database_id 를 wrangler.toml 에 넣는다
npx wrangler d1 execute cds-remake-stats --remote --file=schema.sql
npx wrangler deploy                              # https://cds-remake-stats.<계정>.workers.dev 주소가 찍힌다
```

찍힌 주소를 저장소 뿌리의 `stats-url.txt` 에 한 줄로 적어야 게임이 보내기 시작한다(비어 있으면 안 보낸다).
그 뒤에 낸 릴리즈부터 보낸다 — 처음 켤 때 「통계 보내기」 알림을 한 번 띄우고 켠다.

## 보기

- 대시보드: `https://…workers.dev/dashboard` — 합계 타일 · 모드 옵션(켬·끔 비율 / 단계 옵션은 값마다 설치 수) · 발견물 순위 · 고용한 부하 · 해고한 부하(횟수 · 설치로 줄 세우기) · 주인공(판마다 처음 → 마지막 능력치) · 항해(v1.1.30 까지 받은 옛 자료) · 네비게이션(고른 길 · 자동이동 모드 · 끝난 꼴) · 전투(해전 · 육상전 승률) · 오류 · 날짜/버전별. 지금은 누구나 볼 수 있다.
- 마음대로 묻기:

```
npx wrangler d1 execute cds-remake-stats --remote --command "SELECT key, MAX(name) name, SUM(n) n FROM tally WHERE kind = 'discovery' GROUP BY key ORDER BY n DESC LIMIT 20"
```

## 무료 한도

D1 무료 판은 하루에 읽기 오백만 줄 · 쓰기 십만 줄까지다(표를 훑으면 훑은 줄 수만큼, 색인도 줄마다 쓰기로 센다).

- 대시보드 집계는 `snapshots` 에 묵혀 두고 **한 시간에 한 번만** 새로 센다 — 열 때마다 세면 몇 번 만에 한도가 찬다.
- 셈은 덩이마다 줄을 쌓지 않고 `tally` 의 (갈래 · 열쇠 · 버전 · 설치) 한 줄에 더한다. 훑을 줄이 날마다 늘지 않는다.
- 집계는 덩이 표(`batches`)를 훑지 않고 날마다 설치 하나에 한 줄인 `daily` 를 센다. 모드 표는 `json_each` 로 펼치지 않고 몸통만 읽어 Worker 가 센다(펼친 줄이 다 읽기로 세어진다). `tally` 는 읽는 갈래로만 걸러 옛 `voyage_days` · `voyage_turns` · `city` 줄을 안 읽는다 — 옛 항해 날수 · 선회는 `voyage_legacy` 에 버전마다 한 줄로 묶어 두었다(`migrate-reads.sql`).
- 쿼리마다 몇 줄 읽었는지: `npx wrangler d1 insights cds-remake-stats --sort-by reads --timePeriod 7d --json`.
- 게임은 도시를 나설 때와 끌 때 보낸다(오래 머물면 30분마다).
- 지금 얼마나 썼는지: `npx wrangler d1 info cds-remake-stats` 의 `rows_read_24h` · `rows_written_24h`.

## 적히는 것

- `batches` — 덩이 하나에 한 줄(설치 번호 · 버전 · 받은 때).
- `tally` — 셈. (갈래 · 열쇠 · 버전 · 설치)마다 한 줄이고 받을 때마다 더한다. `kind` 는 `discovery`(발견물 번호) · `hire` · `dismiss`(고용 · 해고한 부하의 인물 번호 — 해고는 부하편성의 「해고」와 재계약 거절만) · `voyage` · `voyage_days` · `voyage_turns`(열쇠는 셋 다 「떠난 도시 번호>닿은 도시 번호」 — 횟수 · 날수의 합 · 선회의 합) · `nav`(`pick:quick` · `helm:steady` · `from:city` · `end:arrived` · `trip:auto` …) · `event`(바다 · 육지에서 하루를 넘길 때의 일 — `sea:day` · `sea:eligible` · `sea:Storm` · `land:day` · `land:Beast` …) · `battle`(`sea:Won` · `sea:Defeated` · `sea:Escaped` · `land:win` · `land:lose`) · `error`(「오류 갈래@클래스.메서드」 — 오류 글은 안 싣는다).
- `options` — 설치마다 지금 켜 둔 옵션(`GameSettings` 의 참·거짓 값 전부와 단계 옵션 몇) 한 벌을 한 줄(JSON)로. 보낼 때마다 마지막 값으로 덮는다(예전의 `mods` 표는 옵션마다 한 줄이라 하루 쓰기 한도를 채웠다 — `migrate-options.sql` 로 옮긴다). 주 모니터 해상도(`ScreenWidth` · `ScreenHeight`, 화면 점)와 배율(`ScreenScale`, %)도 여기 실린다 — 게임 창 크기는 `Resolution`(고른 차례)이다.

- `heroes` — 주인공. 판 표지(`Player.GameId`)마다 한 줄 — 이름 · 나라 · 게임 날짜 · 능력치 여섯(체력 · 지력 · 무력 · 매력 · 운 · 신앙심, 보이는 값)을 처음 받은 값(`first_*`)과 마지막 값으로 둔다. 게임은 이름 · 나라 · 능력치가 바뀌었을 때만 덩이에 싣는다(v1.1.31 부터, `migrate-heroes.sql`).

- `hardcore` — 하드코어 랭킹. 하드코어 판이 게임 오버가 될 때 한 판에 한 줄(판 표지가 주키). 주인공 이름 · 나라 · 발견물 수 · 게임 오버 날짜와 까닭 · 그 판에서 한 번이라도 켠 · 줄인 모드와 쓴 개발 기능(`marks`)을 적는다. 기록은 게임에 박힌 열쇠로 HMAC 서명되어 오고, Worker 비밀 `HARDCORE_KEY` 로 대 보아 안 맞으면 버린다. 열쇠는 GitHub 비밀 `HARDCORE_KEY` 와 같아야 한다(릴리즈 빌드가 게임에 박는다) — 바꾸려면 둘 다 `npx wrangler secret put HARDCORE_KEY` · `gh secret set HARDCORE_KEY` 로 함께 바꾼다. 페이지는 `https://…workers.dev/ranking`.

계정 · IP · 세이브 내용은 적지 않는다. 이름은 주인공 이름만 적는다(`heroes` · `hardcore`). 차림표 줄(`menu`)은 예전에 받았는데 이제 안 받는다 — 줄 글에 회원이 지은 이름(배 이름 따위)이 섞여 들어올 수 있었다. 들른 도시(`city`)와 항해(`voyage` · `voyage_days` · `voyage_turns`)도 이제 안 받는다(항해는 v1.1.30 까지). 표에 남은 옛 줄은 집계에 안 든다(지우려면 `DELETE FROM tally WHERE kind IN ('menu', 'city')`).
