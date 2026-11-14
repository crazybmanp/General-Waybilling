using System;
using Model.Ops;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Lightweight IndustryComponent adapter representing a player-created waybill zone.
    /// Integrates player zones into the base game's OpsController lookup and naming system.
    /// </summary>
    public sealed class ZoneIndustryComponent : IndustryComponent
    {
        private readonly WaybillZone zone;

        public WaybillZone Zone => zone;

        public override bool IsVisible => true;

        public override string DisplayName => zone.DisplayName;

        public ZoneIndustryComponent(WaybillZone zone)
        {
            this.zone = zone ?? throw new ArgumentNullException(nameof(zone));

            subIdentifier = zone.Id;
            name = zone.DisplayName;
            trackSpans = zone.RuntimeSpans.ToArray();
            carTypeFilter = new CarTypeFilter(string.Empty);

            IsProgressionEnabled = true;
            IsTrackReachable = true;
            IsAnySpanReachable = true;
        }

        public override void Service(IIndustryContext ctx)
        {
            // Player zones do not load or consume freight
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            // Player zones do not automatically generate car orders
        }
    }
}
