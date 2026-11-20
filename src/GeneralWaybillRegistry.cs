using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using Model.Ops.AutoDestinations;
using Model.Ops.Definition;

namespace GeneralWaybilling
{
    /// <summary>
    /// Registry managing general waybill destinations for multi-landing industries and multi-industry areas,
    /// tracking associations between composite destinations and individual member spurs.
    /// </summary>
    public static class GeneralWaybillRegistry
    {
        private const string AreaIndustryPrefix = "gw_area_";
        private const string ZoneIndustryPrefix = "gw_zone_";

        private static readonly Dictionary<string, GeneralIndustryComponent> componentsByIdentifier =
            new Dictionary<string, GeneralIndustryComponent>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Industry> areaIndustriesByAreaId =
            new Dictionary<string, Industry>(StringComparer.Ordinal);

        /// <summary>
        /// Registers industry-level and area-level general destinations across all given areas.
        /// </summary>
        public static void RegisterForAreas(IEnumerable<Area> areas)
        {
            if (areas is null)
            {
                return;
            }

            foreach (Area area in areas)
            {
                RegisterForArea(area);
            }
        }

        /// <summary>
        /// Registers industry-level general destinations for each industry in the area,
        /// and area-level general destinations when 2 or more distinct industries share a load.
        /// </summary>
        public static void RegisterForArea(Area area)
        {
            if (area is null || area.Industries is null)
            {
                return;
            }

            List<Industry> realIndustries = area.Industries
                .Where(industry => industry != null && !IsSyntheticIndustry(industry))
                .ToList();

            foreach (Industry industry in realIndustries)
            {
                RegisterForIndustry(industry);
            }

            RegisterAreaGroups(
                area,
                realIndustries.SelectMany(industry => industry.Components?.OfType<IndustryLoader>() ?? Enumerable.Empty<IndustryLoader>()),
                loader => loader.load,
                AutoDestinationType.Empty);

            RegisterAreaGroups(
                area,
                realIndustries.SelectMany(industry => industry.Components?.OfType<IndustryUnloader>() ?? Enumerable.Empty<IndustryUnloader>())
                    .Where(unloader => !unloader.orderLoads),
                unloader => unloader.load,
                AutoDestinationType.Load);
        }

        /// <summary>
        /// Registers general destinations for an industry if it contains multiple loaders or unloaders for the same load.
        /// </summary>
        public static void RegisterForIndustry(Industry industry)
        {
            if (industry is null || industry.Components is null || IsSyntheticIndustry(industry))
            {
                return;
            }

            RegisterIndustryGroups(
                industry,
                industry.Components.OfType<IndustryLoader>(),
                loader => loader.load,
                AutoDestinationType.Empty);

            RegisterIndustryGroups(
                industry,
                industry.Components.OfType<IndustryUnloader>().Where(unloader => !unloader.orderLoads),
                unloader => unloader.load,
                AutoDestinationType.Load);
        }

        private static void RegisterIndustryGroups<TComponent>(
            Industry industry,
            IEnumerable<TComponent> components,
            Func<TComponent, Load?> loadSelector,
            AutoDestinationType destinationType)
            where TComponent : IndustryComponent
        {
            IEnumerable<IGrouping<Load, TComponent>> groups = components
                .Where(component => loadSelector(component) != null)
                .GroupBy(component => loadSelector(component)!);

            foreach (IGrouping<Load, TComponent> group in groups)
            {
                List<IndustryComponent> memberList = group.Cast<IndustryComponent>().ToList();
                if (memberList.Count < 2)
                {
                    continue;
                }

                RegisterCompositeComponent(
                    industry,
                    group.Key,
                    memberList,
                    destinationType,
                    isAreaLevel: false,
                    displayNamePrefix: null);
            }
        }

        private static void RegisterAreaGroups<TComponent>(
            Area area,
            IEnumerable<TComponent> components,
            Func<TComponent, Load?> loadSelector,
            AutoDestinationType destinationType)
            where TComponent : IndustryComponent
        {
            IEnumerable<IGrouping<Load, TComponent>> groups = components
                .Where(component => loadSelector(component) != null)
                .GroupBy(component => loadSelector(component)!);

            foreach (IGrouping<Load, TComponent> group in groups)
            {
                List<IndustryComponent> memberList = group.Cast<IndustryComponent>().ToList();
                int distinctIndustries = memberList
                    .Select(member => member.Industry)
                    .Where(industry => industry != null)
                    .Distinct()
                    .Count();

                if (distinctIndustries < 2)
                {
                    continue;
                }

                Industry areaIndustry = GetOrCreateAreaIndustry(area);
                RegisterCompositeComponent(
                    areaIndustry,
                    group.Key,
                    memberList,
                    destinationType,
                    isAreaLevel: true,
                    displayNamePrefix: area.name);
            }
        }

