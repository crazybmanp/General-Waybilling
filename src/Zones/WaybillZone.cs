using System;
using System.Collections.Generic;
using System.Linq;
using KeyValue.Runtime;
using Model.Ops;
using Track;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Represents a player-defined destination zone consisting of one or more track spans.
    /// Can be associated with an Area (Town) or categorized independently.
    /// </summary>
    public sealed class WaybillZone
    {
        public string Id { get; }
        public string DisplayName { get; set; }
        public string? AreaId { get; set; }

        public List<string> SerializedSpans { get; } = new List<string>();
        public List<TrackSpan> RuntimeSpans { get; } = new List<TrackSpan>();

        private ZoneIndustryComponent? cachedComponent;
        private Industry? cachedIndustry;

        public float TotalLength => RuntimeSpans.Sum((TrackSpan s) => s != null ? s.Length : 0f);

        /// <summary>
        /// Retrieves the list of runtime TrackSpan IDs associated with this zone.
        /// </summary>
        public List<string> GetSpanIds()
        {
            List<string> ids = new List<string>(RuntimeSpans.Count);
            for (int i = 0; i < RuntimeSpans.Count; i++)
            {
                TrackSpan s = RuntimeSpans[i];
                if (s != null && !string.IsNullOrEmpty(s.id))
                {
                    ids.Add(s.id);
                }
            }
            return ids;
        }

        public OpsCarPosition Position => Component;

        public ZoneIndustryComponent Component
        {
            get
            {
                if (cachedComponent == null)
                {
                    cachedComponent = new ZoneIndustryComponent(this);
                }
                return cachedComponent;
            }
        }

        public Industry Industry
        {
            get
            {
                if (cachedIndustry == null)
                {
                    cachedIndustry = new Industry
                    {
                        identifier = Id,
                        name = DisplayName,
                        Area = Area,
                        usesContract = false,
                        IsProgressionEnabled = false,
                        IsTrackReachable = true
                    };
                    cachedIndustry.Initialize();
                    Component.Industry = cachedIndustry;
                    cachedIndustry.Components.Add(Component);
                }
                return cachedIndustry;
            }
        }

        public Area? Area
        {
            get
            {
                if (string.IsNullOrEmpty(AreaId) || OpsController.Shared == null || OpsController.Shared.Areas == null)
                {
                    return null;
                }

                foreach (Area area in OpsController.Shared.Areas)
                {
                    if (string.Equals(area.identifier, AreaId, StringComparison.OrdinalIgnoreCase))
                    {
                        return area;
                    }
                }

                return null;
            }
        }

        public WaybillZone(string id, string displayName, string? areaId = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            Id = id;
            DisplayName = displayName ?? id;
            AreaId = areaId;
        }

        public void InvalidateCache()
        {
            cachedComponent = null;
            cachedIndustry?.Dispose();
            cachedIndustry = null;
        }

        public void AddSpan(TrackSpan span, string lowerString, string upperString)
        {
            if (span is null)
            {
                throw new ArgumentNullException(nameof(span));
            }

            RuntimeSpans.Add(span);
            SerializedSpans.Add(lowerString + "#" + upperString);
            InvalidateCache();
        }

        public void ClearSpans()
        {
            foreach (TrackSpan span in RuntimeSpans)
            {
                if (span != null && Graph.Shared != null)
                {
                    Graph.Shared.RemoveSpan(span);
                    UnityEngine.Object.Destroy(span.gameObject);
                }
            }

            RuntimeSpans.Clear();
            SerializedSpans.Clear();
            InvalidateCache();
        }

        public Value ToKeyValue()
        {
            Dictionary<string, Value> dict = new Dictionary<string, Value>
            {
                { "id", Value.String(Id) },
                { "name", Value.String(DisplayName) },
                { "areaId", string.IsNullOrEmpty(AreaId) ? Value.Null() : Value.String(AreaId!) }
            };

            List<Value> spanValues = new List<Value>();
            foreach (string serialized in SerializedSpans)
            {
                spanValues.Add(Value.String(serialized));
            }

            dict["spans"] = Value.Array(spanValues);
            return Value.Dictionary(dict);
        }

        public static WaybillZone? FromKeyValue(Value value)
        {
            if (value.Type != KeyValue.Runtime.ValueType.Dictionary)
            {
                return null;
            }

            IReadOnlyDictionary<string, Value> dict = value.DictionaryValue;
            string id = dict.TryGetValue("id", out Value idVal) ? idVal.StringValue : string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            string name = dict.TryGetValue("name", out Value nameVal) ? nameVal.StringValue : id;
            string? areaId = dict.TryGetValue("areaId", out Value areaVal) && !areaVal.IsNull ? areaVal.StringValue : null;

            WaybillZone zone = new WaybillZone(id, name, areaId);
            if (dict.TryGetValue("spans", out Value spansVal) && spansVal.Type == KeyValue.Runtime.ValueType.Array)
            {
                foreach (Value item in spansVal.ArrayValue)
                {
                    if (item.Type == KeyValue.Runtime.ValueType.String)
                    {
                        zone.SerializedSpans.Add(item.StringValue);
                    }
                }
            }

            return zone;
        }
    }
}
