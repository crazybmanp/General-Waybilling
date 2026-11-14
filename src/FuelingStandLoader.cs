using System;
using System.Collections.Generic;
using Model.Ops;
using Model.Ops.AutoDestinations;
using Model.Ops.Definition;
using RollingStock;
using Track;
using UnityEngine;

namespace GeneralWaybilling
{
    /// <summary>
    /// Industry component representing a repair/service station fueling stand
    /// (such as a coal tower or diesel fuel stand) as an outbound loading destination.
    /// Allows empty freight cars to be waybilled to the spout to load fuel from station storage.
    /// </summary>
    public sealed class FuelingStandLoader : IndustryComponent
    {
        private readonly Load targetLoad;
        private readonly CarLoadTargetLoader fuelingStand;
        private readonly List<IOpsCar> matchingCarsBuffer = new List<IOpsCar>();
        private readonly Comparison<IOpsCar> compareByLoadDescending;

        public Load TargetLoad => targetLoad;

        public CarLoadTargetLoader FuelingStand => fuelingStand;

        /// <summary>
        /// Initializes a fueling stand loading component bound to a repair station, load type,
        /// physical CarLoadTargetLoader spout, and tight spotting span.
        /// </summary>
        public FuelingStandLoader(
            Industry industry,
            Load load,
            CarLoadTargetLoader fuelingStand,
            string subId,
            string displayName,
            TrackSpan spottingSpan)
        {
            if (industry is null)
            {
                throw new ArgumentNullException(nameof(industry));
            }
            if (load is null)
            {
                throw new ArgumentNullException(nameof(load));
            }
            if (fuelingStand == null)
            {
                throw new ArgumentNullException(nameof(fuelingStand));
            }
            if (string.IsNullOrEmpty(subId))
            {
                throw new ArgumentNullException(nameof(subId));
            }
            if (spottingSpan == null)
            {
                throw new ArgumentNullException(nameof(spottingSpan));
            }

            Industry = industry;
            targetLoad = load;
            this.fuelingStand = fuelingStand;

            subIdentifier = subId;
            name = displayName;
            trackSpans = new TrackSpan[] { spottingSpan };
            carTypeFilter = load.carTypeFilter;
            sharedStorage = true;

            IsProgressionEnabled = industry.IsProgressionEnabled;
            IsTrackReachable = industry.IsTrackReachable;
            IsAnySpanReachable = industry.IsTrackReachable;

            compareByLoadDescending = CompareByLoadDescending;
        }

        /// <summary>
        /// Accepts cars carrying this stand's load so partially filled cars remain valid across save/load.
        /// </summary>
        public override bool AcceptsCarsWithLoad(Load checkLoad)
        {
            return checkLoad?.id == targetLoad.id;
        }

        /// <summary>
        /// Reports that this component draws from station storage for <paramref name="drawLoad"/>.
        /// </summary>
        public override bool FillsCarsFrom(Load drawLoad)
        {
            return drawLoad?.id == targetLoad.id;
        }

        /// <summary>
        /// Exposes this fueling stand as a When Empty -> From stock destination for compatible freight cars.
        /// </summary>
        public override IEnumerable<AutoDestinationOption> EnumerateAutoDestinations(AutoDestinationType destinationType, IOpsCar car)
        {
            if (destinationType != AutoDestinationType.Empty)
            {
                yield break;
            }

            if (targetLoad.carTypeFilter.Matches(car.CarType))
            {
                yield return new AutoDestinationOption(this, targetLoad.id, AutoDestinationTerms.Stock, targetLoad);
            }
        }

        /// <summary>
        /// Loads compatible cars spotted under the spout from station storage at the stand's outputRate,
        /// clearing or cycling the waybill once each car is full.
        /// </summary>
        public override void Service(IIndustryContext ctx)
        {
            if (fuelingStand == null || fuelingStand.outputRate <= 0f)
            {
                return;
            }

            float available = ctx.AvailableToDraw(targetLoad);
            float remainingRate = fuelingStand.outputRate * ctx.DeltaTime;
            if (available <= 0f || remainingRate <= 0f)
            {
                return;
            }

            CollectMatchingCars(ctx);
            if (matchingCarsBuffer.Count == 0)
            {
                return;
            }

            foreach (IOpsCar car in matchingCarsBuffer)
            {
                if (!car.IsFull(targetLoad))
                {
                    float quantityToLoad = Mathf.Min(available, remainingRate);
                    if (quantityToLoad <= 0f)
                    {
                        break;
                    }

                    float loaded = car.Load(targetLoad, quantityToLoad, Identifier);
                    if (loaded > 0f)
                    {
                        ctx.RemoveFromStorage(targetLoad, loaded);
                        available -= loaded;
                        remainingRate -= loaded;
                    }
                }

                if (car.IsFull(targetLoad))
                {
                    car.SetWaybill(null, this, "Full");
                }
            }
        }

        /// <summary>
        /// Populates <see cref="matchingCarsBuffer"/> with compatible waybilled cars at the spout,
        /// ordered from most-loaded to least-loaded so nearly full cars finish first.
        /// </summary>
        private void CollectMatchingCars(IIndustryContext ctx)
        {
            matchingCarsBuffer.Clear();
            foreach (IOpsCar car in EnumerateCars(ctx, CarFilter.CarTypeMatches | CarFilter.WaybilledHere))
            {
                if (car.IsEmptyOrContains(targetLoad))
                {
                    matchingCarsBuffer.Add(car);
                }
            }

            if (matchingCarsBuffer.Count > 1)
            {
                matchingCarsBuffer.Sort(compareByLoadDescending);
            }
        }

        /// <summary>
        /// Compares two cars in descending order of their current quantity of <see cref="targetLoad"/>.
        /// </summary>
        private int CompareByLoadDescending(IOpsCar a, IOpsCar b)
        {
            return b.QuantityOfLoad(targetLoad).quantity.CompareTo(a.QuantityOfLoad(targetLoad).quantity);
        }

        /// <summary>
        /// No-op; fueling stands do not automatically generate inbound car orders.
        /// </summary>
        public override void OrderCars(IIndustryContext ctx)
        {
            // Fueling stands do not automatically generate car orders.
        }
    }
}
