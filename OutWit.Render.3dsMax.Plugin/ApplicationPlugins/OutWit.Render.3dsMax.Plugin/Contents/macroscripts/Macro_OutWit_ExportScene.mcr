macroScript OutWitExportScene
category:"OmnibusCloud"
tooltip:"Export the scene for OmnibusCloud"
buttonText:"Export Scene…"
(
    -- Greyed out until a cloud session is active (design MX-17). Evaluated each time the menu opens.
    on isEnabled return
    (
        try ( OutWit_3dsMax_IsSignedIn() ) catch ( false )
    )

    on execute do
    (
        if OutWit_3dsMax_ShowExportDialog != undefined then
            OutWit_3dsMax_ShowExportDialog()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
