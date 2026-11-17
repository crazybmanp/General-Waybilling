using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using Track;
using UI;
using UI.Common;
using UnityEngine;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Interactive tool coordinator for creating player-drawn waybill zones.
    /// Utilizes native TrackLocationPicker for rail targeting and 3D arrow guidance,
    /// coordinates drag-to-select and drag-to-trim gestures, and manages live glowing previews.
    /// </summary>
    public static class ZoneCreationTool
    {
        public const float DragPixelThreshold = 40f;
        public const float DragTrackDistanceThreshold = 4f;

        private static string activeZoneName = string.Empty;
        private static string activeZoneId = string.Empty;
        private static Action<WaybillZone?>? activeOnComplete;

        private static readonly List<TrackSpan> selectedSpans = new List<TrackSpan>();
        private static readonly List<(string lower, string upper)> serializedSpans = new List<(string lower, string upper)>();
        private static TrackLocationPickRequest? activeRequest;
        private static string? previewToken;
        private static int spanCounter;
        private static bool isCompleting;

        private static Location? dragStartLocation;
        private static Location? currentHoverLocation;
        private static Vector3 dragStartMousePos;

        /// <summary>
        /// Begins an interactive track picking session to build or edit a waybill zone.
        /// </summary>
        public static void StartPicking(string zoneName, Action<WaybillZone?> onComplete)
        {
            if (string.IsNullOrWhiteSpace(zoneName))
            {
                Toast.Present("Please enter a valid zone name first.");
                return;
            }

            TrackLocationPicker? picker = TrackLocationPicker.Shared;
            if (picker == null)
            {
                Toast.Present("TrackLocationPicker is not available.");
                return;
            }

            if (picker.IsPicking)
            {
                picker.Cancel();
            }

            ClearSession();

            activeZoneName = zoneName.Trim();
            activeZoneId = "zone_" + activeZoneName.ToLowerInvariant().Replace(' ', '_').Replace('.', '_');
            activeOnComplete = onComplete;
            spanCounter = 0;
            isCompleting = false;

            EnsureDragRecorder();
            ArmPicker();
        }

        private static void EnsureDragRecorder()
        {
            Transform container = TrackSpanDetector.GetSpanContainer();
            ZoneDragRecorder? recorder = container.gameObject.GetComponent<ZoneDragRecorder>();
            if (recorder == null)
            {
                recorder = container.gameObject.AddComponent<ZoneDragRecorder>();
            }
            recorder.enabled = true;
        }

        /// <summary>
        /// Gets the latest hovered track location reported by TrackLocationPicker.
        /// </summary>
        public static Location? CurrentHoverLocation => currentHoverLocation;

        /// <summary>
        /// Records mouse-down position on track to measure drag gesture displacement.
        /// </summary>
        public static void RecordDragStart(Location loc, Vector3 mousePos)
        {
            dragStartLocation = loc;
            dragStartMousePos = mousePos;
        }

        private static void HandleHoverLocationChanged(Location? loc)
        {
            currentHoverLocation = loc;
        }

        private static void ArmPicker()
        {
            TrackLocationPicker? picker = TrackLocationPicker.Shared;
            if (picker == null)
            {
                CancelSession();
                return;
            }

            currentHoverLocation = null;
            string trackCountText = selectedSpans.Count == 1 ? "1 track" : $"{selectedSpans.Count} tracks";
            string prompt = $"[Drag/Click] Add  ·  [Ctrl+Drag/Click] Remove  ·  [Shift+Click] Spur (Buffer)  ·  [Space] Done ({trackCountText})  ·  [Esc] Cancel";

            TrackLocationPickRequest request = new TrackLocationPickRequest
            {
                Prompt = prompt,
                OnHoverLocationChanged = HandleHoverLocationChanged,
                OnLocationSelected = HandleLocationSelected,
                OnCycleRequested = CompleteSession,
                OnCancelled = CancelSession
            };
            activeRequest = request;

            if (!picker.StartPicking(request))
            {
                Toast.Present("Could not start track picking.");
                CancelSession();
            }
        }

        private static void HandleLocationSelected(Location clickedLoc)
        {
            if (!clickedLoc.IsValid || clickedLoc.segment == null || Graph.Shared == null)
            {
                dragStartLocation = null;
                Toast.Present("Invalid track location clicked.");
                ArmPicker();
                return;
            }

            // 1. Shift+Click ALWAYS takes priority for full spur detection with safety buffer
            if (GameInput.IsShiftDown)
            {
                dragStartLocation = null;
                HandleShiftClick(clickedLoc);
                ArmPicker();
                return;
            }

            bool isControl = GameInput.IsControlDown || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // 2. Check for intentional drag gesture
            Vector3 mouseDelta = Input.mousePosition - dragStartMousePos;
            bool isIntentionalDrag = false;

            if (dragStartLocation.HasValue && dragStartLocation.Value.IsValid)
            {
                Location start = dragStartLocation.Value;
                if (start.segment != clickedLoc.segment)
                {
                    if (mouseDelta.magnitude >= DragPixelThreshold)
                    {
                        isIntentionalDrag = true;
                    }
                }
                else
                {
                    float distStart = ZoneSpanGeometry.NormalizeDistance(start);
                    float distClicked = ZoneSpanGeometry.NormalizeDistance(clickedLoc);
                    float trackDist = Mathf.Abs(distStart - distClicked);
                    if (mouseDelta.magnitude >= DragPixelThreshold && trackDist >= DragTrackDistanceThreshold)
                    {
                        isIntentionalDrag = true;
                    }
                }
            }

            if (isIntentionalDrag && dragStartLocation.HasValue)
            {
                Location start = dragStartLocation.Value;
                dragStartLocation = null;
                HandleDragCompleted(start, clickedLoc, isControl);
            }
            else
            {
                dragStartLocation = null;
                HandleClickCompleted(clickedLoc, isControl);
            }

            ArmPicker();
        }

        /// <summary>
        /// Checks whether a track location is covered by any span currently selected in this tool session.
        /// </summary>
        public static bool IsLocationSelected(Location loc)
        {
            if (!loc.IsValid || loc.segment == null)
            {
                return false;
            }

            for (int i = 0; i < selectedSpans.Count; i++)
            {
                TrackSpan span = selectedSpans[i];
                if (span != null && span.IsValid && ZoneSpanGeometry.IsLocationInsideSpan(span, loc))
                {
                    return true;
                }
            }

            return false;
        }

        private static void HandleClickCompleted(Location loc, bool isControl)
        {
            TrackSegment clickedSegment = loc.segment;
            float length = clickedSegment.GetLength();

            if (isControl)
            {
                // UNSELECT / REMOVE: Trim the entire clicked segment [0, length]
                ZoneSpanGeometry.SubtractRangeFromSpans(activeZoneId, ref spanCounter, clickedSegment, 0f, length, selectedSpans, serializedSpans);
                ZoneSpanGeometry.MergeConnectedSpans(activeZoneId, ref spanCounter, selectedSpans, serializedSpans);
                UpdatePreview();
                string remainingText = selectedSpans.Count == 1 ? "1 track remaining" : $"{selectedSpans.Count} tracks remaining";
                Toast.Present($"Unselected track ({remainingText}).");
            }
            else
            {
                // ADD: Add entire clicked segment and merge
                Location lower = new Location(clickedSegment, 0f, TrackSegment.End.A);
                Location upper = new Location(clickedSegment, length, TrackSegment.End.A);

                string spanId = $"{activeZoneId}_{spanCounter++}";
                TrackSpan newSpan = TrackSpanDetector.CreateRuntimeSpan(spanId, lower, upper);
                selectedSpans.Add(newSpan);
                serializedSpans.Add((Graph.LocationToString(lower), Graph.LocationToString(upper)));

                ZoneSpanGeometry.MergeConnectedSpans(activeZoneId, ref spanCounter, selectedSpans, serializedSpans);

                UpdatePreview();
                float feet = newSpan.Length * 3.28084f;
                string totalText = selectedSpans.Count == 1 ? "1 track" : $"{selectedSpans.Count} tracks";
                Toast.Present($"Added track ({feet:F0} ft). Total: {totalText}.");
            }
        }

        private static void HandleShiftClick(Location clickedLoc)
        {
            // Contiguous spur detection WITH switch safety buffer
            if (!TrackSpanDetector.TrySmartDetectSpur(clickedLoc, out Location lower, out Location upper, out HashSet<TrackSegment> spurSegments))
            {
                Toast.Present("Could not auto-detect spur boundaries at this location.");
                return;
            }

            // Find any existing spans that intersect this contiguous spur
            List<TrackSpan> overlappingSpans = new List<TrackSpan>();
            for (int i = 0; i < selectedSpans.Count; i++)
            {
                TrackSpan s = selectedSpans[i];
                if (s == null)
                {
                    continue;
                }

                bool intersects = false;
                if (s.lower.HasValue && spurSegments.Contains(s.lower.Value.segment))
                {
                    intersects = true;
                }
                else if (s.upper.HasValue && spurSegments.Contains(s.upper.Value.segment))
                {
                    intersects = true;
                }
                else
                {
                    IReadOnlyCollection<TrackSegment> segs = s.GetSegments();
                    if (segs != null && segs.Any(seg => spurSegments.Contains(seg)))
                    {
                        intersects = true;
                    }
                }

                if (intersects)
                {
                    overlappingSpans.Add(s);
                }
            }

            if (overlappingSpans.Count > 0)
            {
                // UNSELECT: Remove all overlapping spans
                for (int i = 0; i < overlappingSpans.Count; i++)
                {
                    TrackSpan spanToRemove = overlappingSpans[i];
                    int index = selectedSpans.IndexOf(spanToRemove);
                    if (index >= 0)
                    {
                        selectedSpans.RemoveAt(index);
                        serializedSpans.RemoveAt(index);
                    }
                    if (Graph.Shared != null)
                    {
                        Graph.Shared.RemoveSpan(spanToRemove);
                        UnityEngine.Object.Destroy(spanToRemove.gameObject);
                    }
                }

                UpdatePreview();
                string remainingText = selectedSpans.Count == 1 ? "1 track remaining" : $"{selectedSpans.Count} tracks remaining";
                Toast.Present($"Unselected spur ({overlappingSpans.Count} span(s) removed). {remainingText}.");
            }
            else
            {
                // SELECT: Add contiguous spur
                string spanId = $"{activeZoneId}_{spanCounter++}";
                TrackSpan newSpan = TrackSpanDetector.CreateRuntimeSpan(spanId, lower, upper);
                selectedSpans.Add(newSpan);
                serializedSpans.Add((Graph.LocationToString(lower), Graph.LocationToString(upper)));

                ZoneSpanGeometry.MergeConnectedSpans(activeZoneId, ref spanCounter, selectedSpans, serializedSpans);

                UpdatePreview();
                float feet = newSpan.Length * 3.28084f;
                string totalText = selectedSpans.Count == 1 ? "1 track" : $"{selectedSpans.Count} tracks";
                Toast.Present($"Selected spur with safety buffer ({feet:F0} ft). Total: {totalText}.");
            }
        }

        private static void HandleDragCompleted(Location start, Location end, bool isControl)
        {
            if (isControl)
            {
                // UNSELECT / TRIM: Subtract the dragged path from overlapping spans
                ZoneSpanGeometry.SubtractPath(activeZoneId, ref spanCounter, start, end, selectedSpans, serializedSpans);
                ZoneSpanGeometry.MergeConnectedSpans(activeZoneId, ref spanCounter, selectedSpans, serializedSpans);
                UpdatePreview();
                string remainingText = selectedSpans.Count == 1 ? "1 track remaining" : $"{selectedSpans.Count} tracks remaining";
                Toast.Present($"Trimmed track section ({remainingText}).");
            }
            else
            {
                // SELECT / ADD: Add exact dragged span [start, end] and merge with any existing spans
                TrackSpan? created = ZoneSpanGeometry.CreateSpanBetween(activeZoneId, ref spanCounter, start, end);
                if (created != null)
                {
                    selectedSpans.Add(created);
                    serializedSpans.Add((Graph.LocationToString(created.lower!.Value), Graph.LocationToString(created.upper!.Value)));

                    ZoneSpanGeometry.MergeConnectedSpans(activeZoneId, ref spanCounter, selectedSpans, serializedSpans);
                    UpdatePreview();
                    float feet = created.Length * 3.28084f;
                    string totalText = selectedSpans.Count == 1 ? "1 track" : $"{selectedSpans.Count} tracks";
                    Toast.Present($"Added track section ({feet:F0} ft). Total: {totalText}.");
                }
                else
                {
                    Toast.Present("Could not establish valid track path along drag.");
                }
            }
        }

        private static void UpdatePreview()
        {
            if (previewToken != null)
            {
                SegmentIndicatorController.Shared?.Remove(previewToken);
                previewToken = null;
            }

            if (selectedSpans.Count > 0 && SegmentIndicatorController.Shared != null)
            {
                List<string> spanIds = selectedSpans.Select(s => s.id).ToList();
                previewToken = SegmentIndicatorController.Shared.Add(spanIds);
            }
        }

        private static void CompleteSession()
        {
            if (isCompleting)
            {
                return;
            }

            if (selectedSpans.Count == 0)
            {
                Toast.Present("No tracks selected. Click or drag a track to select it, or press [Esc] to cancel.");
                ArmPicker();
                return;
            }

            isCompleting = true;

            if (activeRequest != null)
            {
                activeRequest.OnCancelled = null;
                activeRequest.OnLocationSelected = null;
                activeRequest.OnCycleRequested = null;
            }

            TrackLocationPicker? picker = TrackLocationPicker.Shared;
            if (picker != null && picker.IsPicking)
            {
                picker.Cancel();
            }

            ClearPreview();

            // Find closest Area based on track midpoint
            string? areaId = null;
            if (OpsController.Shared != null && selectedSpans.Count > 0)
            {
                Vector3 center = Vector3.zero;
                int validPoints = 0;
                foreach (TrackSpan span in selectedSpans)
                {
                    if (span != null && span.lower.HasValue && Graph.Shared != null)
                    {
                        center += Graph.Shared.GetPosition(span.lower.Value);
                        validPoints++;
                    }
                }

                if (validPoints > 0)
                {
                    center /= validPoints;
                    Area? closest = OpsController.Shared.Areas
                        .OrderBy(a => Vector3.Distance(a.Position, center))
                        .FirstOrDefault();
                    areaId = closest?.identifier;
                }
            }

            WaybillZone zone = new WaybillZone(activeZoneId, activeZoneName, areaId);
            for (int i = 0; i < selectedSpans.Count; i++)
            {
                (string lower, string upper) pair = serializedSpans[i];
                zone.AddSpan(selectedSpans[i], pair.lower, pair.upper);
            }

            WaybillZoneRegistry.RegisterZone(zone);

            string areaName = zone.Area?.name ?? "Independent";
            float totalFeet = zone.TotalLength * 3.28084f;
            Toast.Present($"Zone '{activeZoneName}' created in {areaName} ({totalFeet:F0} ft, {selectedSpans.Count} span(s)).");

            Action<WaybillZone?>? cb = activeOnComplete;
            ClearSession();
            cb?.Invoke(zone);
        }

        private static void CancelSession()
        {
            if (isCompleting)
            {
                return;
            }

            if (activeRequest != null)
            {
                activeRequest.OnCancelled = null;
                activeRequest.OnLocationSelected = null;
                activeRequest.OnCycleRequested = null;
            }

            ClearPreview();

            TrackLocationPicker? picker = TrackLocationPicker.Shared;
            if (picker != null && picker.IsPicking)
            {
                picker.Cancel();
            }

            foreach (TrackSpan span in selectedSpans)
            {
                if (span != null && Graph.Shared != null)
                {
                    Graph.Shared.RemoveSpan(span);
                    UnityEngine.Object.Destroy(span.gameObject);
                }
            }

            Action<WaybillZone?>? cb = activeOnComplete;
            ClearSession();
            Toast.Present("Zone creation cancelled.");
            cb?.Invoke(null);
        }

        private static void ClearPreview()
        {
            if (previewToken != null)
            {
                SegmentIndicatorController.Shared?.Remove(previewToken);
                previewToken = null;
            }
        }

        private static void ClearSession()
        {
            ClearPreview();
            selectedSpans.Clear();
            serializedSpans.Clear();
            activeZoneName = string.Empty;
            activeZoneId = string.Empty;
            activeOnComplete = null;
            activeRequest = null;
            isCompleting = false;
            dragStartLocation = null;
            currentHoverLocation = null;
        }
    }
}
