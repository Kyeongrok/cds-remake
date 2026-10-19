-- 보급 HUD — 애드온 예시. 고쳐 보고 개발 창 「애드온」 탭의 「다시 읽기」나 도시락 「애드온」 목록의 「다시 읽기」를 누르면 바로 바뀝니다.

-- 저장 변수(toc 의 SavedVariables)는 이 파일보다 먼저 되살아납니다. 처음이면 nil 입니다.
SupplyHudDB = SupplyHudDB or { folded = false, found = 0 }

local frame = CreateFrame("SupplyHudFrame")   -- 이름이 있으면 끌어 옮긴 자리를 기억합니다
frame:SetPoint("TOPLEFT", 10, 120)
frame:SetMovable(true)

local title = frame:AddText("보급 HUD")
title:SetTextColor(1, 0.85, 0.4)
local dateLine = frame:AddText()
local goldLine = frame:AddText()
local supplyLine = frame:AddText()
local fleetLine = frame:AddText()
local foundLine = frame:AddText()

local function Refresh()
    local y, m, d = GetDate()
    if not y then return end   -- 제목 화면이면 값이 없습니다
    dateLine:SetText(string.format("%d년 %d월 %d일", y, m, d))
    goldLine:SetText(string.format("금화 %d닢", GetGold()))

    local s = GetSupply()
    local days = GetSupplyDays()
    supplyLine:SetText(string.format("식량 %d · 물 %d  (%d일)", s.food, s.water, days))
    if days < 10 then supplyLine:SetTextColor(1, 0.4, 0.4) else supplyLine:SetTextColor(1, 1, 1) end

    local hp, max = 0, 0
    for _, ship in ipairs(GetShips()) do
        hp = hp + ship.hp
        max = max + ship.maxHp
    end
    fleetLine:SetText(max > 0 and string.format("함대 내구 %d / %d", hp, max) or "배 없음")
    foundLine:SetText(string.format("애드온이 센 발견 %d · 모두 %d", SupplyHudDB.found, GetDiscoveryCount()))
end

local lines = { dateLine, goldLine, supplyLine, fleetLine, foundLine }
local function Fold(folded)
    SupplyHudDB.folded = folded
    for _, line in ipairs(lines) do
        if folded then line:Hide() else line:Show() end
    end
end

frame:AddButton("접기 / 펴기", function() Fold(not SupplyHudDB.folded) end)

frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("DAY_CHANGED")
frame:RegisterEvent("GOLD_CHANGED")
frame:RegisterEvent("CITY_ENTERED")
frame:RegisterEvent("DISCOVERY_FOUND")
frame:SetScript("OnEvent", function(self, event, ...)
    if event == "DISCOVERY_FOUND" then
        local id, name = ...
        SupplyHudDB.found = SupplyHudDB.found + 1
        print(name .. " 발견!")
    elseif event == "CITY_ENTERED" then
        local id, name = ...
        print(name .. "에 들어왔습니다.")
    end
    Refresh()
end)

-- 이벤트가 안 오는 값(배 내구 따위)도 가끔 다시 읽습니다.
C_Timer.NewTicker(1, Refresh)

Fold(SupplyHudDB.folded)
Refresh()
