-- 힌트 정보 단추 — 누르면 단축키 H 와 같은 힌트 정보 창이 열립니다.
-- 끌어 옮길 수 있고, 옮긴 자리는 다음에도 그대로입니다.

local frame = CreateFrame("HintButtonFrame")
frame:SetPoint("TOPRIGHT", 10, 10)
frame:SetMovable(true)

frame:AddButton("힌트 정보", function()
    OpenWindow("hints")
end)
