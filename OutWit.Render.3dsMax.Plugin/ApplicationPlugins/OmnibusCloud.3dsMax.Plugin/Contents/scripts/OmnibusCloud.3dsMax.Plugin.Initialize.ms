global OutWit_3dsMax_Bootstrap
global OutWit_3dsMax_ResolvePackageRoot
global OutWit_3dsMax_ResolveAssemblyPath
global OutWit_3dsMax_EnsurePluginLoaded
global OutWit_3dsMax_EnsureBootstrap
global OutWit_3dsMax_ReportFailure
global OutWit_3dsMax_ShowRenderDialog
global OutWit_3dsMax_ShowExportDialog
global OutWit_3dsMax_ShowSettings
global OutWit_3dsMax_ShowSignIn
global OutWit_3dsMax_SignOut
global OutWit_3dsMax_ShowAbout
global OutWit_3dsMax_OpenPortal
global OutWit_3dsMax_IsSignedIn
global OutWit_3dsMax_WarmUpSession
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
    pathClass.GetFullPath (pathConfig.appendPath packageRoot "bin\\OmnibusCloud.3dsMax.Plugin.dll")
)

fn OutWit_3dsMax_EnsurePluginLoaded =
(
    local assemblyPath = OutWit_3dsMax_ResolveAssemblyPath()

    if doesFileExist assemblyPath == false do
        throw ("OmnibusCloud assembly was not found: " + assemblyPath)

    dotNet.loadAssembly assemblyPath
    assemblyPath
)

-- One shared bootstrap (hence one ApplicationViewModel / cloud session) across every dialog.
fn OutWit_3dsMax_EnsureBootstrap =
(
    if OutWit_3dsMax_Bootstrap == undefined do
    (
        OutWit_3dsMax_EnsurePluginLoaded()
        OutWit_3dsMax_Bootstrap = dotNetObject "OutWit.Render.ThreeDsMax.Plugin.Services.MaxPluginBootstrap"
    )
    OutWit_3dsMax_Bootstrap
)

fn OutWit_3dsMax_ReportFailure title =
(
    local message = getCurrentException() as string
    format "% failed: %\n" title message
    messageBox (title + " failed:\n" + message) title:title
    false
)

fn OutWit_3dsMax_ShowRenderDialog =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).ShowRenderDialog(); true )
    catch ( OutWit_3dsMax_ReportFailure "OmnibusCloud Render" )
)

fn OutWit_3dsMax_ShowExportDialog =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).ShowExportDialog(); true )
    catch ( OutWit_3dsMax_ReportFailure "OmnibusCloud Export" )
)

fn OutWit_3dsMax_ShowSettings =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).ShowSettings(); true )
    catch ( OutWit_3dsMax_ReportFailure "OmnibusCloud Settings" )
)

fn OutWit_3dsMax_ShowSignIn =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).ShowSignIn(); true )
    catch ( OutWit_3dsMax_ReportFailure "OmnibusCloud Sign in" )
)

fn OutWit_3dsMax_SignOut =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).SignOut(); true )
    catch ( OutWit_3dsMax_ReportFailure "OmnibusCloud Sign out" )
)

fn OutWit_3dsMax_OpenPortal =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).OpenPortal(); true )
    catch ( OutWit_3dsMax_ReportFailure "OmnibusCloud Portal" )
)

-- About menu item: the same Settings dialog, opened straight on the About tab (design 1.2).
fn OutWit_3dsMax_ShowAbout =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).ShowAbout(); true )
    catch ( OutWit_3dsMax_ReportFailure "About OmnibusCloud" )
)

-- Gate query for the Render/Export/Sign-in-out menu items. Must NOT create the bootstrap, so hovering
-- the menu has no side effects: no bootstrap yet means no session, hence not signed in.
fn OutWit_3dsMax_IsSignedIn =
(
    try ( if OutWit_3dsMax_Bootstrap == undefined then false else OutWit_3dsMax_Bootstrap.IsSignedIn )
    catch false
)

-- Startup warm-up: creates the bootstrap and kicks the silent session restore in the background, so
-- the menu gate reflects the persisted session without the user opening a dialog first.
fn OutWit_3dsMax_WarmUpSession =
(
    try ( (OutWit_3dsMax_EnsureBootstrap()).WarmUpSession(); true )
    catch ( false )
)

fn OutWit_3dsMax_RegisterMenu =
(
    local menuMgr = callbacks.notificationParam()

    if menuMgr == undefined do
        return false

    local mainMenuBar = menuMgr.mainMenuBar
    local helpId = "cee8f758-2199-411b-81e7-d3ff4a80d143"
    local menuId = "B5F1E2C0-1A4D-4E2B-9C3A-7D6E5F4A3B21"

    -- Back-migration from the 1.0.3-test dynamic-header experiment: a persisted menu carrying the
    -- account header item (its macroscript no longer ships) is dropped and recreated below.
    -- (Live-Max finding, 2026-07-12: structure changes at registration work fine, but
    -- ICuiMenuItem.SetTitle never updated the visible Qt menu — the header stayed static.)
    if (menuMgr.GetMenuItemById "B5F1E2C0-0008-4E2B-9C3A-000000000008") != undefined do
        try ( mainMenuBar.DeleteItem menuId ) catch ()

    if (menuMgr.GetMenuItemById menuId) != undefined do
        return true

    local sub
    if (menuMgr.GetMenuItemById helpId) == undefined then
        sub = mainMenuBar.CreateSubMenu menuId "OmnibusCloud"
    else
        sub = mainMenuBar.CreateSubMenu menuId "OmnibusCloud" beforeId:helpId

    -- macroScript actions live in the "Macros" action table (647394); the "Name`category" string
    -- selects the macro. Canon (design 1.1): scene actions | account/config | external — three
    -- groups split by separators. Render/Export grey out until signed in; Sign in / Sign out are
    -- separate items gated by their own isEnabled handlers (only the relevant one is active).
    sub.CreateAction "B5F1E2C0-0001-4E2B-9C3A-000000000001" 647394 "OutWitRender`OmnibusCloud"
    sub.CreateAction "B5F1E2C0-0002-4E2B-9C3A-000000000002" 647394 "OutWitExportScene`OmnibusCloud"
    try ( sub.CreateSeparator "B5F1E2C0-00A1-4E2B-9C3A-0000000000A1" ) catch ()
    sub.CreateAction "B5F1E2C0-0003-4E2B-9C3A-000000000003" 647394 "OutWitSignIn`OmnibusCloud"
    sub.CreateAction "B5F1E2C0-0006-4E2B-9C3A-000000000006" 647394 "OutWitSignOut`OmnibusCloud"
    sub.CreateAction "B5F1E2C0-0004-4E2B-9C3A-000000000004" 647394 "OutWitSettings`OmnibusCloud"
    try ( sub.CreateSeparator "B5F1E2C0-00A2-4E2B-9C3A-0000000000A2" ) catch ()
    sub.CreateAction "B5F1E2C0-0005-4E2B-9C3A-000000000005" 647394 "OutWitPortal`OmnibusCloud"
    sub.CreateAction "B5F1E2C0-0007-4E2B-9C3A-000000000007" 647394 "OutWitAbout`OmnibusCloud"

    -- The persisted session restores in the background right at startup (menu gate turns green
    -- without a manual sign-in).
    OutWit_3dsMax_WarmUpSession()

    true
)

callbacks.removeScripts id:#OutWitRender3dsMaxMenu
callbacks.addScript #cuiRegisterMenus OutWit_3dsMax_RegisterMenu id:#OutWitRender3dsMaxMenu
