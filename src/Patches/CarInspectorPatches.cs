using GeneralWaybilling.UI;
using HarmonyLib;
using UI.CarInspector;

namespace GeneralWaybilling.Patches
{
    /// <summary>
    /// Non-destructive CarInspector patch. Ensures our Destination Override UI extension
    /// is registered whenever the Operations panel is populated, without suppressing base game
    /// methods or conflicting with other mods.
    /// </summary>
    [HarmonyPatch(typeof(CarInspector))]
    public static class CarInspectorPatches
    {
        [HarmonyPatch("PopulateOperationsPanel")]
        [HarmonyPrefix]
        public static void PopulateOperationsPanelPrefix()
        {
            CarInspectorOverrideSection.Register();
        }
    }
}
