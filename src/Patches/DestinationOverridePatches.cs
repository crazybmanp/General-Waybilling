using System;
using System.Linq;
using HarmonyLib;
using Model;
using Model.Ops;
using GeneralWaybilling.Overrides;
using UI.SwitchList;
using UI.Tags;
using UnityEngine;

namespace GeneralWaybilling.Patches
{
    /// <summary>
    /// Harmony patches that integrate routing overrides into car destination queries, car tags, and switch lists.
    /// Prefixes override destinations with 'Override: ' across base game and third-party tag mods.
    /// </summary>
    public static class DestinationOverridePatches
    {
        [HarmonyPatch(typeof(OpsControllerExtensions), nameof(OpsControllerExtensions.TryGetDestinationInfo))]
        public static class TryGetDestinationInfoPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                OpsController opsController,
                Car car,
                ref string destinationName,
                ref bool isAtDestination,
                ref Vector3 destinationPosition,
                ref OpsCarPosition destination,
                ref bool __result)
            {
                if (car == null || opsController == null)
                {
                    return true;
                }

                // If car has a repair override active, allow repair track to take precedence
                if (car.HasOverrideDestination(OverrideDestination.Repair))
                {
                    return true;
                }

                // Check for general routing override
                if (DestinationOverrideManager.TryGetOverride(car, opsController, out (OpsCarPosition position, string? tag)? overrideResult) && overrideResult.HasValue)
                {
                    try
                    {
                        OpsCarPosition rawPos = overrideResult.Value.position;
                        string? customTag = overrideResult.Value.tag;
                        string prefix = string.IsNullOrEmpty(customTag) ? "Override: " : $"Override [{customTag}]: ";

                        string baseDestName = opsController.NameForPosition(rawPos);
                        destinationName = prefix + baseDestName;
                        destination = new OpsCarPosition(prefix + rawPos.DisplayName, rawPos.Identifier, rawPos.Spans);
                        destinationPosition = rawPos.GetCenter();
                        isAtDestination = opsController.CarsAtPosition(rawPos).Contains(car);
                        __result = true;
                        return false;
                    }
                    catch (Exception ex)
                    {
                        ModEntryPoint.Log.Warning($"Error resolving override destination info for {car}: {ex.Message}");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(TagController), "UpdateTag")]
        public static class TagControllerUpdateTagPatch
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(Car car, TagCallout tagCallout, OpsController opsController)
            {
                if (car == null || tagCallout == null || tagCallout.callout == null || opsController == null)
                {
                    return;
                }

                if (DestinationOverrideManager.TryGetOverride(car, opsController, out (OpsCarPosition position, string? tag)? overrideResult) && overrideResult.HasValue)
                {
                    string currentText = tagCallout.callout.Text;
                    string? customTag = overrideResult.Value.tag;
                    string prefix = string.IsNullOrEmpty(customTag) ? "Override: " : $"Override [{customTag}]: ";

                    if (string.IsNullOrEmpty(currentText))
                    {
                        tagCallout.callout.Text = prefix + overrideResult.Value.position.DisplayName;
                        tagCallout.callout.Layout();
                    }
                    else if (!currentText.StartsWith("Override:", StringComparison.OrdinalIgnoreCase) &&
                             !currentText.StartsWith("Override [", StringComparison.OrdinalIgnoreCase) &&
                             !currentText.Contains("Override:"))
                    {
                        tagCallout.callout.Text = prefix + currentText;
                        tagCallout.callout.Layout();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(OpsCarList), "MakeEntry")]
        public static class OpsCarListMakeEntryPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(string carId, TrainController trainController, ref OpsCarList.Entry? __result)
            {
                if (trainController == null || string.IsNullOrEmpty(carId))
                {
                    return true;
                }

                Car car = trainController.CarForId(carId);
                if (car == null)
                {
                    return true;
                }

                OpsController shared = OpsController.Shared;
                if (shared == null)
                {
                    return true;
                }

                if (DestinationOverrideManager.TryGetOverride(car, shared, out (OpsCarPosition position, string? tag)? overrideResult) && overrideResult.HasValue)
                {
                    try
                    {
                        OpsCarPosition destination = overrideResult.Value.position;
                        Area? area = shared.AreaForCarPosition(destination);
                        string? customTag = overrideResult.Value.tag;
                        string prefix = string.IsNullOrEmpty(customTag) ? "Override: " : $"Override [{customTag}]: ";
                        string displayName = prefix + destination.DisplayName;
                        Vector3 center = destination.GetCenter();
                        OpsCarList.Entry.Location destLoc = new OpsCarList.Entry.Location(
                            displayName,
                            area?.name ?? "Special Routing",
                            center,
                            (int)center.x / 10,
                            destination.Spans);

                        OpsCarList.Entry.Location currentLoc;
                        if (car.IsInBardo)
                        {
                            currentLoc = new OpsCarList.Entry.Location("Off railroad", null, Vector3.zero, 0, Array.Empty<string>());
                        }
                        else
                        {
                            OpsCarPosition? currentPos = shared.PositionForCar(car);
                            if (!currentPos.HasValue)
                            {
                                Area? closest = shared.ClosestArea(car);
                                currentLoc = (closest == null)
                                    ? new OpsCarList.Entry.Location("???", null, Vector3.zero, 0, Array.Empty<string>())
                                    : new OpsCarList.Entry.Location(closest.name, null, Vector3.zero, 0, Array.Empty<string>());
                            }
                            else
                            {
                                OpsCarPosition val = currentPos.Value;
                                Area? curArea = shared.AreaForCarPosition(val);
                                Vector3 curCenter = val.GetCenter();
                                currentLoc = new OpsCarList.Entry.Location(
                                    val.DisplayName,
                                    curArea?.name ?? "???",
                                    curCenter,
                                    (int)curCenter.x / 10,
                                    val.Spans);
                            }
                        }

                        bool isSpotted = DestinationOverrideManager.IsCarSpottedAtOverride(car, destination);
                        __result = new OpsCarList.Entry(carId, car.SortName, currentLoc, destLoc, isSpotted);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        ModEntryPoint.Log.Warning($"OpsCarList.MakeEntry override fallback: {ex.Message}");
                        return true;
                    }
                }

                return true;
            }
        }
    }
}
