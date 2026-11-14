using System;
using System.Collections.Generic;
using System.Linq;
using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using Model.Ops;
using Track;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Manages the registry, persistence, and synchronization of player-created waybill zones.
    /// Integrates custom zones natively into base game Area.Industries for tag coloring and queries.
    /// </summary>
    public static class WaybillZoneRegistry
    {
        private const string PropertyObjectId = "gw_zones";
        private const string PropertyKeyZones = "zones";

        private static readonly Dictionary<string, WaybillZone> zonesById =
            new Dictionary<string, WaybillZone>(StringComparer.Ordinal);

        private static KeyValueStorage? storage;
        private static bool isInitialized;

        // Cached queries to prevent garbage collection allocation spikes in UI loops
        private static List<WaybillZone>? cachedAllZones;
        private static readonly Dictionary<string, List<WaybillZone>> cachedZonesByArea =
            new Dictionary<string, List<WaybillZone>>(StringComparer.OrdinalIgnoreCase);

        public static event Action? OnZonesChanged;

        public static void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            zonesById.Clear();
            InvalidateQueryCaches();

            try
            {
                storage = new KeyValueStorage();
                if (StateManager.Shared != null)
                {
                    StateManager.Shared.RegisterPropertyObject(PropertyObjectId, storage, AuthorizationRequirement.MinimumLevelTrainmaster);
                    storage.Observe(PropertyKeyZones, HandleStorageChanged);
                }

                isInitialized = true;
                ModEntryPoint.Log.Info("WaybillZoneRegistry initialized and registered with StateManager.");
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, "Failed to initialize WaybillZoneRegistry.");
            }
        }

        public static void Cleanup()
        {
            isInitialized = false;

            foreach (WaybillZone zone in zonesById.Values)
            {
                DetachFromArea(zone);
                zone.ClearSpans();
            }

            zonesById.Clear();
            InvalidateQueryCaches();
            storage = null;
        }

        private static void InvalidateQueryCaches()
        {
            cachedAllZones = null;
            cachedZonesByArea.Clear();
        }

        private static void AttachToArea(WaybillZone zone)
        {
            Area? area = zone.Area;
            if (area != null && !area.Industries.Contains(zone.Industry))
            {
                area.Industries.Add(zone.Industry);
            }
        }

        private static void DetachFromArea(WaybillZone zone)
        {
            Area? area = zone.Area;
            if (area != null)
            {
                area.Industries.Remove(zone.Industry);
            }
        }

        private static void HandleStorageChanged(Value value)
        {
            if (value.Type != KeyValue.Runtime.ValueType.Array)
            {
                return;
            }

            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            bool hasInvalidZones = false;

            foreach (Value item in value.ArrayValue)
            {
                try
                {
                    WaybillZone? deserialized = WaybillZone.FromKeyValue(item);
                    if (deserialized == null)
                    {
                        continue;
                    }

                    if (zonesById.TryGetValue(deserialized.Id, out WaybillZone existing))
                    {
                        if (existing.AreaId != deserialized.AreaId)
                        {
                            DetachFromArea(existing);
                            existing.AreaId = deserialized.AreaId;
                        }

                        existing.DisplayName = deserialized.DisplayName;
                        if (existing.RuntimeSpans.Count == 0 || !existing.SerializedSpans.SequenceEqual(deserialized.SerializedSpans))
                        {
                            RecreateRuntimeSpans(existing, deserialized.SerializedSpans);
                        }
                        seenIds.Add(existing.Id);
                        AttachToArea(existing);
                    }
                    else
                    {
                        RecreateRuntimeSpans(deserialized, deserialized.SerializedSpans);
                        if (deserialized.RuntimeSpans.Count == 0 || deserialized.TotalLength < 0.1f)
                        {
                            ModEntryPoint.Log.Info($"Purging invalid 0-span zone '{deserialized.DisplayName}' ({deserialized.Id}) from save.");
                            hasInvalidZones = true;
                            continue;
                        }

                        seenIds.Add(deserialized.Id);
                        AttachToArea(deserialized);
                        zonesById[deserialized.Id] = deserialized;
                    }
                }
                catch (Exception ex)
                {
                    ModEntryPoint.Log.Warning($"Error deserializing zone item: {ex.Message}");
                }
            }

            // Remove any zones deleted remotely
            List<string> toRemove = zonesById.Keys.Where(k => !seenIds.Contains(k)).ToList();
            foreach (string removeId in toRemove)
            {
                DetachFromArea(zonesById[removeId]);
                zonesById[removeId].ClearSpans();
                zonesById.Remove(removeId);
            }

            InvalidateQueryCaches();

            if (hasInvalidZones)
            {
                SaveToStorage();
            }

            OnZonesChanged?.Invoke();
        }

        private static void RecreateRuntimeSpans(WaybillZone zone, List<string> serializedSpans)
        {
            List<string> spansToRestore = serializedSpans.ToList();
            zone.ClearSpans();
            int spanIndex = 0;
            foreach (string serialized in spansToRestore)
            {
                string[] parts = serialized.Split('#');
                if (parts.Length == 2)
                {
                    string spanId = $"{zone.Id}_{spanIndex++}";
                    TrackSpan? span = TrackSpanDetector.RestoreRuntimeSpan(spanId, parts[0], parts[1]);
                    if (span != null)
                    {
                        zone.RuntimeSpans.Add(span);
                        zone.SerializedSpans.Add(serialized);
                    }
                }
                else
                {
                    // Backward compatibility: 6 pipe-separated parts (lower_id|a|dist|upper_id|b|dist)
                    string[] pipeParts = serialized.Split('|');
                    if (pipeParts.Length == 6)
                    {
                        string locLower = $"{pipeParts[0]}|{pipeParts[1]}|{pipeParts[2]}";
                        string locUpper = $"{pipeParts[3]}|{pipeParts[4]}|{pipeParts[5]}";
                        string spanId = $"{zone.Id}_{spanIndex++}";
                        TrackSpan? span = TrackSpanDetector.RestoreRuntimeSpan(spanId, locLower, locUpper);
                        if (span != null)
                        {
                            zone.RuntimeSpans.Add(span);
                            zone.SerializedSpans.Add($"{locLower}#{locUpper}");
                        }
                    }
                }
            }
            zone.InvalidateCache();
        }

        public static void RegisterZone(WaybillZone zone)
        {
            if (zone is null)
            {
                throw new ArgumentNullException(nameof(zone));
            }

            if (zone.RuntimeSpans.Count == 0 || zone.TotalLength < 0.1f)
            {
                ModEntryPoint.Log.Warning($"Rejecting registration of empty zone '{zone.DisplayName}' ({zone.Id}).");
                return;
            }

            AttachToArea(zone);
            zonesById[zone.Id] = zone;
            InvalidateQueryCaches();
            SaveToStorage();
            OnZonesChanged?.Invoke();
            ModEntryPoint.Log.Info($"Registered waybill zone: '{zone.DisplayName}' ({zone.Id}) with {zone.RuntimeSpans.Count} span(s) in {zone.Area?.name ?? "Custom"}.");
        }

        public static bool RemoveZone(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId))
            {
                return false;
            }

            if (zonesById.TryGetValue(zoneId, out WaybillZone zone))
            {
                DetachFromArea(zone);
                zone.ClearSpans();
                zonesById.Remove(zoneId);
                InvalidateQueryCaches();
                SaveToStorage();
                OnZonesChanged?.Invoke();
                ModEntryPoint.Log.Info($"Removed waybill zone: '{zone.DisplayName}' ({zoneId}).");
                return true;
            }

            return false;
        }

        public static bool TryGetZone(string zoneId, out WaybillZone zone)
        {
            if (string.IsNullOrEmpty(zoneId))
            {
                zone = null!;
                return false;
            }

            return zonesById.TryGetValue(zoneId, out zone);
        }

        public static bool TryGetPosition(string zoneId, out OpsCarPosition position)
        {
            if (TryGetZone(zoneId, out WaybillZone zone))
            {
                position = zone.Position;
                return true;
            }

            int dotIndex = zoneId != null ? zoneId.IndexOf('.') : -1;
            if (dotIndex > 0 && TryGetZone(zoneId!.Substring(0, dotIndex), out zone))
            {
                position = zone.Position;
                return true;
            }

            position = default(OpsCarPosition);
            return false;
        }

        public static bool TryGetComponent(string zoneId, out ZoneIndustryComponent component)
        {
            if (TryGetZone(zoneId, out WaybillZone zone))
            {
                component = zone.Component;
                return true;
            }

            int dotIndex = zoneId != null ? zoneId.IndexOf('.') : -1;
            if (dotIndex > 0 && TryGetZone(zoneId!.Substring(0, dotIndex), out zone))
            {
                component = zone.Component;
                return true;
            }

            component = null!;
            return false;
        }

        public static IReadOnlyList<WaybillZone> GetAllZones()
        {
            if (cachedAllZones == null)
            {
                cachedAllZones = zonesById.Values
                    .Where(z => z.RuntimeSpans.Count > 0 && z.TotalLength > 0.1f)
                    .OrderBy(z => z.DisplayName)
                    .ToList();
            }

            return cachedAllZones;
        }

        public static IReadOnlyList<WaybillZone> GetZonesInArea(Area area)
        {
            if (area is null)
            {
                return Array.Empty<WaybillZone>();
            }

            if (!cachedZonesByArea.TryGetValue(area.identifier, out List<WaybillZone> zones))
            {
                zones = zonesById.Values
                    .Where(z => z.RuntimeSpans.Count > 0 && z.TotalLength > 0.1f && string.Equals(z.AreaId, area.identifier, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(z => z.DisplayName)
                    .ToList();
                cachedZonesByArea[area.identifier] = zones;
            }

            return zones;
        }

        private static void SaveToStorage()
        {
            if (storage == null)
            {
                return;
            }

            List<Value> array = new List<Value>();
            foreach (WaybillZone zone in zonesById.Values)
            {
                if (zone.RuntimeSpans.Count > 0 && zone.TotalLength > 0.1f)
                {
                    array.Add(zone.ToKeyValue());
                }
            }

            storage[PropertyKeyZones] = Value.Array(array);
        }
    }
}
