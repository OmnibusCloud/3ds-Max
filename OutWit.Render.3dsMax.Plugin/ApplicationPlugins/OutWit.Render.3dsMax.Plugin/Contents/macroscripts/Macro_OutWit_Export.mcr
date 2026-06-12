macroScript OutWitExport
category:"OutWit"
tooltip:"Open OutWit Export"
buttonText:"OutWit Export"
(
    if OutWit_3dsMax_ShowExportWindow != undefined then
        OutWit_3dsMax_ShowExportWindow()
    else
        messageBox "OutWit Export startup script is not loaded." title:"OutWit Export"
)
