function ShowConfirmDialog(message)
    UI:CreatePanel("confirmpanel", 0, 0, 400, 200)
    UI:SetColor("confirmpanel", 0.1, 0.1, 0.1, 0.9)

    UI:CreateText("confirmtext", message, 0, 40, 350, 60, "confirmpanel")

    UI:CreateButton("confirm_yes", "Yes", -80, -60, 120, 40, "OnConfirmYes", "confirmpanel")
    UI:CreateButton("confirm_no", "No", 80, -60, 120, 40, "OnConfirmNo", "confirmpanel")
end

function OnConfirmYes(buttonId)
    UI:DestroyElement("confirmpanel")
end

function OnConfirmNo(buttonId)
    UI:DestroyElement("confirmpanel")
end
ShowConfirmDialog("Are you sure?")