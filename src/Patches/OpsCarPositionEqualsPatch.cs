using HarmonyLib;
using Model.Ops;

namespace GeneralWaybilling.Patches
{
    /// <summary>
    /// Harmony postfix on OpsCarPosition.Equals that recognizes general destinations as equivalent
    /// to their member landing spurs without modifying or detouring EnumerateCars or using IL transpilers.
    /// </summary>
    [HarmonyPatch(typeof(OpsCarPosition), nameof(OpsCarPosition.Equals), typeof(OpsCarPosition))]
    public static class OpsCarPositionEqualsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(OpsCarPosition __instance, OpsCarPosition other, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (GeneralWaybillRegistry.ArePositionsEquivalent(__instance, other))
            {
                __result = true;
            }
        }
    }
}
