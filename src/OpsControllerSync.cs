using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;

namespace GeneralWaybilling
{
    /// <summary>
    /// Shared helper for synchronizing mod-created <see cref="IndustryComponent"/> instances
    /// with <see cref="OpsController"/>'s internal lookup collections.
    /// </summary>
    internal static class OpsControllerSync
    {
        private static readonly FieldInfo? industryComponentLookupField =
            typeof(OpsController).GetField("_industryComponentLookup", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo? allIndustryComponentsProperty =
            typeof(OpsController).GetProperty(nameof(OpsController.AllIndustryComponents), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool warnedMissingLookupField;
        private static bool warnedMissingAllComponentsSetter;

        /// <summary>
        /// Registers <paramref name="components"/> in <paramref name="opsController"/>'s identifier lookup
        /// and optionally appends any missing entries to <see cref="OpsController.AllIndustryComponents"/>.
        /// </summary>
        public static void SyncComponents(OpsController opsController, IEnumerable<IndustryComponent> components, bool includeAllIndustryComponents)
        {
            if (opsController is null || components is null)
            {
                return;
            }

            List<IndustryComponent> componentList = components.Where(c => c != null).ToList();
            if (componentList.Count == 0)
            {
                return;
            }

            SyncLookup(opsController, componentList);

            if (includeAllIndustryComponents)
            {
                SyncAllIndustryComponents(opsController, componentList);
            }
        }

        private static void SyncLookup(OpsController opsController, List<IndustryComponent> components)
        {
            if (industryComponentLookupField == null)
            {
                if (!warnedMissingLookupField)
                {
                    warnedMissingLookupField = true;
                    ModEntryPoint.Log.Warning("OpsController._industryComponentLookup field was not found; synthetic waybill destinations may not resolve.");
                }
                return;
            }

            if (industryComponentLookupField.GetValue(opsController) is Dictionary<string, IndustryComponent> lookup)
            {
                foreach (IndustryComponent component in components)
                {
                    lookup[component.Identifier] = component;
                }
            }
        }

        private static void SyncAllIndustryComponents(OpsController opsController, List<IndustryComponent> components)
        {
            if (allIndustryComponentsProperty?.SetMethod == null)
            {
                if (!warnedMissingAllComponentsSetter)
                {
                    warnedMissingAllComponentsSetter = true;
                    ModEntryPoint.Log.Warning("OpsController.AllIndustryComponents setter was not found; PositionForCar may not detect new components.");
                }
                return;
            }

            if (opsController.AllIndustryComponents == null)
            {
                return;
            }

            HashSet<IndustryComponent> existing = new HashSet<IndustryComponent>(opsController.AllIndustryComponents);
            bool added = false;
            foreach (IndustryComponent component in components)
            {
                if (existing.Add(component))
                {
                    added = true;
                }
            }

            if (added)
            {
                allIndustryComponentsProperty.SetMethod.Invoke(opsController, new object[] { existing.ToArray() });
            }
        }
    }
}
