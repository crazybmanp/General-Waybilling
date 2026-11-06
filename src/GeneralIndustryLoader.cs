using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using Model.Ops.AutoDestinations;
using Model.Ops.Definition;
using Track;

namespace GeneralWaybilling
{
    /// <summary>
    /// Represents a composite/general industry component for multi-track or multi-landing industries,
    /// allowing cars to be waybilled to a general destination (e.g. Connelly Creek - Logs)
    /// rather than being constrained to a single landing spur.
    /// </summary>
    public sealed class GeneralIndustryLoader : IndustryComponent
    {
        private readonly Load targetLoad;
        private readonly List<IndustryLoader> memberLoaders;
        private readonly HashSet<string> memberIdentifiers;

        public Load TargetLoad => targetLoad;

        public IReadOnlyList<IndustryLoader> MemberLoaders => memberLoaders;

        public override bool IsVisible => false;

        public GeneralIndustryLoader(
            Industry industry,
            Load load,
            IEnumerable<IndustryLoader> loaders)
        {
            if (industry is null)
            {
                throw new ArgumentNullException(nameof(industry));
            }
            if (load is null)
            {
                throw new ArgumentNullException(nameof(load));
            }
            if (loaders is null)
            {
                throw new ArgumentNullException(nameof(loaders));
            }

            Industry = industry;
            targetLoad = load;
            memberLoaders = loaders.ToList();
            memberIdentifiers = new HashSet<string>(memberLoaders.Select(loader => loader.Identifier), StringComparer.Ordinal);

            subIdentifier = "general_" + load.id.Replace('/', '_');
            name = industry.DisplayName + " - " + load.description;

            // Combine all unique track spans across all member landings
            List<TrackSpan> spans = new List<TrackSpan>();
            foreach (IndustryLoader loader in memberLoaders)
            {
                if (loader.trackSpans != null)
                {
                    foreach (TrackSpan span in loader.trackSpans)
                    {
                        if (!spans.Contains(span))
                        {
                            spans.Add(span);
                        }
                    }
                }
            }
            trackSpans = spans.ToArray();

            // Match all car types supported by member loaders or the load itself
            carTypeFilter = load.carTypeFilter;

            IsProgressionEnabled = true;
            IsTrackReachable = true;
            IsAnySpanReachable = true;
        }

        public override bool AcceptsCarsWithLoad(Load checkLoad)
        {
            return checkLoad == targetLoad;
        }

        public override bool FillsCarsFrom(Load drawLoad)
        {
            return drawLoad?.id == targetLoad.id;
        }

        /// <summary>
        /// Determines whether the specified landing spur identifier is a member of this general destination.
        /// </summary>
        public bool ServesLanding(string spurIdentifier)
        {
            if (string.IsNullOrEmpty(spurIdentifier))
            {
                return false;
            }

            return memberIdentifiers.Contains(spurIdentifier);
        }

        /// <summary>
        /// Determines whether the specified OpsCarPosition is a member landing of this general destination.
        /// </summary>
        public bool ServesLanding(OpsCarPosition position)
        {
            return ServesLanding(position.Identifier);
        }

        public const string GeneralTag = "general";

        public override IEnumerable<AutoDestinationOption> EnumerateAutoDestinations(AutoDestinationType destinationType, IOpsCar car)
        {
            if (destinationType == AutoDestinationType.Empty)
            {
                yield return new AutoDestinationOption(this, GeneralTag, AutoDestinationTerms.CaptiveService, targetLoad);
            }
        }

        public override void Service(IIndustryContext ctx)
        {
            // Individual member loaders service cars located on their specific track spans.
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            // Ordering is managed by individual member loaders.
        }

        public override void CheckForCompleted(IIndustryContext ctx)
        {
            // Waybill completion is handled when a car is spotted at an individual member landing.
        }
    }
}
