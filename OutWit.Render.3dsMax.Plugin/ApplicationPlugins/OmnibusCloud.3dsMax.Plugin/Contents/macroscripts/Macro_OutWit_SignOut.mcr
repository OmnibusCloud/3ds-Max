macroScript OutWitSignOut
category:"OmnibusCloud"
tooltip:"Sign out of OmnibusCloud"
buttonText:"Sign out"
iconName:"OmnibusCloud/SignOut"
(
    -- Enabled only while a session is active.
    on isEnabled return
    (
        try ( OutWit_3dsMax_IsSignedIn() ) catch ( false )
    )

    on execute do
    (
        if OutWit_3dsMax_SignOut != undefined then
            OutWit_3dsMax_SignOut()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
