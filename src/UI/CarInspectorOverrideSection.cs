using System;
using System.Collections.Generic;
using System.Linq;
using GeneralWaybilling.Overrides;
using GeneralWaybilling.Zones;
using Model;
using Model.Ops;
using UI;
using UI.Builder;
using UI.CarInspector;
using UI.Common;
using UI.Extensions;

namespace GeneralWaybilling.UI
{
    /// <summary>
    /// Renders the unified Destination Override management and delivery approval controls
    /// into the Car Inspector Operations panel for any piece of rolling stock.
    /// </summary>
    public static class CarInspectorOverrideSection
    {
        private const string ExtensionPointId = "carInspector.operations";
        private const string ExtensionId = "CRZBP.GeneralWaybilling.Override";

        public static void Register()
        {
            UIExtensionRegistry.RegisterExtension<Car>(
                ExtensionPointId,
                ExtensionId,
                null,
                priority: 10,
                delegate(UIPanelBuilder builder, Car car)
                {
                    BuildSection(builder, car);
                });
        }

        public static void BuildSection(UIPanelBuilder builder, Car car)
        {
            if (car == null)
            {
                return;
            }

            OpsController? ops = OpsController.Shared;
            if (ops == null)
            {
                return;
            }

            builder.RebuildOnPropertyChange(car.KeyValueObject, DestinationOverrideManager.KeyOpsOverrideDest);

            bool hasOverride = DestinationOverrideManager.TryGetOverride(car, ops, out (OpsCarPosition position, string? tag)? activeOverride);

            builder.AddSection("Destination Override", delegate(UIPanelBuilder section)
            {
                section.FieldLabelWidth = 100f;

                MenuFieldState state = hasOverride && activeOverride.HasValue
                    ? new MenuFieldState(activeOverride.Value.position.DisplayName)
                    : MenuFieldState.None;

                IReadOnlyList<MenuItem> menuItems = BuildAreaGroupedMenu(ops, car, delegate((OpsCarPosition position, string? tag)? picked)
                {
                    DestinationOverrideManager.SetOverride(car, picked);
                    section.Rebuild();
                });

                section.AddMenuField("Override", state, menuItems, "CarInspectorOverride").Tooltip(
                    "Destination Override",
                    "Temporarily route this car to a custom zone, yard track, or industry. Original waybill resumes upon approval.");

                if (hasOverride && activeOverride.HasValue)
                {
                    OpsCarPosition overridePos = activeOverride.Value.position;
                    bool isSpotted = DestinationOverrideManager.IsCarSpottedAtOverride(car, overridePos);

                    // Destination with in-world track highlighting on hover and camera jump on click
                    OpsCarPositionDisplayable posDisplayable = new OpsCarPositionDisplayable(overridePos);
                    section.AddField("Destination", section.AddLocationField(overridePos.DisplayName, posDisplayable, delegate
                    {
                        if (overridePos.Spans != null && overridePos.Spans.Length > 0 && CameraSelector.shared != null)
                        {
                            CameraSelector.shared.ZoomToPoint(overridePos.GetCenter());
                        }
                    }));

                    // Original route preview
                    Waybill? originalWaybill = car.GetWaybill(ops);
                    string originalText = originalWaybill.HasValue
                        ? $"{originalWaybill.Value.Destination.DisplayName} (Resumes on approval)"
                        : "None (Equipment / Unbilled)";
                    section.AddField("Original Route", originalText);

                    // Approval actions row
                    int coupledCount = DestinationOverrideManager.CountSpottedCoupledCars(car, overridePos);
                    string coupledLabel = coupledCount > 1
                        ? $"Approve ({coupledCount} Coupled)"
                        : "Approve (Coupled)";

                    section.AddField("Approval", section.ButtonStrip(delegate(UIPanelBuilder approvalStrip)
                    {
                        IConfigurableElement approveBtn = approvalStrip.AddButtonCompact("Approve", delegate
                        {
                            DestinationOverrideManager.CompleteOverride(car);
                            section.Rebuild();
                        }).Tooltip("Approve (This Car)", "Complete the override waybill for this car and resume its original route.");

                        if (!isSpotted)
                        {
                            approveBtn.Disable(true).Tooltip("Not Spotted", "Car must be spotted on destination track spans and stopped to approve.");
                        }

                        IConfigurableElement coupledBtn = approvalStrip.AddButtonCompact(coupledLabel, delegate
                        {
                            DestinationOverrideManager.CompleteOverrideCoupled(car);
                            section.Rebuild();
                        }).Tooltip("Approve (All Coupled)", "Complete override waybill for all spotted coupled cars sharing this destination.");

                        if (coupledCount == 0)
                        {
                            coupledBtn.Disable(true).Tooltip("None Spotted", "Coupled cars must be spotted on destination track spans and stopped.");
                        }
                    }));

                    // Secondary actions row
                    section.AddField("Actions", section.ButtonStrip(delegate(UIPanelBuilder actionStrip)
                    {
                        actionStrip.AddButtonCompact("Copy to Coupled", delegate
                        {
                            int copied = DestinationOverrideManager.CopyOverrideToCoupled(car, ops);
                            Toast.Present($"Copied routing override to {copied} coupled car(s).");
                            section.Rebuild();
                        }).Tooltip("Copy to Coupled", "Set this destination override on all cars coupled to this car.");

                        actionStrip.AddButtonCompact("Clear", delegate
                        {
                            DestinationOverrideManager.SetOverride(car, null);
                            section.Rebuild();
                        }).Tooltip("Clear Override", "Clear this override immediately and revert to the original route.");
                    }));
                }
                else
                {
                    // Unassigned actions row
                    section.AddField("Actions", section.ButtonStrip(delegate(UIPanelBuilder actionStrip)
                    {
                        actionStrip.AddButtonCompact("Copy to Coupled", delegate
                        {
                            int copied = DestinationOverrideManager.CopyOverrideToCoupled(car, ops);
                            Toast.Present($"Copied routing override to {copied} coupled car(s).");
                            section.Rebuild();
                        }).Tooltip("Copy to Coupled", "Copy destination override from coupled cars.");
                    }));
                }

                // Custom zones management row
                section.AddField("Zones", section.ButtonStrip(delegate(UIPanelBuilder zoneStrip)
                {
                    zoneStrip.AddButtonCompact("Manage Zones...", delegate
                    {
                        ZoneManagerWindow.Open();
                    }).Tooltip("Waybill Zones", "Open the Waybill Zones Manager to create or edit custom destination zones.");
                }));
            });
        }

