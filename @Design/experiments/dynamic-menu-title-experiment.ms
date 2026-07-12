-- OmnibusCloud dynamic-menu experiment (ship-plan post-ship tail: dynamic account header).
-- Validates, on a live Max with plugin 1.0.2 installed, the whole chain the feature needs:
--   1. the ICuiMenuManager handed to #cuiRegisterMenus stays usable when stashed in a global;
--   2. GetMenuItemById finds our registered action item outside the registration callback;
--   3. ICuiMenuItem.SetTitle is exposed to MAXScript and changes the visible menu label;
--   4. the Qt menu picks the new title up on next open (deferred menu build).
--
-- HOW TO RUN (in 3ds Max):
--   1. Scripting > Run Script... > this file  (or drag-drop it into a viewport).
--   2. Switch workspace: Workspaces dropdown (top toolbar) -> pick any other workspace,
--      then switch back. This forces a menu rebuild so the callback fires.
--   3. In the Listener run:  OC_Exp_TryUpdate()
--   4. Open the OmnibusCloud menu: the Render item should read "* DYNAMIC TITLE TEST *".
--   5. Paste the whole Listener output back to the chat.
--   6. Revert + cleanup:  OC_Exp_Revert()

global OC_Exp_MenuMgr
global OC_Exp_Item
global OC_Exp_SavedTitle

fn OC_Exp_OnRegister =
(
    OC_Exp_MenuMgr = callbacks.notificationParam()
    format "[OC-EXP] cuiRegisterMenus fired, manager stashed: %\n" OC_Exp_MenuMgr
    true
)

fn OC_Exp_TryUpdate =
(
    if OC_Exp_MenuMgr == undefined then
    (
        format "[OC-EXP] manager NOT captured yet — switch workspace away and back first\n"
        false
    )
    else
    (
        format "[OC-EXP] manager: %\n" OC_Exp_MenuMgr

        -- The Render action item registered by the installed plugin
        OC_Exp_Item = OC_Exp_MenuMgr.GetMenuItemById "B5F1E2C0-0001-4E2B-9C3A-000000000001"
        if OC_Exp_Item == undefined then
        (
            format "[OC-EXP] item NOT found by id\n"
            false
        )
        else
        (
            format "[OC-EXP] item found: %\n" OC_Exp_Item
            try ( showInterfaces OC_Exp_Item ) catch ( try ( showInterface OC_Exp_Item ) catch ( format "[OC-EXP] showInterface failed\n" ) )

            OC_Exp_SavedTitle = try ( OC_Exp_Item.GetTitle() ) catch ( undefined )
            format "[OC-EXP] current title: %\n" OC_Exp_SavedTitle

            try
            (
                OC_Exp_Item.SetTitle "* DYNAMIC TITLE TEST *"
                format "[OC-EXP] SetTitle OK — now OPEN the OmnibusCloud menu and look at the Render item\n"
                format "[OC-EXP] title readback: %\n" (try ( OC_Exp_Item.GetTitle() ) catch ( "<failed>" ))
            )
            catch
            (
                format "[OC-EXP] SetTitle FAILED: %\n" (getCurrentException())
            )
            true
        )
    )
)

fn OC_Exp_Revert =
(
    if OC_Exp_Item != undefined and OC_Exp_SavedTitle != undefined then
    (
        try
        (
            OC_Exp_Item.SetTitle OC_Exp_SavedTitle
            format "[OC-EXP] reverted title to: %\n" OC_Exp_SavedTitle
        )
        catch ( format "[OC-EXP] revert FAILED: %\n" (getCurrentException()) )
    )
    callbacks.removeScripts id:#OCExpMenu
    format "[OC-EXP] callback removed, experiment cleaned up\n"
)

callbacks.removeScripts id:#OCExpMenu
callbacks.addScript #cuiRegisterMenus OC_Exp_OnRegister id:#OCExpMenu
format "[OC-EXP] Registered. NOW: switch workspace away and back, then run OC_Exp_TryUpdate()\n"
