macroScript OutWitAbout
category:"OmnibusCloud"
tooltip:"About OmnibusCloud 3ds Max"
buttonText:"About OmnibusCloud 3ds Max"
iconName:"OmnibusCloud/About"
(
    on execute do
    (
        if OutWit_3dsMax_ShowAbout != undefined then
            OutWit_3dsMax_ShowAbout()
        else
            messageBox "OmnibusCloud startup script is not loaded." title:"OmnibusCloud"
    )
)