        /// <summary>
        /// Builds clean hierarchical menu items grouped by Area (Town) submenus.
        /// Deduplicates industry components by name, displays custom zones with [Custom] prefix,
        /// and avoids unsupported font glyphs.
        /// </summary>
        public static IReadOnlyList<MenuItem> BuildAreaGroupedMenu(
            OpsController ops,
            Car car,
            Action<(OpsCarPosition position, string? tag)?> onPick)
        {
            List<MenuItem> items = new List<MenuItem>();

            // 1. Clear Override Option
            items.Add(MenuItem.Action("None (Clear Override)", delegate
            {
                onPick(null);
            }));

            // 2. Custom Player Zones with No Town (Wilderness / Mainline)
            IReadOnlyList<WaybillZone> allZones = WaybillZoneRegistry.GetAllZones();
            IReadOnlyList<WaybillZone> unzoned = allZones.Where(z => z.Area == null).ToList();
            if (unzoned.Count > 0)
            {
                List<MenuItem> zoneItems = new List<MenuItem>();
                foreach (WaybillZone zone in unzoned.OrderBy(z => z.DisplayName))
                {
                    zoneItems.Add(MenuItem.Action($"[Custom] {zone.DisplayName}", delegate
                    {
                        onPick((zone.Position, null));
                    }));
                }
                items.Add(MenuItem.Submenu("Custom Zones", zoneItems));
            }

            // 3. Submenus for each Area (Town) in the world
            if (ops.Areas != null)
            {
                foreach (Area area in ops.Areas.OrderBy(a => a.name))
                {
                    List<MenuItem> areaRows = new List<MenuItem>();

                    // Player zones associated with this specific area (placed at top with [Custom] prefix)
                    IReadOnlyList<WaybillZone> areaZones = WaybillZoneRegistry.GetZonesInArea(area);
                    foreach (WaybillZone zone in areaZones.OrderBy(z => z.DisplayName))
                    {
                        areaRows.Add(MenuItem.Action($"[Custom] {zone.DisplayName}", delegate
                        {
                            onPick((zone.Position, null));
                        }));
                    }

                    // Base game industry spurs, loaders, unloaders, interchanges in this area (DEDUPLICATED)
                    if (area.Industries != null)
                    {
                        foreach (Industry ind in area.Industries.OrderBy(i => i.name))
                        {
                            if (ind.Components == null)
                            {
                                continue;
                            }

                            IEnumerable<IndustryComponent> uniqueComponents = ind.Components
                                .Where(c => c.trackSpans != null && c.trackSpans.Length > 0 && !(c is ZoneIndustryComponent))
                                .GroupBy(PlaceNames.Of)
                                .Select(g => g.First())
                                .OrderBy(PlaceNames.Of);

                            foreach (IndustryComponent comp in uniqueComponents)
                            {
                                areaRows.Add(MenuItem.Action(PlaceNames.Of(comp), delegate
                                {
                                    onPick((comp, null));
                                }));
                            }
                        }
                    }

                    if (areaRows.Count > 0)
                    {
                        items.Add(MenuItem.Submenu(area.name, areaRows));
                    }
                }
            }

            return items;
        }
    }
}
