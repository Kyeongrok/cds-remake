# 애드온 만들기

개발 창(햄버거 → 개발) 「애드온」 탭에서 **애드온**을 켜면 이 폴더(`%APPDATA%\CdsHelper\AddOns`)의 애드온을 읽어 지도 위에 패널을 띄웁니다.
와우 애드온과 비슷하게 Lua로 짭니다(Lua 5.2, MoonSharp).

- 애드온은 게임 값을 **읽기만** 합니다. 돈이나 짐을 고치는 함수는 없습니다.
- 파일 읽기 · 쓰기, 프로그램 실행, `load` · `require` 는 막혀 있습니다.
- 끝없이 도는 고리(`while true do end`)는 막지 못합니다. 게임이 멈춥니다.
- 애드온을 켜 두면 하드코어 랭킹 기록에 모드처럼 적힙니다.
- 패널은 지도가 보일 때만 뜹니다. 도시 안이나 창이 떠 있을 때는 숨습니다.

## 폴더

```
AddOns\
  SupplyHud\            ← 애드온 하나에 폴더 하나 (폴더 이름은 영어로)
    SupplyHud.toc       ← 폴더 이름과 같은 이름의 toc
    SupplyHud.lua
  SavedVariables\       ← 게임이 적는 저장 변수 · 패널 자리. 지우면 처음부터
  addons.log            ← print 와 오류가 적힙니다
```

애드온마다 켜고 끄는 것은 제목 줄 왼쪽 위 도시락 단추(점 아홉)의 **애드온**에서 합니다. 새로 넣은 애드온은 켜진 채로 시작합니다.

파일을 고친 뒤에는 개발 창 「애드온」 탭의 **다시 읽기**를 누르거나 Lua 에서 `ReloadUI()` 를 부르면 게임을 끄지 않고 다시 읽습니다.

## toc

```
## Title: 보급 HUD
## Notes: 설명 한 줄
## SavedVariables: SupplyHudDB, SupplyHudOther
SupplyHud.lua
more.lua
```

- `## SavedVariables` 에 적은 전역 변수는 게임을 끌 때 저장되고, 다음에 켤 때 **lua 파일보다 먼저** 되살아납니다. 숫자 · 글 · 참거짓 · 표만 저장됩니다.
- 나머지 줄은 읽을 lua 파일입니다. 적은 차례대로 읽습니다. 애드온 폴더 밖 파일은 못 읽습니다.
- 파일은 UTF-8 로 저장하세요.

## 패널

```lua
local f = CreateFrame("MyFrame")      -- 이름을 주면 끌어 옮긴 자리를 기억합니다
f:SetPoint("TOPRIGHT", 10, 40)        -- 지도 기준 자리 + 안쪽으로 민 거리
f:SetMovable(true)                    -- 끌어 옮기기
local line = f:AddText("안녕하세요")    -- 글 한 줄 (위에서 아래로 쌓입니다)
line:SetTextColor(1, 0.8, 0.3)        -- 0~1
f:AddButton("눌러 보기", function(button) print("눌렀습니다") end)
```

| 함수 | 설명 |
|---|---|
| `CreateFrame([이름])` | 패널을 만듭니다. 처음에는 보이는 상태입니다 |
| `f:SetPoint(자리, x, y)` | `TOPLEFT` `TOP` `TOPRIGHT` `LEFT` `CENTER` `RIGHT` `BOTTOMLEFT` `BOTTOM` `BOTTOMRIGHT` |
| `f:SetSize(넓이, 높이)` | 0 이면 내용에 맞춥니다 |
| `f:SetBackdrop(r, g, b, [a])` | 바탕색. a 가 0 이면 바탕과 테두리가 없습니다 |
| `f:AddText([글])` | 글 줄을 붙이고 돌려줍니다 |
| `f:AddButton(글, [함수])` | 단추를 붙이고 돌려줍니다 |
| `f:Show()` `f:Hide()` `f:IsShown()` | |
| `f:SetMovable(참거짓)` | |
| `f:RegisterEvent(이벤트)` `f:UnregisterEvent(이벤트)` `f:UnregisterAllEvents()` | |
| `f:SetScript(이름, 함수)` | `OnEvent(self, 이벤트, ...)` · `OnUpdate(self, 지난 초)` · `OnShow(self)` · `OnHide(self)`. 함수 자리에 nil 이면 뗍니다 |
| `f:GetName()` | |
| 글 `t:SetText(글)` `t:GetText()` `t:SetTextColor(r, g, b, [a])` `t:SetFontSize(크기)` `t:Show()` `t:Hide()` | |
| 단추 `b:SetText(글)` `b:SetScript("OnClick", 함수)` `b:SetEnabled(참거짓)` `b:Show()` `b:Hide()` | |