        private static void RegisterCompositeComponent(
            Industry hostIndustry,
            Load load,
            List<IndustryComponent> memberList,
            AutoDestinationType destinationType,
            bool isAreaLevel,
            string? displayNamePrefix)
        {
            string scopePrefix = isAreaLevel ? "general_area_" : "general_";
            string modePrefix = destinationType == AutoDestinationType.Load ? "unload_" : string.Empty;
            string generalSubId = scopePrefix + modePrefix + load.id.Replace('/', '_');
            string generalIdentifier = hostIndustry.identifier + "." + generalSubId;

            GeneralIndustryComponent? existing = hostIndustry.Components
                .OfType<GeneralIndustryComponent>()
                .FirstOrDefault(component => component.Identifier == generalIdentifier);

            if (existing is null)
            {
                GeneralIndustryComponent composite = new GeneralIndustryComponent(
                    hostIndustry,
                    load,
                    memberList,
                    destinationType,
                    isAreaLevel,
                    displayNamePrefix);
                hostIndustry.Components.Add(composite);
                existing = composite;
                ModEntryPoint.Log.Info($"Registered general waybill destination: '{existing.DisplayName}' ({generalIdentifier}) with {memberList.Count} member spurs.");
            }

            componentsByIdentifier[generalIdentifier] = existing;
        }

        private static Industry GetOrCreateAreaIndustry(Area area)
        {
            if (areaIndustriesByAreaId.TryGetValue(area.identifier, out Industry existing))
            {
                return existing;
            }

            Industry areaIndustry = new Industry
            {
                identifier = AreaIndustryPrefix + area.identifier,
                name = area.name,
                Area = area,
                usesContract = false,
                IsProgressionEnabled = true,
                IsTrackReachable = true
            };
            areaIndustry.Initialize();

            if (!area.Industries.Contains(areaIndustry))
            {
                area.Industries.Add(areaIndustry);
            }

            areaIndustriesByAreaId[area.identifier] = areaIndustry;
            return areaIndustry;
        }

        private static bool IsSyntheticIndustry(Industry industry)
        {
            if (string.IsNullOrEmpty(industry.identifier))
            {
                return false;
            }

            return industry.identifier.StartsWith(AreaIndustryPrefix, StringComparison.Ordinal) ||
                   industry.identifier.StartsWith(ZoneIndustryPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Synchronizes registered general destinations with OpsController's internal lookup dictionary.
        /// </summary>
        public static void RegisterForOpsController(OpsController opsController)
        {
            if (opsController is null)
            {
                return;
            }

            if (opsController.Areas != null)
            {
                RegisterForAreas(opsController.Areas);
            }
            else if (opsController.AllIndustries != null)
            {
                foreach (Industry industry in opsController.AllIndustries)
                {
                    RegisterForIndustry(industry);
                }
            }

            OpsControllerSync.SyncComponents(opsController, componentsByIdentifier.Values, includeAllIndustryComponents: false);
        }

        /// <summary>
        /// Attempts to get a registered GeneralIndustryComponent by its identifier.
        /// </summary>
        public static bool TryGetComponent(string identifier, out GeneralIndustryComponent component)
        {
            if (identifier != null)
            {
                return componentsByIdentifier.TryGetValue(identifier, out component);
            }

            component = null!;
            return false;
        }

        /// <summary>
        /// Attempts to get an OpsCarPosition for a registered general destination.
        /// </summary>
        public static bool TryGetPosition(string identifier, out OpsCarPosition position)
        {
            if (TryGetComponent(identifier, out GeneralIndustryComponent component))
            {
                position = component;
                return true;
            }

            position = default(OpsCarPosition);
            return false;
        }

        /// <summary>
        /// Determines whether a car destination matches or is accepted by an industry component.
        /// Returns true if destination is a general destination of which the component is a member spur.
        /// </summary>
        public static bool IsDestinationAccepted(IndustryComponent component, OpsCarPosition destination)
        {
            if (component is null || string.IsNullOrEmpty(destination.Identifier))
            {
                return false;
            }

            if (TryGetComponent(destination.Identifier, out GeneralIndustryComponent generalComponent))
            {
                return generalComponent.ServesLanding(component.Identifier);
            }

            return false;
        }

        /// <summary>
        /// Determines whether two OpsCarPosition instances are equivalent under general waybilling rules
        /// (e.g., one is a general destination and the other is one of its member spurs).
        /// </summary>
        public static bool ArePositionsEquivalent(OpsCarPosition a, OpsCarPosition b)
        {
            if (string.IsNullOrEmpty(a.Identifier) || string.IsNullOrEmpty(b.Identifier))
            {
                return false;
            }

            if (string.Equals(a.Identifier, b.Identifier, StringComparison.Ordinal))
            {
                return true;
            }

            if (TryGetComponent(a.Identifier, out GeneralIndustryComponent generalA) && generalA.ServesLanding(b.Identifier))
            {
                return true;
            }

            if (TryGetComponent(b.Identifier, out GeneralIndustryComponent generalB) && generalB.ServesLanding(a.Identifier))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears all registrations and removes custom components and synthetic area industries upon mod unload.
        /// </summary>
        public static void UnregisterAll()
        {
            foreach (KeyValuePair<string, GeneralIndustryComponent> pair in componentsByIdentifier)
            {
                Industry industry = pair.Value.Industry;
                industry?.Components?.Remove(pair.Value);
            }

            componentsByIdentifier.Clear();

            foreach (KeyValuePair<string, Industry> pair in areaIndustriesByAreaId)
            {
                Industry areaIndustry = pair.Value;
                areaIndustry.Area?.Industries?.Remove(areaIndustry);
                areaIndustry.Dispose();
            }

            areaIndustriesByAreaId.Clear();
            ModEntryPoint.Log.Info("Unregistered all general waybill destinations.");
        }
    }
}
