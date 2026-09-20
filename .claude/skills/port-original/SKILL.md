---
name: port-original
description: 원본(CDS_95.EXE)에 있는데 앱에 없는 것을 하나 옮긴다. 사용자가 "다음꺼", "순서대로 진행", "안 옮긴 거 해" 라고 하거나 원본과 다른 점을 고쳐 달라고 할 때 쓴다. 분석 → 구현 → 빌드 → 메모리 → 커밋·푸시를 묻지 않고 끝까지 돈다.
---

# 원본에 맞춰 하나씩 옮기기

한 번 부르면 **한 가지**를 끝까지 옮긴다 — 분석하고, 짓고, 굽고, 적어 두고, 커밋·푸시까지.
**중간에 묻지 않는다.** 사용자가 이미 「순서대로 진행하고 하나 할 때마다 커밋」이라고 일러 두었다.

**한 번 부르면 멈추지 않는다.** 한 가지를 끝내고 커밋했으면 <b>곧바로 다음 것을 골라 또 한다</b> —
사용자가 「다음」이라고 다시 말해 주기를 기다리지 않는다. 사용자는 이미 「두 시간 동안은 내가 계속 다음 한 것과
같은 효과」라고 일러 두었다. 그러니 **두 시간쯤 이어서 하고**, 그때 한 번만 무엇을 해 왔는지 추려 묻는다.

무엇을 할지 · 커밋해도 되는지 · 실행해 볼지를 **되묻지 않는다**. 마음에 안 드는 것이 있으면 사용자가
「그 커밋 빼라」고 말할 것이므로, <b>한 커밋에 한 가지만</b> 담아 되돌리기 쉽게 둔다.
한 차례에 여러 개를 해도 좋다 — 분석을 서브에이전트에 맡겨 두고 기다리는 동안 다른 작은 것을 집어 끝내면
쉬는 시간이 없다. 다만 커밋은 갈래마다 따로 한다.

말은 <b>다 하고 나서 한 번</b> 하면 된다 — 한 가지 끝낼 때마다 길게 알리지 말고, 여러 개를 해 놓고 한 번에
추려 적는다(커밋 해시와 원본에서 밝힌 규칙, 아직 다른 점).

## 무엇을 고를까

1. 손잡이가 아예 없는 시설 줄부터다(흐린 줄). 이렇게 찾는다.

   ```bash
   python - <<'EOF'
   import re
   works=set(re.findall(r'new\(TownWork\.(\w+)', open('CdsHelper.Game/Engine/Town/TownWork.cs',encoding='utf-8').read()))
   mapped=set(re.findall(r'TownWork\.(\w+)', open('CdsHelper.Game/Engine/Menu/TownMenu.cs',encoding='utf-8').read()))
   print(sorted(works-mapped))
   EOF
   ```

2. 그 다음은 주석에 적어 둔 것들이다.

   ```bash
   grep -rn "아직 안 옮\|아직 안 붙\|안 옮겼다\|아직 흉내" --include=*.cs CdsHelper.Game
   ```

3. 사용자가 집어 준 것이 있으면 그것이 먼저다.

## 차례

### 1. 분석 (원본이 근거다)

도구는 스크래치패드에 있다(`cdsdis.py` · `citystate/full.txt` · `strs.py` · `findk.py` ·
`findimm.py`). **스크립트를 `dis.py` 로 이름 짓지 말 것**, `strs.py` 는 스크래치패드에서만 돌린다
(저장소에 `strs.txt` 를 흘리지 않게).

- 한 함수만 보면 되는 작은 것은 직접 뜯는다.
- 화면 한 벌·규칙 한 덩이처럼 **여러 함수를 훑어야 하면 서브에이전트**에 맡기고(`run_in_background`),
  기다리는 동안 다른 손질을 한다. 물어볼 것: 정확한 주소·식·문구(원문 그대로)·표 값·부르는 곳.
