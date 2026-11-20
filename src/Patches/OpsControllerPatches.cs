using System.Collections.Generic;
using GeneralWaybilling.Zones;
using HarmonyLib;
using Model.Ops;

namespace GeneralWaybilling.Patches
{
    /// <summary>
    /// Patches OpsController lifecycle and resolution methods to seamlessly integrate general waybill destinations
    /// and player-created waybill zones.
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
                FuelingStandRegistry.RegisterForAreas(areas);
            }
        }

        [HarmonyPatch(typeof(OpsController), nameof(OpsController.PostRestoreProperties))]
        public static class PostRestorePropertiesPatch
        {
            [HarmonyPrefix]
            public static void Prefix(OpsController __instance)
            {
                GeneralWaybillRegistry.RegisterForOpsController(__instance);
                FuelingStandRegistry.RegisterForOpsController(__instance);
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

                if (FuelingStandRegistry.TryGetPosition(opsCarPositionIdentifier, out OpsCarPosition fuelingPosition))
                {
                    __result = fuelingPosition;
                    return false;
                }

                if (WaybillZoneRegistry.TryGetPosition(opsCarPositionIdentifier, out OpsCarPosition zonePosition))
                {
                    __result = zonePosition;
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
                    if (GeneralWaybillRegistry.TryGetComponent(identifier, out GeneralIndustryComponent component))
                    {
                        __result = component;
                        return;
                    }

                    if (FuelingStandRegistry.TryGetComponent(identifier, out FuelingStandLoader fuelingLoader))
                    {
                        __result = fuelingLoader;
                        return;
                    }

                    if (WaybillZoneRegistry.TryGetComponent(identifier, out ZoneIndustryComponent zoneComponent))
                    {
                        __result = zoneComponent;
                        return;
                    }
                }
            }
        }
    }
}
