macroScript OutWitSettings
category:"OmnibusCloud"
tooltip:"OmnibusCloud settings"
buttonText:"Settings…"
iconName:"OmnibusCloud/Settings"
(
    on execute do
    (
        if OutWit_3dsMax_ShowSettings != undefined then
            OutWit_3dsMax_ShowSettings()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
