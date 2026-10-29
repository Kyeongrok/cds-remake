---
name: run-game
description: 게임 앱(CostaDelSol)을 닫고, 다시 구워, 띄운다. 사용자가 "실행", "실행해줘", "띄워줘", "돌려봐" 라고 하거나, 코드를 고쳐 빌드가 통과한 뒤 화면으로 확인할 일이 있을 때 쓴다.
---

# 게임 앱 실행

`CostaDelSol.Play` 가 이 저장소의 실행 앱이고, 구워진 이름은 **`CostaDelSol.exe`** 다.

## 차례

세 걸음을 순서대로 한다. **앞의 것이 실패해도 다음으로 간다** — 앱이 안 떠 있으면
`taskkill` 이 1 을 내는 것이 정상이다.

```bash
taskkill //IM CostaDelSol.exe //F 2>/dev/null; sleep 1
dotnet build cds-helper.sln -v q --nologo
```

빌드가 통과하면 **바탕에서** 띄운다(`run_in_background: true`) — 앞에서 띄우면 앱이
닫힐 때까지 대화가 막힌다.

```
CostaDelSol.Play/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CostaDelSol.exe
```

띄운 뒤 `tasklist //FI "IMAGENAME eq CostaDelSol.exe"` 로 떴는지 한 번 확인한다.

## 알아 둘 것

- **앱이 떠 있으면 빌드가 깨진다.** `CostaDelSol.Play` 가 `CdsHelper.Game.dll` ·
  `CdsHelper.Maze.dll` 을 잠그고 있어 `MSB3027`(파일이 잠겨 있습니다)로 멎는다.
  그래서 굽기 전에 반드시 닫는다.
- 대상 판이 올라가면 `net10.0-windows10.0.19041.0` 자리가 바뀐다. 길이 안 맞으면
  `ls CostaDelSol.Play/bin/Debug/*/win-x64/CostaDelSol.exe` 로 찾아 쓴다.
- 앱을 닫으면 하던 판이 날아간다. 사용자가 게임 중일 수 있으니 **닫는다는 말을 한 줄
  적고** 닫는다.

## 실행 단추

터미널에 단추를 그리는 길은 없지만 **고르는 목록이 그 몫을 한다**. 코드를 고쳐 빌드가
통과했고 화면으로 확인할 것이 있으면, 말로 "실행해 보시겠습니까" 하고 묻지 말고
`AskUserQuestion` 으로 물어 **누를 수 있게** 낸다.

```
header  : "실행"
question: "새로 구운 것으로 띄울까요?"
options : "실행한다"   — 앱을 닫고 다시 구워 띄운다
          "안 띄운다"  — 고친 것만 두고 넘어간다
```

사용자가 「실행한다」를 고르면 위 차례를 그대로 밟는다.

## 화면 밖 시험 (사람 화면을 안 건드리고 확인할 때)

사용자가 PC 를 쓰는 중이면 위 차례(닫고 · 굽고 · 띄우기) 대신 **이것으로** 확인한다. 창은 화면 밖(-8000)에 포커스 ·
작업 표시줄 단추 없이 뜨고, 소리는 꺼지고, 세이브 · 설정은 시험 폴더에 적힌다(`%APPDATA%\CdsHelper` 는 읽기만).
스위치는 환경 변수다(`CdsHelper.Support/Local/Helpers/OffscreenTest.cs`, 장치는 `CdsHelper.Game/UI/Testing/OffscreenHarness.cs`).
없으면 아무 일도 안 한다.

| 변수 | 하는 일 |
|---|---|
| `CDS_OFFSCREEN=1` | 화면 밖 · 포커스 없이(이 실의 창은 CBT 훅으로 아예 활성이 안 된다 — 커서도 배를 안 몬다) |
| `CDS_SAVEDIR=<폴더>` | 사용자 자료 폴더(`UserDataFolder.Roaming`)를 그 폴더로. 업데이트 · 통계 · 내려받기 · 처음 묻는 창 · 오류 알림(클립보드)도 끈다 |
| `CDS_MUTE=1` | 소리 끔(화면 밖이면 저절로, `CDS_MUTE=0` 이면 낸다) |
| `CDS_GAMEDIR=<폴더>` | 원본 게임 폴더. 없으면 사용자 `settings.json` 의 세이브 자리를 읽기만 해서 쓴다 |
| `CDS_START=sea:<도시>:<선체 key>` | 타이틀을 건너뛰고 라몬이 그 배 한 척으로 그 도시 앞바다에서 항해 지도에 든다(암스테르담 24 · 제노바 28 · 리스본 …) |
| `CDS_HEADING=<0~7>` · `CDS_SAIL=1` | 뱃머리(0 북 · 2 서 · 4 남 · 6 동 · 7 북동, `ShipRegistry.DirectionNames` 차례)와 저절로 나아가기 |
| `CDS_SHOT=<png>:<초>[;…]` | **바다로 든 뒤** 그 초에 찍고, 마지막 것을 찍으면 스스로 닫힌다 |
| 메시지 `0x8001` | 창에 `PostMessage` 하면 `<SAVEDIR 또는 %TEMP%>\cds_snapshot.png`. 창 핸들은 같은 폴더 `cds_hwnd.txt` |

도우미 스크립트가 굽기(따로 둔 폴더로 — 사용자의 `bin\Debug` 는 띄워 둔 앱이 잠그고 있을 수 있다) · 띄우기 · 기다리기 · 끄기를 한다.

```powershell
pwsh -File tools/offscreen-shot.ps1 -Build -OutDir <스크래치>\cds-shot -Start sea:24:ketch -Heading 2 -Sail -Shots 0.5,1.5 -Name ketch-west
pwsh -File tools/offscreen-shot.ps1 -Exe <스크래치>\cds-shot\build\CostaDelSol.exe -OutDir <스크래치>\cds-shot -PostAt 8   # 타이틀을 떠 둔 채 한 장
```

- 찍은 PNG 는 `Read` 로 **직접 열어** 본다. 작은 것은 PIL 로 잘라 키워 본다(배 그림 따위).
- 케치는 빠르다 — 암스테르담에서 서쪽으로 6초면 브리튼 해안에 막힌다. 항해 중 그림은 1~3초에 찍는다.
- 끄는 것은 **스크립트가 띄운 PID 만**이다. `taskkill /IM` 은 쓰지 않는다(사용자가 띄워 둔 게임까지 죽는다).
  끝나면 `Get-Process CostaDelSol` 로 내가 띄운 것이 안 남았는지 본다.
- 못 찍는 것: WPF `Popup`(좌표 상자 · 미니맵 · 비 · 눈 — 창이 활성일 때만 뜨므로 화면 밖에서는 원래 없다)과 딴 실의 창(불러오는 쪽지).
- `CDS_OFFSCREEN` 은 늘 `CDS_SAVEDIR` 와 함께 건다 — 혼자 걸면 시험 판이 사용자 세이브에 적는다(스크립트는 늘 건다).
