# 놀이 통계 받는 곳 (Cloudflare Worker + D1)

게임이 몇 분마다, 그리고 끌 때 보내는 셈(무엇을 발견했고 어느 도시에 들렀고 어느 메뉴를 눌렀고 얼마나 싸웠나)을 받아 D1 에 쌓는다.
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

- 대시보드: `https://…workers.dev/dashboard` — 합계 타일 · 모드 옵션(켬·끔 비율 / 단계 옵션은 값마다 설치 수) · 도시 · 발견물 순위(횟수 · 설치로 줄 세우기) · 메뉴(창으로 묶음 / 줄별) · 전투(해전 · 육상전 승률) · 오류 · 날짜/버전별. 지금은 누구나 볼 수 있다.
- 마음대로 묻기:

```
npx wrangler d1 execute cds-remake-stats --remote --command "SELECT key, MAX(name) name, SUM(n) n FROM counts WHERE kind = 'city' GROUP BY key ORDER BY n DESC LIMIT 20"
```

## 적히는 것

- `batches` — 덩이 하나에 한 줄(설치 번호 · 버전 · 받은 때).
- `counts` — 덩이 안의 셈. `kind` 는 `discovery`(발견물 번호) · `city`(도시 번호) · `menu`(「창 제목/줄 글」) · `battle`(`sea:Won` · `sea:Defeated` · `sea:Escaped` · `land:win` · `land:lose`) · `error`(「오류 갈래@클래스.메서드」 — 오류 글은 안 싣는다).
- `mods` — 설치마다 지금 켜 둔 옵션(`GameSettings` 의 참·거짓 값 전부와 단계 옵션 몇). 보낼 때마다 마지막 값으로 덮는다.

이름 · 계정 · IP · 세이브 내용은 적지 않는다. 메뉴 줄 글은 화면에 뜬 그대로라, 줄에 회원이 지은 이름(배 이름 따위)이 뜨는 창이 있으면 그것도 들어온다.
