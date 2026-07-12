macroScript OutWitPortal
category:"OmnibusCloud"
tooltip:"Open the OmnibusCloud portal in your browser"
buttonText:"Open Portal…"
iconName:"OmnibusCloud/Portal"
(
    on execute do
    (
        if OutWit_3dsMax_OpenPortal != undefined then
            OutWit_3dsMax_OpenPortal()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
