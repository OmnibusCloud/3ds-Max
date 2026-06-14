macroScript OutWitRender
category:"OmnibusCloud"
tooltip:"Render on OmnibusCloud"
buttonText:"Render on OmnibusCloud…"
(
    -- Greyed out until a cloud session is active (design MX-17). Evaluated each time the menu opens.
    on isEnabled return
    (
        try ( OutWit_3dsMax_IsSignedIn() ) catch ( false )
    )

    on execute do
    (
        if OutWit_3dsMax_ShowRenderDialog != undefined then
            OutWit_3dsMax_ShowRenderDialog()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
