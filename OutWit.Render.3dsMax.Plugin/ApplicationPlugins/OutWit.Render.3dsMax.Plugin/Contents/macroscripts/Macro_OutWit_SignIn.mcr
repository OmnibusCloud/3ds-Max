macroScript OutWitSignIn
category:"OmnibusCloud"
tooltip:"Sign in to OmnibusCloud"
buttonText:"Sign in…"
iconName:"OmnibusCloud/SignIn"
(
    -- Greyed out while a session is already active (the Sign out item takes over).
    on isEnabled return
    (
        try ( not (OutWit_3dsMax_IsSignedIn()) ) catch ( true )
    )

    on execute do
    (
        if OutWit_3dsMax_ShowSignIn != undefined then
            OutWit_3dsMax_ShowSignIn()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
