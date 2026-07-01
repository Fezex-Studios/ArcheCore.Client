UI.CreatePanel("levelPanel", 20, 20, 200, 80)
UI.CreateText("levelLabel", "Level: ?", 30, 50, 180, 30, "levelPanel")
UI.CreateButton("levelBtn", "Get Level", 30, 15, 140, 30, "OnGetLevelClicked", "levelPanel")

function OnGetLevelClicked(id)
    UI.SetText("levelLabel", "Level: ...")
    Net.RequestPlayerLevel()
end

function OnPlayerLevelReceived(level)
    UI.SetText("levelLabel", "Level: " .. level)
end