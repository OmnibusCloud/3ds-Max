macroScript OutWitSignInOut
category:"OmnibusCloud"
tooltip:"Sign in to or out of OmnibusCloud"
buttonText:"Sign in / out"
(
    -- One toggle item: sign out when signed in, otherwise open the sign-in dialog (design 4.4 / MX-17).
    on execute do
    (
        if OutWit_3dsMax_ToggleSignInOut != undefined then
            OutWit_3dsMax_ToggleSignInOut()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
