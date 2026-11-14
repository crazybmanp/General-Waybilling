using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using Model.Ops;
using Model.Ops.Definition;
using RollingStock;
using Track;
using UnityEngine;

namespace GeneralWaybilling
{
    /// <summary>
    /// Discovers fueling stands (CarLoadTargetLoader) associated with repair/service stations
    /// and registers them as outbound loading destinations with tight +/-3m spotting zones.
    /// </summary>
    public static class FuelingStandRegistry
    {
        public const float SpottingHalfLengthMeters = 3f;
        public const float TrackSearchRadiusMeters = 15f;

        private static readonly Dictionary<string, FuelingStandLoader> componentsByIdentifier =
            new Dictionary<string, FuelingStandLoader>(StringComparer.Ordinal);

        private static readonly FieldInfo? sourceIndustryIdField =
            typeof(CarLoadTargetLoader).GetField("sourceIndustryId", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool warnedMissingSourceIndustryField;
        private static GameObject? spanContainerObject;

        /// <summary>
        /// Discovers and registers fueling stand loaders for all repair stations in the given areas.
        /// </summary>
        public static void RegisterForAreas(IEnumerable<Area> areas)
        {
            UnregisterAll();

            if (areas is null)
            {
                return;
            }

            IEnumerable<Industry> industries = areas
                .Where(area => area?.Industries != null)
                .SelectMany(area => area.Industries);

            DiscoverAndRegister(industries);
        }

        /// <summary>
        /// Discovers any fueling stands not yet registered and synchronizes OpsController's lookup collections.
        /// </summary>
        public static void RegisterForOpsController(OpsController opsController)
        {
            if (opsController is null)
            {
                return;
            }

            if (opsController.AllIndustries != null)
            {
                DiscoverAndRegister(opsController.AllIndustries);
            }

            OpsControllerSync.SyncComponents(opsController, componentsByIdentifier.Values, includeAllIndustryComponents: true);
        }

        /// <summary>
        /// Attempts to look up a registered fueling stand loader by its full component identifier.
        /// </summary>
        public static bool TryGetComponent(string identifier, out FuelingStandLoader component)
        {
            if (!string.IsNullOrEmpty(identifier))
            {
                return componentsByIdentifier.TryGetValue(identifier, out component);
            }

            component = null!;
            return false;
        }

        /// <summary>
        /// Attempts to resolve an <see cref="OpsCarPosition"/> for a registered fueling stand identifier.
        /// </summary>
        public static bool TryGetPosition(string identifier, out OpsCarPosition position)
        {
            if (TryGetComponent(identifier, out FuelingStandLoader loader))
            {
                position = loader;
                return true;
            }

            position = default(OpsCarPosition);
            return false;
        }

        /// <summary>
        /// Clears all registered fueling stand loaders and destroys any runtime spotting spans.
        /// </summary>
        public static void UnregisterAll()
        {
            componentsByIdentifier.Clear();

            if (spanContainerObject != null)
            {
                UnityEngine.Object.Destroy(spanContainerObject);
                spanContainerObject = null;
            }
        }

        /// <summary>
        /// Scans the scene for CarLoadTargetLoader components linked to repair stations and registers each group.
        /// </summary>
        private static void DiscoverAndRegister(IEnumerable<Industry> industries)
        {
            if (Graph.Shared == null)
            {
                return;
            }

            Dictionary<string, Industry> repairStationsById = industries
                .Where(i => i != null && !string.IsNullOrEmpty(i.identifier) && i.Components != null && i.Components.OfType<RepairTrack>().Any())
                .GroupBy(i => i.identifier, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            if (repairStationsById.Count == 0)
            {
                return;
            }

            CarLoadTargetLoader[] sceneLoaders = UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            if (sceneLoaders == null || sceneLoaders.Length == 0)
            {
                return;
            }

            IEnumerable<IGrouping<(Industry Industry, Load Load), (CarLoadTargetLoader Loader, Industry? Industry)>> groupedStands = sceneLoaders
                .Select(loader => (Loader: loader, Industry: ResolveRepairStation(loader, repairStationsById)))
                .Where(entry => entry.Industry != null)
                .GroupBy(entry => (Industry: entry.Industry!, Load: entry.Loader.load));

            foreach (IGrouping<(Industry Industry, Load Load), (CarLoadTargetLoader Loader, Industry? Industry)> group in groupedStands)
            {
                RegisterStandGroup(group.Key.Industry, group.Key.Load, group.Select(g => g.Loader));
            }
        }

        /// <summary>
        /// Resolves the repair-station <see cref="Industry"/> referenced by a loader's private sourceIndustryId, or null if unrelated.
        /// </summary>
        private static Industry? ResolveRepairStation(CarLoadTargetLoader loader, Dictionary<string, Industry> repairStationsById)
        {
            if (sourceIndustryIdField == null)
            {
                if (!warnedMissingSourceIndustryField)
                {
                    warnedMissingSourceIndustryField = true;
                    ModEntryPoint.Log.Warning("CarLoadTargetLoader.sourceIndustryId field was not found; fueling stand discovery is disabled.");
                }
                return null;
            }

            if (loader == null || loader.load == null)
            {
                return null;
            }

            string? sourceIndustryId = sourceIndustryIdField.GetValue(loader) as string;
            if (string.IsNullOrEmpty(sourceIndustryId))
            {
                return null;
            }

            return repairStationsById.TryGetValue(sourceIndustryId!, out Industry industry) ? industry : null;
        }

        /// <summary>
        /// Deterministically sorts and registers all fueling stands sharing the same repair station and load.
        /// </summary>
        private static void RegisterStandGroup(Industry industry, Load load, IEnumerable<CarLoadTargetLoader> loaders)
        {
            List<CarLoadTargetLoader> sorted = loaders
                .OrderBy(l => l.transform.position.x)
                .ThenBy(l => l.transform.position.z)
                .ThenBy(l => l.transform.position.y)
                .ToList();

            bool multiple = sorted.Count > 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                RegisterStand(industry, load, sorted[i], i, multiple);
            }
        }

        /// <summary>
        /// Registers a single fueling stand loader and its spotting span if not already registered.
        /// </summary>
        private static void RegisterStand(Industry industry, Load load, CarLoadTargetLoader stand, int index, bool multiple)
        {
            string loadKey = load.id.Replace('/', '_');
            string subId = multiple ? $"fuel_load_{loadKey}_{index}" : $"fuel_load_{loadKey}";
            string identifier = industry.identifier + "." + subId;

            if (componentsByIdentifier.ContainsKey(identifier))
            {
                return;
            }

            FuelingStandLoader? existing = industry.Components
                .OfType<FuelingStandLoader>()
                .FirstOrDefault(c => c.Identifier == identifier);

            if (existing != null)
            {
                componentsByIdentifier[identifier] = existing;
                return;
            }

            TrackSpan? spottingSpan = CreateSpottingSpan(identifier, stand.transform.position);
            if (spottingSpan == null)
            {
                ModEntryPoint.Log.Warning($"Could not locate track near fueling stand '{stand.name}' for {industry.DisplayName} ({load.description}); skipping.");
                return;
            }

            string displayName = BuildDisplayName(load, index, multiple);

            FuelingStandLoader component = new FuelingStandLoader(industry, load, stand, subId, displayName, spottingSpan);
            industry.Components.Add(component);
            componentsByIdentifier[identifier] = component;

            ModEntryPoint.Log.Info($"Registered fueling stand loading destination: '{displayName}' ({identifier}), rate={stand.outputRate}/s.");
        }

        /// <summary>
        /// Builds a short, readable component name (e.g. "Coal (Load)") so both
        /// <c>LocationsPanelBuilder</c> (<c>ShortName</c>) and <c>PlaceNames.Of</c> render cleanly.
        /// </summary>
        private static string BuildDisplayName(Load load, int index, bool multiple)
        {
            return multiple
                ? $"{load.description} #{index + 1} (Load)"
                : $"{load.description} (Load)";
        }

        /// <summary>
        /// Creates and registers a +/-3m (6m total) runtime <see cref="TrackSpan"/> centered on the fueling spout's track location.
        /// </summary>
        private static TrackSpan? CreateSpottingSpan(string spanId, Vector3 worldPosition)
        {
            Graph graph = Graph.Shared;
            if (!TryFindNearestTrackLocation(graph, worldPosition, out Location centerLocation))
            {
                return null;
            }

            Location lower = graph.LocationByMoving(centerLocation, SpottingHalfLengthMeters, checkSwitchAgainstMovement: false, Graph.EndOfTrackHandling.Clamp);
            Location upper = graph.LocationByMoving(centerLocation.Flipped(), SpottingHalfLengthMeters, checkSwitchAgainstMovement: false, Graph.EndOfTrackHandling.Clamp);

            if (spanContainerObject == null)
            {
                spanContainerObject = new GameObject("GW_FuelingSpans");
                spanContainerObject.hideFlags = HideFlags.DontSave;
                UnityEngine.Object.DontDestroyOnLoad(spanContainerObject);
            }

            GameObject spanGo = new GameObject("Span-" + spanId);
            spanGo.hideFlags = HideFlags.DontSave;
            spanGo.transform.SetParent(spanContainerObject.transform, worldPositionStays: false);

            TrackSpan span = spanGo.AddComponent<TrackSpan>();
            span.id = spanId;
            span.lower = lower;
            span.upper = upper;
            span.OrientEndpointsInward();
            graph.AddSpan(span);

            return span;
        }

        /// <summary>
        /// Finds the closest track <see cref="Location"/> within range of <paramref name="worldPosition"/>, falling back to disabled progression segments.
        /// </summary>
        private static bool TryFindNearestTrackLocation(Graph graph, Vector3 worldPosition, out Location output)
        {
            if (graph.TryGetLocationFromWorldPoint(worldPosition, TrackSearchRadiusMeters, out output))
            {
                return true;
            }

            // Fallback to all segments (including not-yet-enabled progression track groups)
            Vector3 gamePosition = worldPosition.WorldToGame();
            Location? best = null;
            float bestDist = float.MaxValue;

            foreach (TrackSegment segment in graph.AllSegments)
            {
                if (graph.TryGetLocationFromPoint(segment, gamePosition, TrackSearchRadiusMeters, out Location candidate))
                {
                    float dist = Vector3.Distance(graph.GetPosition(candidate), gamePosition);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = candidate;
                    }
                }
            }

            output = best.GetValueOrDefault();
            return best.HasValue;
        }
    }
}
