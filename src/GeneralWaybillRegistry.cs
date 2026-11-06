using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using Model.Ops.Definition;

namespace GeneralWaybilling
{
    /// <summary>
    /// Registry managing general waybill destinations for multi-landing industries,
    /// tracking associations between composite destinations and individual landing components.
    /// </summary>
    public static class GeneralWaybillRegistry
    {
        private static readonly Dictionary<string, GeneralIndustryLoader> componentsByIdentifier =
            new Dictionary<string, GeneralIndustryLoader>(StringComparer.Ordinal);

        private static readonly FieldInfo? industryComponentLookupField =
            typeof(OpsController).GetField("_industryComponentLookup", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Registers general destinations for all eligible multi-landing industries across the given areas.
        /// </summary>
        public static void RegisterForAreas(IEnumerable<Area> areas)
        {
            if (areas is null)
            {
                return;
            }

            foreach (Area area in areas)
            {
                if (area.Industries is null)
                {
                    continue;
                }

                foreach (Industry industry in area.Industries)
                {
                    RegisterForIndustry(industry);
                }
            }
        }

        /// <summary>
        /// Registers general destinations for an industry if it contains multiple loaders for the same load.
        /// </summary>
        public static void RegisterForIndustry(Industry industry)
        {
            if (industry is null || industry.Components is null)
            {
                return;
            }

            // Find all IndustryLoader components and group them by Load
            List<IndustryLoader> loaders = industry.Components.OfType<IndustryLoader>().ToList();
            IEnumerable<IGrouping<Load, IndustryLoader>> groups = loaders
                .Where(loader => loader.load != null)
                .GroupBy(loader => loader.load);

            foreach (IGrouping<Load, IndustryLoader> group in groups)
            {
                List<IndustryLoader> memberList = group.ToList();
                // Only create general destinations when there are 2 or more distinct landing spurs
                if (memberList.Count < 2)
                {
                    continue;
                }

                Load load = group.Key;
                string generalSubId = "general_" + load.id.Replace('/', '_');
                string generalIdentifier = industry.identifier + "." + generalSubId;

                GeneralIndustryLoader? existing = industry.Components
                    .OfType<GeneralIndustryLoader>()
                    .FirstOrDefault(c => c.Identifier == generalIdentifier);

                if (existing is null)
                {
                    GeneralIndustryLoader generalLoader = new GeneralIndustryLoader(industry, load, memberList);
                    industry.Components.Add(generalLoader);
                    existing = generalLoader;
                    ModEntryPoint.Log.Info($"Registered general waybill destination: '{existing.DisplayName}' ({generalIdentifier}) with {memberList.Count} member landings.");
                }

                componentsByIdentifier[generalIdentifier] = existing;
            }
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

            if (opsController.AllIndustries != null)
            {
                foreach (Industry industry in opsController.AllIndustries)
                {
                    RegisterForIndustry(industry);
                }
            }

            if (industryComponentLookupField != null)
            {
                Dictionary<string, IndustryComponent>? lookup =
                    industryComponentLookupField.GetValue(opsController) as Dictionary<string, IndustryComponent>;

                if (lookup != null)
                {
                    foreach (KeyValuePair<string, GeneralIndustryLoader> pair in componentsByIdentifier)
                    {
                        lookup[pair.Key] = pair.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to get a registered GeneralIndustryLoader by its identifier.
        /// </summary>
        public static bool TryGetComponent(string identifier, out GeneralIndustryLoader component)
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
            if (TryGetComponent(identifier, out GeneralIndustryLoader loader))
            {
                position = loader;
                return true;
            }

            position = default(OpsCarPosition);
            return false;
        }

        /// <summary>
        /// Determines whether a car destination matches or is accepted by an industry component.
        /// Returns true if destination is directly the component, or if destination is a general destination
        /// of which the component is a member landing.
        /// </summary>
        public static bool IsDestinationAccepted(IndustryComponent component, OpsCarPosition destination)
        {
            if (component is null || string.IsNullOrEmpty(destination.Identifier))
            {
                return false;
            }

            if (TryGetComponent(destination.Identifier, out GeneralIndustryLoader generalLoader))
            {
                return generalLoader.ServesLanding(component.Identifier);
            }

            return false;
        }

        /// <summary>
        /// Determines whether two OpsCarPosition instances are equivalent under general waybilling rules
        /// (e.g., one is a general destination and the other is one of its member landings).
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

            if (TryGetComponent(a.Identifier, out GeneralIndustryLoader generalA) && generalA.ServesLanding(b.Identifier))
            {
                return true;
            }

            if (TryGetComponent(b.Identifier, out GeneralIndustryLoader generalB) && generalB.ServesLanding(a.Identifier))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears all registrations and removes custom components from industries upon mod unload.
        /// </summary>
        public static void UnregisterAll()
        {
            foreach (KeyValuePair<string, GeneralIndustryLoader> pair in componentsByIdentifier)
            {
                Industry industry = pair.Value.Industry;
                industry?.Components?.Remove(pair.Value);
            }

            componentsByIdentifier.Clear();
            ModEntryPoint.Log.Info("Unregistered all general waybill destinations.");
        }
    }
}
