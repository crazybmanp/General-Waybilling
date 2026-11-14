using System;
using System.Collections.Generic;
using Track;
using UnityEngine;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Provides algorithmic spur detection and runtime TrackSpan creation for player-drawn waybill zones.
    /// Handles physical switch fouling clearance, dead-end bumper margins, and span GameObject registration.
    /// </summary>
    public static class TrackSpanDetector
    {
        public const float DefaultLeadClearanceMeters = 3.5f;
        public const float DefaultBumperClearanceMeters = 2.0f;
        public const float FoulingSafetyMarginMeters = 1.0f;
        public const float MinEndOffsetMeters = 0.5f;

        private static Transform? spanContainer;

        public static Transform GetSpanContainer()
        {
            if (spanContainer == null)
            {
                GameObject containerObj = new GameObject("GW_ZoneSpans");
                UnityEngine.Object.DontDestroyOnLoad(containerObj);
                spanContainer = containerObj.transform;
            }
            return spanContainer;
        }

        /// <summary>
        /// Automatically detects the physical boundaries of a siding or spur starting from a clicked track location.
        /// Walks both directions along the track graph until encountering a switch or dead end.
        /// </summary>
        public static bool TrySmartDetectSpur(Location clickedLoc, out Location lowerOut, out Location upperOut, float clearanceMeters = DefaultLeadClearanceMeters)
        {
            return TrySmartDetectSpur(clickedLoc, out lowerOut, out upperOut, out _, clearanceMeters);
        }

        /// <summary>
        /// Automatically detects the physical boundaries of a siding or spur starting from a clicked track location.
        /// Walks both directions along the track graph until encountering a switch or dead end, collecting all traversed segments.
        /// </summary>
        public static bool TrySmartDetectSpur(Location clickedLoc, out Location lowerOut, out Location upperOut, out HashSet<TrackSegment> spurSegments, float clearanceMeters = DefaultLeadClearanceMeters)
        {
            lowerOut = default(Location);
            upperOut = default(Location);
            spurSegments = new HashSet<TrackSegment>();

            if (!clickedLoc.IsValid || clickedLoc.segment == null || Graph.Shared == null)
            {
                return false;
            }

            TrackSegment clickedSegment = clickedLoc.segment;
            spurSegments.Add(clickedSegment);

            // Trace toward End A
            Location endA = TraceToBoundary(clickedSegment, TrackSegment.End.A, clearanceMeters, spurSegments);

            // Trace toward End B
            Location endB = TraceToBoundary(clickedSegment, TrackSegment.End.B, clearanceMeters, spurSegments);

            if (!endA.IsValid || !endB.IsValid)
            {
                return false;
            }

            lowerOut = endA;
            upperOut = endB;
            return true;
        }

        private static Location TraceToBoundary(TrackSegment startSegment, TrackSegment.End startEnd, float clearanceMeters, HashSet<TrackSegment> visitedSegments)
        {
            Graph graph = Graph.Shared;
            TrackSegment currentSegment = startSegment;
            TrackSegment.End currentEnd = startEnd;
            int stepCount = 0;
            const int maxSteps = 150;

            while (stepCount++ < maxSteps)
            {
                visitedSegments.Add(currentSegment);
                TrackNode node = (currentEnd == TrackSegment.End.A) ? currentSegment.a : currentSegment.b;

                // 1. Check for switch boundary
                if (graph.IsSwitch(node))
                {
                    float length = currentSegment.GetLength();
                    float distFromEnd = clearanceMeters;

                    if (graph.DecodeSwitchAt(node, out TrackSegment enter, out TrackSegment a, out TrackSegment b))
                    {
                        if (enter != currentSegment)
                        {
                            // Trailing / diverging side of switch: clear the frog fouling point
                            float foulingDistance = graph.CalculateFoulingDistance(node);
                            distFromEnd = Mathf.Max(clearanceMeters, foulingDistance + FoulingSafetyMarginMeters);
                        }
                    }

                    if (distFromEnd >= length)
                    {
                        // The entire segment is within the switch fouling zone; stop at its far boundary
                        float distance = (currentEnd == TrackSegment.End.A) ? length : 0f;
                        return new Location(currentSegment, distance, TrackSegment.End.A);
                    }
                    else
                    {
                        float distance = (currentEnd == TrackSegment.End.A)
                            ? distFromEnd
                            : (length - distFromEnd);
                        return new Location(currentSegment, distance, TrackSegment.End.A);
                    }
                }

                // 2. Check for bumper or dead end
                if (graph.NodeIsDeadEnd(node, out _))
                {
                    float length = currentSegment.GetLength();
                    float distFromEnd = Mathf.Clamp(DefaultBumperClearanceMeters, MinEndOffsetMeters, Mathf.Max(MinEndOffsetMeters, length - MinEndOffsetMeters));
                    float distance = (currentEnd == TrackSegment.End.A)
                        ? distFromEnd
                        : (length - distFromEnd);
                    return new Location(currentSegment, distance, TrackSegment.End.A);
                }

                // 3. Trace connected continuing segments
                IReadOnlyList<TrackSegment> connected = graph.SegmentsConnectedTo(node);
                if (connected.Count != 2)
                {
                    float length = currentSegment.GetLength();
                    float distFromEnd = Mathf.Clamp(clearanceMeters, MinEndOffsetMeters, Mathf.Max(MinEndOffsetMeters, length - MinEndOffsetMeters));
                    float distance = (currentEnd == TrackSegment.End.A)
                        ? distFromEnd
                        : (length - distFromEnd);
                    return new Location(currentSegment, distance, TrackSegment.End.A);
                }

                TrackSegment nextSegment = (connected[0] == currentSegment) ? connected[1] : connected[0];
                TrackSegment.End nextEnd = (nextSegment.a == node) ? TrackSegment.End.B : TrackSegment.End.A;

                currentSegment = nextSegment;
                currentEnd = nextEnd;
            }

            return new Location(currentSegment, 0f, TrackSegment.End.A);
        }

        /// <summary>
        /// Creates and registers a runtime TrackSpan on the active track graph.
        /// </summary>
        public static TrackSpan CreateRuntimeSpan(string spanId, Location lower, Location upper)
        {
            if (Graph.Shared == null)
            {
                throw new InvalidOperationException("Graph.Shared is null when creating runtime track span.");
            }

            GameObject spanObject = new GameObject("ZoneSpan-" + spanId);
            spanObject.transform.SetParent(GetSpanContainer(), false);

            TrackSpan trackSpan = spanObject.AddComponent<TrackSpan>();
            trackSpan.id = spanId;
            trackSpan.lower = lower;
            trackSpan.upper = upper;

            try
            {
                trackSpan.OrientEndpointsInward();
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Warning($"OrientEndpointsInward failed for {spanId}: {ex.Message}");
            }

            Graph.Shared.AddSpan(trackSpan);
            return trackSpan;
        }

        /// <summary>
        /// Restores a runtime TrackSpan from serialized location strings.
        /// </summary>
        public static TrackSpan? RestoreRuntimeSpan(string spanId, string lowerString, string upperString)
        {
            if (Graph.Shared == null)
            {
                return null;
            }

            try
            {
                Location lower = Graph.Shared.ResolveLocationString(lowerString);
                Location upper = Graph.Shared.ResolveLocationString(upperString);
                return CreateRuntimeSpan(spanId, lower, upper);
            }
            catch (Exception ex)
            {
                ModEntryPoint.Log.Error(ex, $"Failed to resolve locations for zone span {spanId}");
                return null;
            }
        }
    }
}
