using Track;
using UI;
using UnityEngine;

namespace GeneralWaybilling.Zones
{
    /// <summary>
    /// Monitored update component that records mouse down position on track to detect drag gestures.
    /// Does not intercept or finish modal picking; TrackLocationPicker handles the release and commit.
    /// </summary>
    public sealed class ZoneDragRecorder : MonoBehaviour
    {
        private void Update()
        {
            TrackLocationPicker? picker = TrackLocationPicker.Shared;
            if (picker == null || !picker.IsPicking)
            {
                return;
            }

            if (GameInput.IsMouseOverUI(out _, out _))
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                Location? hover = picker.CurrentHoverLocation;
                if (hover.HasValue && hover.Value.IsValid)
                {
                    ZoneCreationTool.RecordDragStart(hover.Value, Input.mousePosition);
                }
            }
        }
    }
}