- 원본 데이터의 흠(절대 안 걸리는 조건, 죽은 코드)은 **그대로 두고 주석에 적는다**.
- EXE 문자열을 훑어 빠진 것을 찾을 때는 `missing.py` → `missing2.py` 를 쓰되,
  **표에서 런타임에 읽는 글**(교역품·발견물 설명, 인물·마을 사람 말)과
  **힌트 패널 글**(`0x0040E0A0(0x580C48, …)`, [[reference_cds95_hint_panel]])은 걸러 낸다 —
  둘 다 옮길 것이 아니다.
- 대사가 하나만 있는 것 같아도 `0x004694C0`(말투 셋) · `0x00469680`(부관 있음/없음 둘)로
  여러 벌인 때가 많다. 문자열 자리 앞뒤를 `strs.py` 로 훑어 짝을 찾는다.

### 2. 구현

- 규칙은 `CdsHelper.Game/Engine/...`, 화면은 `UI/Views/...`, 저장 값은 `CdsHelper.Support` 의 `Player` 에 둔다.
- 세이브에 새 칸을 더하면 `Engine/GameSave.cs` 의 `Data` 와 `Save`, 그리고 `ShipMapWindow` 의 되돌리는 자리를
  함께 고친다. **옛 세이브가 열려야 한다**(없는 칸은 null 로 물러선다).
- 주석은 기존 문체대로 「~한다」로 쓰고 근거 주소(`0x004xxxxx`)를 함께 적는다. 문구는 원본 그대로 옮긴다.
- 여러 줄을 정확히 바꿀 때는 파이썬 패치 글(`patchlib.py` 의 `patch`)을 스크래치패드에 지어 쓴다 —
  CRLF·BOM 을 지켜 준다. 경로는 `/` 로 적는다(`\U` 가 파이썬 이스케이프로 깨진다).

### 3. 굽기

```bash
taskkill //F //IM CostaDelSol.exe 2>/dev/null; taskkill //F //IM Editor.exe 2>/dev/null
dotnet build cds-helper.sln -v q --nologo --no-restore
```

**커밋은 빌드가 지나야 한다.** `tail` 로 본 것만으로 믿지 말고 빌드를 커밋의 조건으로 건다 —
한 번 깨진 채로 올라간 적이 있다.

```bash
dotnet build cds-helper.sln -v q --nologo --no-restore >/dev/null 2>&1 && git add -A && git commit ...
```

값이 날짜·발견물처럼 굴러가는 것이면 스크래치패드의 `landcheck` 콘솔로 눈으로 대 본다
(`Program.cs` 를 잠깐 바꿔 돌리고 `Program.cs.bak` 으로 되돌린다).

### 4. 적어 두기

새로 밝힌 규칙은 메모리에 남긴다 — 이미 있는 `reference_cds95_*.md` 에 덧붙이고, 새 갈래면 새 파일을
짓고 `MEMORY.md` 에 한 줄 더한다.

### 5. 커밋·푸시

**묻지 않고** 커밋하고 민다. 한 가지를 옮겼으면 한 번 커밋이다.

```bash
git add -A && git commit -q -F - <<'EOF'
<무엇을 원본대로 고쳤는지 한 줄, 「~한다」 문체>

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: <이 세션 링크>
EOF
git push -q
```

### 6. 알리기

존댓말로 짧게 — 무엇을 옮겼는지, 원본에서 밝힌 규칙, **원본과 아직 다른 점**, 커밋 해시.
실행은 **묻지 않는다** — 사용자가 「실행」이라고 하면 그때 `run-game` 스킬로 띄운다.

## 하지 말 것

- 앱에 이미 있는 것을 다시 짓지 말 것 — 먼저 `grep` 으로 찾아보고, 어긋난 데만 고친다.
- 원본에 없는 편의 기능을 끼워 넣지 말 것(개발 창은 예외).
- 한 커밋에 여러 갈래를 섞지 말 것.
