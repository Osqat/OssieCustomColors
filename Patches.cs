using HarmonyLib;

namespace OssieCustomColors
{
    [HarmonyPatch(typeof(InventoryBook), "TurnOffScreens")]
    static class Patch_InventoryBook_TurnOffScreens
    {
        static void Postfix(bool ShowBook)
        {
            var ui = ColorPickerUI.Instance;
            if (ui == null) return;
            if (ShowBook && GameSettings.GetInstance().GameMode == GameState.GameMode.FREEPLAY)
                ui.OnBookShown();
            else if (!ShowBook)
                ui.OnBookHidden();
        }
    }
}
