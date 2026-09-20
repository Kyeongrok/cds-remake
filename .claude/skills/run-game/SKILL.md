---
name: run-game
description: 놀이 앱(CostaDelSol)을 닫고, 다시 구워, 띄운다. 사용자가 "실행", "실행해줘", "띄워줘", "돌려봐" 라고 하거나, 코드를 고쳐 빌드가 통과한 뒤 화면으로 확인할 일이 있을 때 쓴다.
---

# 놀이 앱 실행

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
- 앱을 닫으면 하던 판이 날아간다. 사용자가 놀이 중일 수 있으니 **닫는다는 말을 한 줄
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
