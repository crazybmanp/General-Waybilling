using System.Collections.Generic;
using HarmonyLib;
using Model.Ops;

namespace GeneralWaybilling.Patches
{
    /// <summary>
    /// Patches OpsController lifecycle and resolution methods to seamlessly integrate general waybill destinations.
    /// </summary>
    public static class OpsControllerPatches
    {
        [HarmonyPatch(typeof(OpsController), nameof(OpsController.RegisterLoadedData))]
        public static class RegisterLoadedDataPatch
        {
            [HarmonyPrefix]
            public static void Prefix(List<Area> areas)
            {
                GeneralWaybillRegistry.RegisterForAreas(areas);
            }
        }

        [HarmonyPatch(typeof(OpsController), nameof(OpsController.ResolveOpsCarPosition))]
        public static class ResolveOpsCarPositionPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(string opsCarPositionIdentifier, ref OpsCarPosition __result)
            {
                if (GeneralWaybillRegistry.TryGetPosition(opsCarPositionIdentifier, out OpsCarPosition position))
                {
                    __result = position;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(OpsController), "IndustryComponentForIdentifier", new[] { typeof(string) })]
        public static class IndustryComponentForIdentifierPatch
        {
            [HarmonyPostfix]
            public static void Postfix(string identifier, ref IndustryComponent __result)
            {
                if (__result == null && identifier != null)
                {
                    if (GeneralWaybillRegistry.TryGetComponent(identifier, out GeneralIndustryLoader loader))
                    {
                        __result = loader;
                    }
                }
            }
        }
    }
}
