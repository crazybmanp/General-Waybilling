using System;
using System.Collections.Generic;
using GeneralWaybilling.Zones;
using UI;
using UI.Builder;
using UI.Common;
using UI.Windows;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GeneralWaybilling.UI
{
    /// <summary>
    /// Management window for creating, viewing, focusing, and deleting player-drawn waybill zones.
    /// Provides in-world hover track previews and clean plain-text controls.
    /// </summary>
    public sealed class ZoneManagerWindow : BuilderWindowBase<ZoneManagerWindow>
    {
        private static readonly Vector2Int DefaultSize = new Vector2Int(480, 420);
        private static readonly Vector2Int MinSize = new Vector2Int(400, 300);

        private string newZoneName = string.Empty;

        public override string Identifier => "GeneralWaybilling.ZoneManager";

        public override WindowConfig Config => WindowConfig.Resizable(DefaultSize, MinSize);

        protected override string Title => "Waybill Zones Manager";

        public static void Open()
        {
            WindowManager? shared = WindowManager.Shared;
            if (shared == null)
            {
                return;
            }

            ZoneManagerWindow window;
            try
            {
                window = shared.GetWindow<ZoneManagerWindow>();
            }
            catch (ArgumentException)
            {
                window = shared.CreateWindowWithComponent<ZoneManagerWindow>();
            }

            window.Show();
        }

        public static void ToggleOpen()
        {
            WindowManager? shared = WindowManager.Shared;
            if (shared == null)
            {
                return;
            }

            ZoneManagerWindow window;
            try
            {
                window = shared.GetWindow<ZoneManagerWindow>();
            }
            catch (ArgumentException)
            {
                window = shared.CreateWindowWithComponent<ZoneManagerWindow>();
            }

            if (window.Window != null && window.Window.IsShown)
            {
                window.Window.CloseWindow();
            }
            else
            {
                window.Show();
            }
        }

        protected override void BuildContent(UIPanelBuilder builder)
        {
            // Creation Section
            builder.AddSection("Create New Zone", delegate(UIPanelBuilder createSection)
            {
                createSection.FieldLabelWidth = 90f;
                createSection.AddField("Zone Name", createSection.AddInputField(newZoneName, delegate(string val)
                {
                    newZoneName = val;
                }, "e.g. Whittier Inbound Track 1", 40));

                createSection.Spacer(4f);

                createSection.AddButton("Select Track", delegate
                {
                    if (string.IsNullOrWhiteSpace(newZoneName))
                    {
                        Toast.Present("Enter a zone name before picking track.");
                        return;
                    }

                    string targetName = newZoneName.Trim();
                    newZoneName = string.Empty;
                    Window.CloseWindow();

                    ZoneCreationTool.StartPicking(targetName, delegate(WaybillZone? zone)
                    {
                        Open();
                    });
                }).Tooltip("Select Track", "Drag/click to add track. Hold Ctrl + drag/click to remove. Shift+click toggles spur. Press [Space] when done.");

                createSection.Spacer(2f);
                createSection.AddLabel("<i>Tip: [Drag/Click] adds. [Ctrl+Drag/Click] removes. [Shift+Click] toggles spur (buffer). [Space] Finishes.</i>");
            });

            builder.Spacer(6f);

            // Existing Zones List Section
            IReadOnlyList<WaybillZone> zones = WaybillZoneRegistry.GetAllZones();
            builder.AddSection($"Configured Zones ({zones.Count})", delegate(UIPanelBuilder listSection)
            {
                if (zones.Count == 0)
                {
                    listSection.AddLabel("<i>No custom waybill zones created yet. Enter a name above to begin.</i>");
                    return;
                }

                listSection.VScrollView(delegate(UIPanelBuilder scroll)
                {
                    foreach (WaybillZone zone in zones)
                    {
                        string areaName = zone.Area?.name ?? "Custom / No Town";
                        float lengthFt = zone.TotalLength * 3.28084f;
                        string details = $"{areaName}  •  {zone.RuntimeSpans.Count} span(s)  •  {lengthFt:F0} ft";

                        RectTransform rowRect = scroll.HStack(delegate(UIPanelBuilder row)
                        {
                            row.VStack(delegate(UIPanelBuilder info)
                            {
                                info.AddLabel($"<b>{zone.DisplayName}</b>");
                                info.AddLabel($"<size=85%><color=#A0A0A0>{details}</color></size>");
                            });

                            row.Spacer();

                            row.ButtonStrip(delegate(UIPanelBuilder btnStrip)
                            {
                                btnStrip.AddButtonCompact("Locate", delegate
                                {
                                    if (zone.RuntimeSpans.Count > 0 && CameraSelector.shared != null)
                                    {
                                        CameraSelector.shared.ZoomToPoint(zone.Position.GetCenter());
                                    }
                                }).Tooltip("Locate Zone", "Zoom overhead camera to this zone.");

                                btnStrip.AddButtonCompact("Delete", delegate
                                {
                                    WaybillZoneRegistry.RemoveZone(zone.Id);
                                    RebuildPanel();
                                }).Tooltip("Delete Zone", "Permanently remove this waybill zone.");
                            });
                        });

                        // Attach in-world hover preview to the row
                        ZoneHoverPreview hover = rowRect.gameObject.AddComponent<ZoneHoverPreview>();
                        hover.SetZone(zone);

                        scroll.Spacer(6f);
                    }
                });
            });
        }
    }

    /// <summary>
    /// Illuminates zone track spans in the 3D world when the player hovers over a zone row.
    /// </summary>
    public sealed class ZoneHoverPreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private WaybillZone? zone;
        private string? indicatorToken;

        public void SetZone(WaybillZone z)
        {
            zone = z;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (zone != null && zone.RuntimeSpans.Count > 0 && SegmentIndicatorController.Shared != null)
            {
                List<string> spanIds = zone.GetSpanIds();
                if (spanIds.Count > 0)
                {
                    indicatorToken = SegmentIndicatorController.Shared.Add(spanIds);
                }
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ClearIndicator();
        }

        private void OnDisable()
        {
            ClearIndicator();
        }

        private void OnDestroy()
        {
            ClearIndicator();
        }

        private void ClearIndicator()
        {
            if (indicatorToken != null)
            {
                SegmentIndicatorController.Shared?.Remove(indicatorToken);
                indicatorToken = null;
            }
        }
    }
}
