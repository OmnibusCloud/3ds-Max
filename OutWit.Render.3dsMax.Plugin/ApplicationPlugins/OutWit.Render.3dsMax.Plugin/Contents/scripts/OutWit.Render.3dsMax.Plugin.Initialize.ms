global OutWit_3dsMax_ResolvePackageRoot
global OutWit_3dsMax_ResolveAssemblyPath
global OutWit_3dsMax_EnsurePluginLoaded
global OutWit_3dsMax_ShowExportWindow
global OutWit_3dsMax_RegisterMenu

dotNet.loadAssembly "System"

fn OutWit_3dsMax_ResolvePackageRoot =
(
    local pathClass = dotNetClass "System.IO.Path"
    local scriptDirectory = getFilenamePath (getThisScriptFilename())
    pathClass.GetFullPath (pathConfig.appendPath scriptDirectory "..")
)

fn OutWit_3dsMax_ResolveAssemblyPath =
(
    local pathClass = dotNetClass "System.IO.Path"
    local packageRoot = OutWit_3dsMax_ResolvePackageRoot()
    pathClass.GetFullPath (pathConfig.appendPath packageRoot "bin\\OutWit.Render.3dsMax.Plugin.dll")
)

fn OutWit_3dsMax_EnsurePluginLoaded =
(
    local assemblyPath = OutWit_3dsMax_ResolveAssemblyPath()

    if doesFileExist assemblyPath == false do
        throw ("OutWit Export assembly was not found: " + assemblyPath)

    dotNet.loadAssembly assemblyPath
    assemblyPath
)

fn OutWit_3dsMax_ShowExportWindow =
(
    try
    (
        OutWit_3dsMax_EnsurePluginLoaded()
        local bootstrap = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Services.MaxPluginBootstrap"
        bootstrap.ShowExportWindow()
        true
    )
    catch
    (
        local message = getCurrentException() as string
        format "OutWit Export failed: %\n" message
        messageBox ("OutWit Export failed:\n" + message) title:"OutWit Export"
        false
    )
)

fn OutWit_3dsMax_RegisterMenu =
(
    local menuMgr = callbacks.notificationParam()

    if menuMgr == undefined do
        return false

    local mainMenuBar = menuMgr.mainMenuBar
    local helpId = "cee8f758-2199-411b-81e7-d3ff4a80d143"
    local outWitMenuId = "A1A0A6B8-AC2E-4D23-BD95-FD0FEAEE9D9A"
    local outWitActionId = "9DB6D1E7-13BF-4C11-8EC7-9866C3BD4B5A"

    if (menuMgr.GetMenuItemById outWitMenuId) != undefined do
        return true

    local newSubMenu
    if (menuMgr.GetMenuItemById helpId) == undefined then
        newSubMenu = mainMenuBar.CreateSubMenu outWitMenuId "OutWit"
    else
        newSubMenu = mainMenuBar.CreateSubMenu outWitMenuId "OutWit" beforeId:helpId

    local actionMenu = newSubMenu.CreateAction outWitActionId 647394 "OutWitExport`OutWit"
    actionMenu.isFlat = true
    true
)

callbacks.removeScripts id:#OutWitRender3dsMaxMenu
callbacks.addScript #cuiRegisterMenus OutWit_3dsMax_RegisterMenu id:#OutWitRender3dsMaxMenu
