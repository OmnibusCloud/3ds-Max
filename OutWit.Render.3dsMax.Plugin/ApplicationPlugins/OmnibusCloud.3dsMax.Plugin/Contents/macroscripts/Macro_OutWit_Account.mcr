macroScript OutWitAccount
category:"OmnibusCloud"
tooltip:"OmnibusCloud account"
buttonText:"Not connected"
(
    -- Passive account header (design canon 1.1): the first menu item shows who is signed in and
    -- never executes anything. The live title ("Signed in: <name>" / "Not connected") is applied
    -- via ICuiMenuItem.SetTitle from Initialize.ms whenever the session state changes.
    on isEnabled return false

    on execute do ( )
)
