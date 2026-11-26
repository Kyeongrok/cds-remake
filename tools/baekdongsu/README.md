# 백동수편 (공개 배포하지 않는 시나리오)

팬 모드 「대항해시대3 백동수편」에서 옮긴 시나리오 데이터다. 다른 시나리오를 만들 바탕이라 **릴리즈에 싣지 않는다** —
`exe-tables/` 에 두면 앱에 실려 모든 회원에게 나가므로 여기에 둔다.

- `scenarios.json` — 기본 시나리오 표에 「백동수편」(시나리오책2) 줄을 더한 것
- `scenario-book2.json` — 이야기 대본(`tools/extract_baekdongsu.py` 가 만든다)

이 PC 에서 쓰려면 둘을 설치 앱의 표 폴더에 복사한다. 업데이트가 폴더를 덮으면 다시 복사한다.

```powershell
Copy-Item tools/baekdongsu/scenarios.json, tools/baekdongsu/scenario-book2.json "$env:LOCALAPPDATA\CdsHelper\current\exe-tables\" -Force
```

`exe-tables/scenarios.json` 을 고치면 여기 `scenarios.json` 에도 같은 고침을 넣는다(여기 것은 기본 표 + 백동수편 줄이다).
