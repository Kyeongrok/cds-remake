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
| `CDS_START=land:<도시>:<선체 key>:<x>:<y>` | 같은 바로 시작인데 배는 그 도시 앞바다에 대 두고 말을 그 칸(가까운 육지)에 세운다 — 발견물 자리 사건을 밟아 볼 때(로제타석: `land:78:ketch:1460.5:407.5` + 뱃머리 6 · 걷기) |
| `CDS_DIALOG=bugreport` | 왼쪽 위 메뉴로 연 것처럼 버그 리포트 창을 띄운다 — 메뉴 창이 떠 있는 동안 배 · 말이 서는지 본다 |
| `CDS_DIALOG=personmove-gate:<도시>` | `CDS_START=sea:<도시>:…` 와 함께 — 그 도시에 들어가 도시락 「인물 이동」을 띄운 채 성문으로 나서고 창을 닫은 뒤 커맨드 창을 열었다 닫는다. 걸음마다 지도 상태(멈춤 · 묻는 중 · 도시 안)를 기록에 적는다(버그 리포트 #44) |
| `CDS_DIALOG=mod[:<찾는 말>]` | 모드 창을 띄우고 찾기 칸에 그 말을 넣는다(`mod:자동` — 모든 탭을 가로질러 걸러진 화면). `CDS_START` 와 함께 쓴다 |
| `CDS_DIALOG=sound` | 2초마다 다섯 번 효과음(닻)을 내고 소리 진단(버그 리포트의 `sound` — 곡 상태 · 재생 위치 · 트는 손 · 기본 출력 장치 · 앱 음량)을 기록에 적는다. 소리를 내려면 `CDS_MUTE=0`(크기는 시험 폴더 `game-settings.json` 의 `BgmVolume`/`SfxVolume` 0 으로 조용히). `CDS_AUDIO=winrt` 면 WPF MediaPlayer 대신 WinRT 쪽으로 튼다 |
| `CDS_DIALOG=storm` | `CDS_START=sea:…` 와 함께 — 지도를 멈춘 채 폭풍 장면만 돌리고, 앞 · 도는 동안(1초마다) · 뒤의 소리 진단(곡 상태 · 장치 음량 · 앱 음량 · 장치 음량이 바뀐 자취 `deviceChanges`)을 기록에 적는다(버그 리포트 #45). 곡을 들으려면 `CDS_MUTE=0` 과 시험 폴더 `game-settings.json` 의 작은 `BgmVolume` |
| `CDS_DIALOG=landbattle-sound` | `CDS_START=sea:…` 와 함께 — 육상전이 곡을 다루는 차례(전투 곡 → 끝맺음 숨죽임 → 듣던 곡)와 해전 · 일기토 승패 소리의 멈춤(Pause → Resume)을 1초 간격으로 밟으며 곡 상태 · 장치 음량 · `deviceChanges` 를 적는다(버그 리포트 #46 · #47). 흐름이 안 끊기면 내내 `wpf Playing` 이고 자리가 흐른다. `CDS_MUTE=0` 과 시험 폴더 `game-settings.json` 의 `BgmVolume` 0 |
| `CDS_DIALOG=areapick[:x1,y1,x2,y2[,1]]` | 사람 발견물 창의 「지도에서 고르기…」(세계지도 고르기 모드)를 그 범위에서 띄운다(끝의 1 은 육지). `CDS_START` 없이 걸면 타이틀에서 WORLD.CDS 로 지은 지도로 뜬다 |
| `CDS_FOUND=<이름>[,<이름>…]` | 바로 시작한 뒤 그 이름의 발견물들을 찾은 것으로 적고 지도를 다시 짓는다 — 사람 발견물 그림이 지도에 서는지 볼 때(사람 발견물은 `<SAVEDIR>\discoveries\<uuid>\discovery.json` 에 미리 넣어 두고 `-KeepSave`) |
| `CDS_DIALOG=childname` | 자녀 이름 다시 짓기 창(선명입력 창)을 띄우고 그 창이 쥔 초점(글상자 · 고른 글자 수)을 기록에 적은 뒤 닫는다 |
| `CDS_DIALOG=dev:<탭>` | 개발 창을 그 탭으로 띄운다(`dev:에디터` = 주인공 능력치 · 기능 · 언어 고치기). `CDS_START` 와 함께 쓴다 |
| `CDS_DIALOG=userdiscovery[:<탭>]` | 도시락 「발견물 추가」처럼 커스텀 발견물 창을 그 탭(`userdiscovery:아이템`)으로 띄운다. `CDS_ITEMPIC=<그림>` 이면 고른 아이템의 그림 칸에 그 그림을 올린다(120x120 에 통째로 맞춘 첫 자리). 지은 아이템 · 발견물은 `CDS_SAVEDIR` 의 `items` · `discoveries` 에서 읽는다 |
| `CDS_TITLE=Down,Enter` | 타이틀에서 그 글쇠들을 1초 간격으로 누른다(LOAD GAME = `Down,Enter`, CONTINUE = `Down,Down,Enter`). 기록에 세이브 자리 요약(`GameSave.Census`)과 1초마다 화면 · 마지막 불러오기 결과를 적는다 |
| `CDS_AUTOKEY=Enter` | `CDS_TITLE` 과 함께 — 새로 뜬 창마다 종류 · 물음 글(ConfirmDialog 는 이름으로)을 적고 그 글쇠를 한 번 보낸다(목록은 맨 위 칸, 물음은 「예」) |
| `CDS_NAV=<도시>` | `CDS_START` 와 함께 — 네비게이션에서 그 도시의 첫 길을 고른 것처럼 자동항해를 걸고, 1초마다 배 자리 · 마디 · 어귀 · 끝난 말 · 뜬 창을 기록에 적는다(`sea:75:large-carrack` + `78` = 파마구스타에서 알렉산드리아) |
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