글이나 바탕만 있는 패널은 마우스가 그냥 지나가 밑의 지도를 누를 수 있습니다. 단추가 있거나 끌 수 있는 패널은 마우스를 받습니다.
`OnUpdate` 는 0.1초마다 불립니다.

## 이벤트

| 이벤트 | 인자 |
|---|---|
| `ADDON_LOADED` | 애드온 폴더 이름 |
| `PLAYER_ENTERING_WORLD` | 새 게임 · 불러오기로 게임이 섰을 때 |
| `DAY_CHANGED` | 해, 달, 날 |
| `GOLD_CHANGED` | 지금 금화, 바뀐 만큼 |
| `CITY_ENTERED` | 도시 번호, 도시 이름 |
| `CITY_LEFT` | 도시 번호, 도시 이름 |
| `DISCOVERY_FOUND` | 발견물 번호, 발견물 이름 |
| `SEA_BATTLE_ENDED` | 결과 — `Won` · `Defeated` · `Escaped` · `Surrendered` |
| `LAND_BATTLE_ENDED` | 이겼는지 |

## 값 읽기

제목 화면처럼 게임이 서 있지 않으면 nil 을 돌려줍니다.

| 함수 | 돌려주는 것 |
|---|---|
| `GetPlayerName()` | 제독 이름 |
| `GetGold()` `GetFame()` `GetAge()` | |
| `GetCondition()` `GetFatigue()` `GetMorale()` | 컨디션 · 피로 · 사기 |
| `GetCrew()` | 선원 수 |
| `GetDate()` | 해, 달, 날 |
| `GetCity()` | 도시 번호, 이름 (도시 밖이면 nil) |
| `GetPosition()` | 위도, 경도 |
| `IsOnLand()` | 육지에 섰는지 |
| `GetSupply()` | `{ food, water, material, ammo }` 통 수 |
| `GetSupplyDays()` | 식량 · 물이 며칠 갈지 |
| `GetShips()` | 배마다 `{ name, hull, hp, maxHp, speed, maxSpeed, capacity, crewNeeded, guns, lent }` |
| `GetCargo()` | 교역품마다 `{ name, count, months, spoiled }` |
| `GetDiscoveryCount()` | 찾은 발견물 수 |
| `GetTime()` | 게임을 켠 뒤 흐른 초 |
| `GetLocale()` | `ko` · `ja` |
| `GetAddOnName()` | 이 애드온 폴더 이름 |

## 그 밖

- `print(...)` — `addons.log` 에 적고 화면 아래 띠에도 띄웁니다.
- `C_Timer.After(초, 함수)` — 한 번 부릅니다.
- `C_Timer.NewTicker(초, 함수)` — 계속 부릅니다. 돌려받은 것의 `:Cancel()` 로 멈춥니다.
- `ReloadUI()` — 애드온을 모두 다시 읽습니다.
- `OpenWindow(이름)` — 단축키로 여는 게임 창을 엽니다. `hints`(힌트 정보) · `items`(소지품) · `person`(인물정보) · `patrons`(후원자 정보) · `cargo`(매입 정보). 열 수 있는 이름이면 true 를 돌려줍니다.
- 오류가 나면 첫 번째는 화면 아래 띠에 뜨고, 열 번 나면 그 애드온만 멈춥니다. 모두 `addons.log` 에 적힙니다.
- 애드온끼리는 전역 변수를 나눠 쓰지 않습니다.
