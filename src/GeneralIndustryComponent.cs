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
    /// Represents a composite/general industry component for multi-landing industries or multi-industry areas,
    /// allowing cars to be waybilled to a general loading or unloading destination rather than a single spur.
    /// </summary>
    public sealed class GeneralIndustryComponent : IndustryComponent
    {
        private readonly Load targetLoad;
        private readonly AutoDestinationType targetDestinationType;
        private readonly AutoDestinationTerms targetTerms;
        private readonly bool isAreaLevel;
        private readonly List<IndustryComponent> memberComponents;
        private readonly HashSet<string> memberIdentifiers;

        public Load TargetLoad => targetLoad;

        public AutoDestinationType TargetDestinationType => targetDestinationType;

        public bool IsAreaLevel => isAreaLevel;

        public IReadOnlyList<IndustryComponent> MemberComponents => memberComponents;

        public override bool IsVisible => false;

        public GeneralIndustryComponent(
            Industry industry,
            Load load,
            IEnumerable<IndustryComponent> members,
            AutoDestinationType destinationType,
            bool isAreaLevel,
            string? displayNamePrefix = null)
        {
            if (industry is null)
            {
                throw new ArgumentNullException(nameof(industry));
            }
            if (load is null)
            {
                throw new ArgumentNullException(nameof(load));
            }
            if (members is null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            Industry = industry;
            targetLoad = load;
            targetDestinationType = destinationType;
            targetTerms = destinationType == AutoDestinationType.Empty
                ? AutoDestinationTerms.CaptiveService
                : AutoDestinationTerms.Unload;
            this.isAreaLevel = isAreaLevel;

            memberComponents = members.ToList();
            memberIdentifiers = new HashSet<string>(
                memberComponents.Select(member => member.Identifier),
                StringComparer.Ordinal);

            string scopePrefix = isAreaLevel ? "general_area_" : "general_";
            string modePrefix = destinationType == AutoDestinationType.Load ? "unload_" : string.Empty;
            subIdentifier = scopePrefix + modePrefix + load.id.Replace('/', '_');

            string prefix = string.IsNullOrEmpty(displayNamePrefix) ? industry.DisplayName : displayNamePrefix!;
            name = prefix + " - " + load.description;

            List<TrackSpan> spans = new List<TrackSpan>();
            foreach (IndustryComponent member in memberComponents)
            {
                if (member.trackSpans != null)
                {
                    foreach (TrackSpan span in member.trackSpans)
                    {
                        if (!spans.Contains(span))
                        {
                            spans.Add(span);
                        }
                    }
                }
            }
            trackSpans = spans.ToArray();

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
            return targetDestinationType == AutoDestinationType.Empty && drawLoad?.id == targetLoad.id;
        }

        /// <summary>
        /// Determines whether the specified spur identifier is a member of this general destination.
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
        /// Determines whether the specified OpsCarPosition is a member spur of this general destination.
        /// </summary>
        public bool ServesLanding(OpsCarPosition position)
        {
            return ServesLanding(position.Identifier);
        }

        public const string GeneralTag = "general";

        public override IEnumerable<AutoDestinationOption> EnumerateAutoDestinations(AutoDestinationType destinationType, IOpsCar car)
        {
            if (destinationType != targetDestinationType)
            {
                yield break;
            }

            if (isAreaLevel && CountAvailableIndustries() < 2)
            {
                yield break;
            }

            yield return new AutoDestinationOption(this, GeneralTag, targetTerms, targetLoad);
        }

        private int CountAvailableIndustries()
        {
            HashSet<string> availableIndustries = new HashSet<string>(StringComparer.Ordinal);
            foreach (IndustryComponent member in memberComponents)
            {
                Industry? memberIndustry = member.Industry;
                if (memberIndustry == null)
                {
                    continue;
                }

                if (memberIndustry.IsEnabledReachable &&
                    member.IsProgressionEnabled &&
                    member.IsAnySpanReachable &&
                    memberIndustry.ShouldOrderCars())
                {
                    availableIndustries.Add(memberIndustry.identifier);
                }
            }

            return availableIndustries.Count;
        }

        public override void Service(IIndustryContext ctx)
        {
            // Individual member components service cars located on their specific track spans.
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            // Ordering is managed by individual member components.
        }

        public override void CheckForCompleted(IIndustryContext ctx)
        {
            // Waybill completion is handled when a car is spotted at an individual member spur.
        }
    }
}
