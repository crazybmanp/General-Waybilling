using System;
using System.Collections.Generic;
using System.Linq;
using Track;
using Track.Search;
using UnityEngine;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Pure mathematical and geometric operations for track spans, interval slicing,
    /// route resolution, and continuous span merging across nodes and switches.
    /// </summary>
    public static class ZoneSpanGeometry
    {
        public const float TouchToleranceMeters = 0.5f;
        public const float OverlapEpsilonMeters = 0.2f;
        public const float MinSpanLengthMeters = 0.5f;

        /// <summary>
        /// Converts a Track Location's distance into a normalized offset measured from TrackSegment.End.A.
        /// </summary>
        public static float NormalizeDistance(Location loc)
        {
            if (!loc.IsValid || loc.segment == null)
            {
                return 0f;
            }

            float segLen = loc.segment.GetLength();
            return loc.EndIsA ? loc.distance : (segLen - loc.distance);
        }

        /// <summary>
        /// Checks whether a specific track location falls within the geometric bounds of a TrackSpan.
        /// Performs exact numeric interval testing on single-segment spans and falls back to graph containment.
        /// </summary>
        public static bool IsLocationInsideSpan(TrackSpan span, Location loc)
        {
            if (span == null || !span.IsValid || !loc.IsValid || loc.segment == null)
            {
                return false;
            }

            if (span.lower.HasValue && span.upper.HasValue &&
                span.lower.Value.segment == loc.segment && span.upper.Value.segment == loc.segment)
            {
                float distLoc = NormalizeDistance(loc);
                float distLower = NormalizeDistance(span.lower.Value);
                float distUpper = NormalizeDistance(span.upper.Value);
                float min = Mathf.Min(distLower, distUpper);
                float max = Mathf.Max(distLower, distUpper);
                return distLoc >= min - OverlapEpsilonMeters && distLoc <= max + OverlapEpsilonMeters;
            }

            return span.Contains(loc);
        }

        /// <summary>
        /// Evaluates all 4 directional heading combinations to find the shortest navigable route between two locations.
        /// </summary>
        private static bool TryFindOptimalRoute(
            Graph graph,
            Location a,
            Location b,
            out List<RouteSearch.Step> bestSteps,
            out Location bestStart,
            out Location bestEnd)
        {
            bestSteps = null!;
            bestStart = default(Location);
            bestEnd = default(Location);

            Location[] startCandidates = new Location[] { a, a.Flipped() };
            Location[] endCandidates = new Location[] { b, b.Flipped() };

            foreach (Location s in startCandidates)
            {
                foreach (Location e in endCandidates)
                {
                    List<RouteSearch.Step> steps = graph.FindRoute(s, e, HeuristicCosts.Zero, checkForCars: false, 0f, 0f, 200);
                    if (steps != null && steps.Count > 0)
                    {
                        if (bestSteps == null || steps.Count < bestSteps.Count)
                        {
                            bestSteps = steps;
                            bestStart = s;
                            bestEnd = e;
                        }
                    }
                }
            }

            if (bestSteps == null)
            {
                foreach (Location s in endCandidates)
                {
                    foreach (Location e in startCandidates)
                    {
                        List<RouteSearch.Step> steps = graph.FindRoute(s, e, HeuristicCosts.Zero, checkForCars: false, 0f, 0f, 200);
                        if (steps != null && steps.Count > 0)
                        {
                            if (bestSteps == null || steps.Count < bestSteps.Count)
                            {
                                bestSteps = steps;
                                bestStart = s;
                                bestEnd = e;
                            }
                        }
                    }
                }
            }

            return bestSteps != null && bestSteps.Count > 0;
        }

        /// <summary>
        /// Creates a runtime TrackSpan connecting two track locations, resolving single-segment
        /// coordinates directly and multi-segment connections via directional route search.
        /// </summary>
        public static TrackSpan? CreateSpanBetween(string zoneId, ref int spanCounter, Location a, Location b)
        {
            if (Graph.Shared == null || !a.IsValid || !b.IsValid || a.segment == null || b.segment == null)
            {
                return null;
            }

            if (a.segment == b.segment)
            {
                float distA = NormalizeDistance(a);
                float distB = NormalizeDistance(b);
                float min = Mathf.Min(distA, distB);
                float max = Mathf.Max(distA, distB);

                Location lower = new Location(a.segment, min, TrackSegment.End.A);
                Location upper = new Location(a.segment, max, TrackSegment.End.A);
                string spanId = $"{zoneId}_{spanCounter++}";
                return TrackSpanDetector.CreateRuntimeSpan(spanId, lower, upper);
            }

            Graph graph = Graph.Shared;
            if (TryFindOptimalRoute(graph, a, b, out _, out Location bestStart, out Location bestEnd))
            {
                string spanId = $"{zoneId}_{spanCounter++}";
                return TrackSpanDetector.CreateRuntimeSpan(spanId, bestStart, bestEnd);
            }

            return null;
        }

        /// <summary>
        /// Returns all TrackSegment objects traversed along the shortest navigable path between two locations.
        /// </summary>
        public static List<TrackSegment> GetSegmentsAlongPath(Location a, Location b)
        {
            List<TrackSegment> result = new List<TrackSegment>();
            if (Graph.Shared == null || !a.IsValid || !b.IsValid || a.segment == null || b.segment == null)
            {
                return result;
            }

            if (a.segment == b.segment)
            {
                result.Add(a.segment);
                return result;
            }

            Graph graph = Graph.Shared;
            if (TryFindOptimalRoute(graph, a, b, out List<RouteSearch.Step> bestSteps, out _, out _))
            {
                for (int i = 0; i < bestSteps.Count; i++)
                {
                    TrackSegment? seg = bestSteps[i].Location.segment;
                    if (seg != null && !result.Contains(seg))
                    {
                        result.Add(seg);
                    }
                    if (i < bestSteps.Count - 1 && bestSteps[i].Node != null && bestSteps[i + 1].Node != null)
                    {
                        TrackSegment common = graph.SegmentCommonToNodes(bestSteps[i].Node, bestSteps[i + 1].Node);
                        if (common != null && !result.Contains(common))
                        {
                            result.Add(common);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Subtracts the navigable track path between two locations from the selected spans collection.
        /// Handles single-segment partial trims as well as multi-segment interval clipping across nodes.
        /// </summary>
        public static void SubtractPath(
            string zoneId,
            ref int spanCounter,
            Location start,
            Location end,
            List<TrackSpan> selectedSpans,
            List<(string lower, string upper)> serializedSpans)
        {
            if (!start.IsValid || !end.IsValid || start.segment == null || end.segment == null)
            {
                return;
            }

            if (start.segment == end.segment)
            {
                TrackSegment seg = start.segment;
                float distStart = NormalizeDistance(start);
                float distEnd = NormalizeDistance(end);
                float cutMin = Mathf.Min(distStart, distEnd);
                float cutMax = Mathf.Max(distStart, distEnd);

                SubtractRangeFromSpans(zoneId, ref spanCounter, seg, cutMin, cutMax, selectedSpans, serializedSpans);
                return;
            }

            List<TrackSegment> traversed = GetSegmentsAlongPath(start, end);
            if (traversed.Count > 0)
            {
                for (int i = 0; i < traversed.Count; i++)
                {
                    TrackSegment seg = traversed[i];
                    float segLen = seg.GetLength();
                    float cutMin = 0f;
                    float cutMax = segLen;

                    if (i == 0)
                    {
                        float distStart = NormalizeDistance(start);
                        TrackSegment next = traversed[1];
                        TrackNode joint = (seg.a == next.a || seg.a == next.b) ? seg.a : seg.b;
                        float distJoint = (joint == seg.a) ? 0f : segLen;
                        cutMin = Mathf.Min(distStart, distJoint);
                        cutMax = Mathf.Max(distStart, distJoint);
                    }
                    else if (i == traversed.Count - 1)
                    {
                        float distEnd = NormalizeDistance(end);
                        TrackSegment prev = traversed[i - 1];
                        TrackNode joint = (seg.a == prev.a || seg.a == prev.b) ? seg.a : seg.b;
                        float distJoint = (joint == seg.a) ? 0f : segLen;
                        cutMin = Mathf.Min(distEnd, distJoint);
                        cutMax = Mathf.Max(distEnd, distJoint);
                    }

                    SubtractRangeFromSpans(zoneId, ref spanCounter, seg, cutMin, cutMax, selectedSpans, serializedSpans);
                }
            }
            else
            {
                float segLen = start.segment.GetLength();
                float distStart = NormalizeDistance(start);
                SubtractRangeFromSpans(zoneId, ref spanCounter, start.segment, Mathf.Max(0f, distStart - 2f), Mathf.Min(segLen, distStart + 2f), selectedSpans, serializedSpans);
            }
        }

        /// <summary>
        /// Subtracts an interval [cutMin, cutMax] (measured from End.A) from any spans covering the specified segment.
        /// Replaces overlapping spans with remaining non-overlapped sub-spans.
        /// </summary>
        public static void SubtractRangeFromSpans(
            string zoneId,
            ref int spanCounter,
            TrackSegment seg,
            float cutMin,
            float cutMax,
            List<TrackSpan> selectedSpans,
            List<(string lower, string upper)> serializedSpans)
        {
            if (seg == null || selectedSpans == null || serializedSpans == null)
            {
                return;
            }

            float segLen = seg.GetLength();
            List<(float remMin, float remMax)> newRanges = new List<(float, float)>();

            for (int i = selectedSpans.Count - 1; i >= 0; i--)
            {
                TrackSpan span = selectedSpans[i];
                if (span == null || !span.IsValid || !span.lower.HasValue || !span.upper.HasValue)
                {
                    continue;
                }

                if (span.lower.Value.segment == seg && span.upper.Value.segment == seg)
                {
                    float distA = NormalizeDistance(span.lower.Value);
                    float distB = NormalizeDistance(span.upper.Value);
                    float spanMin = Mathf.Min(distA, distB);
                    float spanMax = Mathf.Max(distA, distB);

                    float overlapStart = Mathf.Max(cutMin, spanMin);
                    float overlapEnd = Mathf.Min(cutMax, spanMax);

                    if (overlapEnd - overlapStart > OverlapEpsilonMeters)
                    {
                        selectedSpans.RemoveAt(i);
                        serializedSpans.RemoveAt(i);
                        if (Graph.Shared != null)
                        {
                            Graph.Shared.RemoveSpan(span);
                            UnityEngine.Object.Destroy(span.gameObject);
                        }

                        if (spanMin < cutMin - OverlapEpsilonMeters)
                        {
                            newRanges.Add((spanMin, cutMin));
                        }
                        if (cutMax + OverlapEpsilonMeters < spanMax)
                        {
                            newRanges.Add((cutMax, spanMax));
                        }
                    }
                }
                else
                {
                    IReadOnlyCollection<TrackSegment> segs = span.GetSegments();
                    if (segs != null && segs.Contains(seg))
                    {
                        if (span.lower.Value.segment == seg)
                        {
                            float distA = NormalizeDistance(span.lower.Value);
                            if (cutMin <= distA + OverlapEpsilonMeters && cutMax > distA)
                            {
                                span.lower = new Location(seg, cutMax, TrackSegment.End.A);
                                span.InvalidateCache();
                            }
                        }
                        else if (span.upper.Value.segment == seg)
                        {
                            float distB = NormalizeDistance(span.upper.Value);
                            if (cutMax >= distB - OverlapEpsilonMeters && cutMin < distB)
                            {
                                span.upper = new Location(seg, cutMin, TrackSegment.End.A);
                                span.InvalidateCache();
                            }
                        }
                        else
                        {
                            selectedSpans.RemoveAt(i);
                            serializedSpans.RemoveAt(i);
                            if (Graph.Shared != null)
                            {
                                Graph.Shared.RemoveSpan(span);
                                UnityEngine.Object.Destroy(span.gameObject);
                            }
                        }
                    }
                }
            }

            foreach ((float remMin, float remMax) in newRanges)
            {
                if (remMax - remMin > MinSpanLengthMeters)
                {
                    Location remLower = new Location(seg, remMin, TrackSegment.End.A);
                    Location remUpper = new Location(seg, remMax, TrackSegment.End.A);
                    string spanId = $"{zoneId}_{spanCounter++}";
                    TrackSpan remSpan = TrackSpanDetector.CreateRuntimeSpan(spanId, remLower, remUpper);
                    selectedSpans.Add(remSpan);
                    serializedSpans.Add((Graph.LocationToString(remLower), Graph.LocationToString(remUpper)));
                }
            }
        }

        /// <summary>
        /// Merges any pairs of spans in selectedSpans that overlap on the same segment or touch at endpoint nodes.
        /// </summary>
        public static void MergeConnectedSpans(
            string zoneId,
            ref int spanCounter,
            List<TrackSpan> selectedSpans,
            List<(string lower, string upper)> serializedSpans)
        {
            if (selectedSpans == null || serializedSpans == null)
            {
                return;
            }

            bool mergedAny = true;
            int iterations = 0;

            while (mergedAny && iterations++ < 50)
            {
                mergedAny = false;
                for (int i = 0; i < selectedSpans.Count; i++)
                {
                    for (int j = i + 1; j < selectedSpans.Count; j++)
                    {
                        TrackSpan a = selectedSpans[i];
                        TrackSpan b = selectedSpans[j];

                        if (TryMergeTwoSpans(zoneId, ref spanCounter, a, b, out TrackSpan? merged) && merged != null)
                        {
                            selectedSpans.RemoveAt(j);
                            serializedSpans.RemoveAt(j);
                            selectedSpans.RemoveAt(i);
                            serializedSpans.RemoveAt(i);

                            if (Graph.Shared != null)
                            {
                                Graph.Shared.RemoveSpan(a);
                                UnityEngine.Object.Destroy(a.gameObject);
                                Graph.Shared.RemoveSpan(b);
                                UnityEngine.Object.Destroy(b.gameObject);
                            }

                            selectedSpans.Add(merged);
                            serializedSpans.Add((Graph.LocationToString(merged.lower!.Value), Graph.LocationToString(merged.upper!.Value)));

                            mergedAny = true;
                            break;
                        }
                    }

                    if (mergedAny)
                    {
                        break;
                    }
                }
            }
        }

        private static bool TryMergeTwoSpans(string zoneId, ref int spanCounter, TrackSpan a, TrackSpan b, out TrackSpan? merged)
        {
            merged = null;
            if (a == null || b == null || !a.lower.HasValue || !a.upper.HasValue || !b.lower.HasValue || !b.upper.HasValue || Graph.Shared == null)
            {
                return false;
            }

            Graph graph = Graph.Shared;

            // Case 1: Both spans are on the exact same single TrackSegment
            if (a.lower.Value.segment == a.upper.Value.segment &&
                b.lower.Value.segment == b.upper.Value.segment &&
                a.lower.Value.segment == b.lower.Value.segment)
            {
                TrackSegment seg = a.lower.Value.segment;
                float a1 = NormalizeDistance(a.lower.Value);
                float a2 = NormalizeDistance(a.upper.Value);
                float minA = Mathf.Min(a1, a2);
                float maxA = Mathf.Max(a1, a2);

                float b1 = NormalizeDistance(b.lower.Value);
                float b2 = NormalizeDistance(b.upper.Value);
                float minB = Mathf.Min(b1, b2);
                float maxB = Mathf.Max(b1, b2);

                if (maxA >= minB - OverlapEpsilonMeters && maxB >= minA - OverlapEpsilonMeters)
                {
                    float newMin = Mathf.Min(minA, minB);
                    float newMax = Mathf.Max(maxA, maxB);

                    Location newLower = new Location(seg, newMin, TrackSegment.End.A);
                    Location newUpper = new Location(seg, newMax, TrackSegment.End.A);

                    string spanId = $"{zoneId}_{spanCounter++}";
                    merged = TrackSpanDetector.CreateRuntimeSpan(spanId, newLower, newUpper);
                    return true;
                }
            }

            // Case 2: Endpoint connection across nodes/switches
            Vector3 posALower = graph.GetPosition(a.lower.Value);
            Vector3 posAUpper = graph.GetPosition(a.upper.Value);
            Vector3 posBLower = graph.GetPosition(b.lower.Value);
            Vector3 posBUpper = graph.GetPosition(b.upper.Value);

            Location? candLower = null;
            Location? candUpper = null;

            if (Vector3.Distance(posAUpper, posBLower) < TouchToleranceMeters)
            {
                candLower = a.lower.Value;
                candUpper = b.upper.Value;
            }
            else if (Vector3.Distance(posAUpper, posBUpper) < TouchToleranceMeters)
            {
                candLower = a.lower.Value;
                candUpper = b.lower.Value;
            }
            else if (Vector3.Distance(posALower, posBLower) < TouchToleranceMeters)
            {
                candLower = a.upper.Value;
                candUpper = b.upper.Value;
            }
            else if (Vector3.Distance(posALower, posBUpper) < TouchToleranceMeters)
            {
                candLower = a.upper.Value;
                candUpper = b.lower.Value;
            }

            if (candLower.HasValue && candUpper.HasValue)
            {
                List<RouteSearch.Step> steps = graph.FindRoute(candLower.Value, candUpper.Value, HeuristicCosts.Zero, checkForCars: false, 0f, 0f, 200);
                if (steps == null || steps.Count == 0)
                {
                    steps = graph.FindRoute(candUpper.Value, candLower.Value, HeuristicCosts.Zero, checkForCars: false, 0f, 0f, 200);
                    if (steps != null && steps.Count > 0)
                    {
                        Location temp = candLower.Value;
                        candLower = candUpper.Value;
                        candUpper = temp;
                    }
                }

                if (steps != null && steps.Count > 0)
                {
                    string spanId = $"{zoneId}_{spanCounter++}";
                    TrackSpan candidate = TrackSpanDetector.CreateRuntimeSpan(spanId, candLower.Value, candUpper.Value);
                    if (candidate.IsValid && candidate.Length > 0.1f)
                    {
                        float expectedLength = a.Length + b.Length;
                        if (Mathf.Abs(candidate.Length - expectedLength) < 5.0f)
                        {
                            merged = candidate;
                            return true;
                        }
                    }

                    graph.RemoveSpan(candidate);
                    UnityEngine.Object.Destroy(candidate.gameObject);
                }
            }

            return false;
        }
    }
}
