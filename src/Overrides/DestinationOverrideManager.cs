using System;
using System.Collections.Generic;
using System.Linq;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Network;
using UnityEngine;

namespace GeneralWaybilling.Overrides
{
    /// <summary>
    /// Manages temporary routing destination overrides on cars, mirroring the base game's repair override pattern.
    /// Allows routing cars to custom zones, yards, or industries without modifying underlying revenue waybills.
    /// </summary>
    public static class DestinationOverrideManager
    {
        public const string KeyOpsOverrideDest = "gw.override-dest";

        /// <summary>
        /// Checks whether the specified car has an active routing override destination.
        /// </summary>
        public static bool HasOverride(Car car)
        {
            if (car is null || car.KeyValueObject == null)
            {
                return false;
            }

            return !car.KeyValueObject[KeyOpsOverrideDest].IsNull;
        }

        /// <summary>
        /// Attempts to resolve the active override destination for the car.
        /// </summary>
        public static bool TryGetOverride(Car car, IOpsCarPositionResolver resolver, out (OpsCarPosition position, string? tag)? result)
        {
            result = null;
            if (car is null || resolver is null || car.KeyValueObject == null)
            {
                return false;
            }

            Value val = car.KeyValueObject[KeyOpsOverrideDest];
            if (val.IsNull)
            {
                return false;
            }

            try
            {
                if (val.Type == KeyValue.Runtime.ValueType.String)
                {
                    OpsCarPosition pos = resolver.ResolveOpsCarPosition(val.StringValue);
                    result = (pos, null);
                    return true;
                }

                if (val.Type == KeyValue.Runtime.ValueType.Dictionary)
                {
                    IReadOnlyDictionary<string, Value> dict = val.DictionaryValue;
                    if (!dict.TryGetValue("id", out Value idVal) || string.IsNullOrEmpty(idVal.StringValue))
                    {
                        return false;
                    }

                    OpsCarPosition pos = resolver.ResolveOpsCarPosition(idVal.StringValue);
                    string? tag = dict.TryGetValue("tag", out Value tagVal) && !tagVal.IsNull ? tagVal.StringValue : null;
                    result = (pos, tag);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Warning($"Failed to resolve override destination for {car}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Sets or clears the destination override for the given car.
        /// </summary>
        public static void SetOverride(Car car, (OpsCarPosition position, string? tag)? destination)
        {
            if (car is null || car.KeyValueObject == null)
            {
                return;
            }

            if (!destination.HasValue)
            {
                car.KeyValueObject[KeyOpsOverrideDest] = Value.Null();
                return;
            }

            Dictionary<string, Value> dict = new Dictionary<string, Value>
            {
                { "id", Value.String(destination.Value.position.Identifier) },
                { "tag", string.IsNullOrEmpty(destination.Value.tag) ? Value.Null() : Value.String(destination.Value.tag!) }
            };

            car.KeyValueObject[KeyOpsOverrideDest] = Value.Dictionary(dict);
        }

        /// <summary>
        /// Determines whether a car is physically spotted and stopped on the track spans of the override destination.
        /// </summary>
        public static bool IsCarSpottedAtOverride(Car car, OpsCarPosition destination)
        {
            if (car is null || OpsController.Shared == null)
            {
                return false;
            }

            // 1. Must be physically stopped (base game threshold is 0.05 m/s)
            if (Mathf.Abs(car.velocity) > 0.05f)
            {
                return false;
            }

            // 2. Must be located on the destination spans
            return OpsController.Shared.CarsAtPosition(destination).Contains(car);
        }

        /// <summary>
        /// Retrieves all coupled cars that share the specified override destination and are spotted on its spans.
        /// </summary>
        public static List<Car> GetSpottedCoupledCars(Car car, OpsCarPosition destination)
        {
            List<Car> spottedCars = new List<Car>();
            if (car is null || OpsController.Shared == null)
            {
                return spottedCars;
            }

            foreach (Car coupled in car.EnumerateCoupled())
            {
                if (TryGetOverride(coupled, OpsController.Shared, out (OpsCarPosition position, string? tag)? res) && res.HasValue)
                {
                    if (string.Equals(res.Value.position.Identifier, destination.Identifier, StringComparison.Ordinal)
                        && IsCarSpottedAtOverride(coupled, destination))
                    {
                        spottedCars.Add(coupled);
                    }
                }
            }

            return spottedCars;
        }

        /// <summary>
        /// Counts how many coupled cars share this override destination and are spotted on the span.
        /// </summary>
        public static int CountSpottedCoupledCars(Car car, OpsCarPosition destination)
        {
            return GetSpottedCoupledCars(car, destination).Count;
        }

        /// <summary>
        /// Completes the destination override for a single car, reverting it to its underlying waybill.
        /// </summary>
        public static void CompleteOverride(Car car)
        {
            if (car is null || OpsController.Shared == null)
            {
                return;
            }

            if (TryGetOverride(car, OpsController.Shared, out (OpsCarPosition position, string? tag)? result) && result.HasValue)
            {
                string destName = result.Value.position.DisplayName;
                SetOverride(car, null);

                Waybill? original = car.GetWaybill(OpsController.Shared);
                string nextInfo = original.HasValue ? $"Waybill resumed to {original.Value.Destination.DisplayName}." : "No prior waybill.";
                Multiplayer.Broadcast($"{Hyperlink.To(car)}: Override waybill completed at {destName}. {nextInfo}");
            }
        }

        /// <summary>
        /// Completes the destination override for all spotted coupled cars sharing the same override.
        /// </summary>
        public static int CompleteOverrideCoupled(Car car)
        {
            if (car is null || OpsController.Shared == null)
            {
                return 0;
            }

            if (!TryGetOverride(car, OpsController.Shared, out (OpsCarPosition position, string? tag)? target) || !target.HasValue)
            {
                return 0;
            }

            OpsCarPosition targetPos = target.Value.position;
            List<Car> spottedCars = GetSpottedCoupledCars(car, targetPos);

            foreach (Car spotted in spottedCars)
            {
                SetOverride(spotted, null);
            }

            if (spottedCars.Count > 0)
            {
                Multiplayer.Broadcast($"{targetPos.DisplayName}: Override waybills completed for {spottedCars.Count} car(s). Original waybills resumed.");
            }

            return spottedCars.Count;
        }

        /// <summary>
        /// Copies the car's current override destination to all coupled cars.
        /// </summary>
        public static int CopyOverrideToCoupled(Car car, IOpsCarPositionResolver resolver)
        {
            if (car is null || resolver is null)
            {
                return 0;
            }

            TryGetOverride(car, resolver, out (OpsCarPosition position, string? tag)? sourceOverride);

            int count = 0;
            foreach (Car coupled in car.EnumerateCoupled())
            {
                if (coupled != car)
                {
                    SetOverride(coupled, sourceOverride);
                    count++;
                }
            }

            return count;
        }
    }
}
