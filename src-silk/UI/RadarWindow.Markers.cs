// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.UI.Maps;
using eft_dma_radar.Silk.UI.Shell;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// User-placed map markers (local + shared/global): right-click placement, the ImGui
    /// create/edit popup, radar rendering, and hover hit-testing. Storage + persistence
    /// live in <see cref="MapMarkerManager"/>; this partial is the desktop UI/render layer.
    /// </summary>
    internal static partial class RadarWindow
    {
        #region Fields

        /// <summary>Marker currently under the cursor (for the tooltip + right-click edit).</summary>
        private static MapMarker? _mouseOverMarker;

        // Editor popup state
        private static bool _markerPopupOpenRequested;
        private static bool _markerEditMode;            // true = editing existing, false = creating
        private static string? _markerEditId;
        private static Vector3 _markerNewWorld;         // world pos for a pending new marker
        private static Vector2 _markerPopupScreenPos;   // where to anchor the popup
        private static readonly byte[] _markerLabelBuf = new byte[128];
        private static string _markerColorHex = "#FFB300"; // == MarkerPalette[1]; literal avoids static-init ordering
        private static bool _markerShared;

        /// <summary>Quick-pick colour palette for the marker editor.</summary>
        private static readonly string[] MarkerPalette =
        [
            "#FF5252", // red
            "#FFB300", // amber
            "#FFEB3B", // yellow
            "#69F0AE", // green
            "#40C4FF", // blue
            "#7C4DFF", // purple
            "#FFFFFF", // white
            "#FF80AB", // pink
        ];

        // Reusable scratch paints — mutated per marker (render thread is single-threaded).
        private static readonly SKPaint _markerFillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
        private static readonly SKPaint _markerRingPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true };
        private static readonly SKPaint _markerTextPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };

        private const float MarkerHitRadius = 12f; // scene-space pixels

        #endregion

        #region Rendering

        /// <summary>
        /// Draws every marker for the current map. Called from <c>DrawRadar</c> while the
        /// canvas is scaled by UIScale, so screen coords are in unzoomed scene space.
        /// </summary>
        private static void DrawMapMarkers(SKCanvas canvas, MapParams mapParams, MapConfig cfg, WorldBounds worldBounds)
        {
            if (!Config.ShowMapMarkers)
                return;
            var mapId = Memory.MapID;
            if (string.IsNullOrEmpty(mapId))
                return;

            foreach (var m in MapMarkerManager.All)
            {
                if (!m.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var world = m.Position;
                if (!worldBounds.Contains(world))
                    continue;
                var sp = mapParams.ToScreenPos(MapParams.ToMapPos(world, cfg));
                DrawMarkerGlyph(canvas, sp, m);
            }
        }

        private static void DrawMarkerGlyph(SKCanvas canvas, SKPoint p, MapMarker m)
        {
            if (!SKColor.TryParse(m.Color, out var color))
                color = new SKColor(255, 179, 0);

            float x = p.X, y = p.Y;

            // Shared markers get an outer ring so they read differently from local ones.
            if (m.Shared)
            {
                _markerRingPaint.Color = color.WithAlpha(170);
                canvas.DrawCircle(x, y, 8.5f, _markerRingPaint);
            }

            // Diamond body
            const float s = 5.5f;
            using (var path = new SKPath())
            {
                path.MoveTo(x, y - s);
                path.LineTo(x + s, y);
                path.LineTo(x, y + s);
                path.LineTo(x - s, y);
                path.Close();

                _markerFillPaint.Color = color;
                canvas.DrawPath(path, _markerFillPaint);
                canvas.DrawPath(path, SKPaints.ShapeBorder);
            }

            if (!string.IsNullOrEmpty(m.Label))
            {
                float lx = x + 9f, ly = y + 4f;
                canvas.DrawText(m.Label, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
                _markerTextPaint.Color = color;
                canvas.DrawText(m.Label, lx, ly, SKPaints.FontRegular11, _markerTextPaint);
            }
        }

        #endregion

        #region Hit-testing

        /// <summary>
        /// Finds the closest marker (on the current map) within <paramref name="radius"/>
        /// scene-space pixels of <paramref name="scenePos"/>.
        /// </summary>
        private static bool TryFindMarkerNear(MapParams mp, Vector2 scenePos, float radius, out MapMarker? closest)
        {
            closest = null;
            var mapId = Memory.MapID;
            if (string.IsNullOrEmpty(mapId))
                return false;

            float best = radius;
            foreach (var m in MapMarkerManager.All)
            {
                if (!m.MapId.Equals(mapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var sp = mp.ToScreenPos(MapParams.ToMapPos(m.Position, mp.Config));
                float d = Vector2.Distance(new Vector2(sp.X, sp.Y), scenePos);
                if (d < best)
                {
                    best = d;
                    closest = m;
                }
            }
            return closest is not null;
        }

        #endregion

        #region Right-click placement

        /// <summary>
        /// Handles a right-click on the radar canvas: edits the marker under the cursor if
        /// there is one, otherwise opens the "new marker" popup at the clicked world point.
        /// <paramref name="rawPos"/> is the raw window-pixel mouse position.
        /// </summary>
        private static void HandleMarkerRightClick(Vector2 rawPos)
        {
            // Don't place markers when the click is over an ImGui panel/popup.
            if (ImGui.GetIO().WantCaptureMouse)
                return;

            var curParams = GetCurrentMapParams();
            if (curParams is null)
                return;
            var mp = curParams.Value;

            float scale = UIScale;
            var scene = new Vector2(rawPos.X / scale, rawPos.Y / scale);

            _markerPopupScreenPos = rawPos;

            if (TryFindMarkerNear(mp, scene, MarkerHitRadius, out var existing) && existing is not null)
            {
                BeginEditMarker(existing);
            }
            else
            {
                float worldY = LocalPlayer?.Position.Y ?? 0f;
                var mapPos = mp.ScreenToMapPos(new SKPoint(scene.X, scene.Y));
                var world = MapParams.MapPosToWorld(mapPos, mp.Config, worldY);
                BeginNewMarker(world);
            }
        }

        private static void BeginNewMarker(Vector3 world)
        {
            _markerEditMode = false;
            _markerEditId = null;
            _markerNewWorld = world;
            SetLabelBuffer("");
            _markerColorHex = MarkerPalette[1];
            _markerShared = false;
            _markerPopupOpenRequested = true;
        }

        private static void BeginEditMarker(MapMarker m)
        {
            _markerEditMode = true;
            _markerEditId = m.Id;
            SetLabelBuffer(m.Label);
            _markerColorHex = m.Color;
            _markerShared = m.Shared;
            _markerPopupOpenRequested = true;
        }

        #endregion

        #region Editor popup

        /// <summary>
        /// Draws the marker create/edit popup. Called every frame from <c>DrawWindows</c>
        /// so <c>BeginPopup</c> can latch the deferred open request.
        /// </summary>
        private static void DrawMarkerEditorPopup()
        {
            const string PopupId = "##MapMarkerEditor";

            if (_markerPopupOpenRequested)
            {
                _markerPopupOpenRequested = false;
                ImGui.SetNextWindowPos(_markerPopupScreenPos, ImGuiCond.Appearing);
                ImGui.OpenPopup(PopupId);
            }

            if (!ImGui.BeginPopup(PopupId))
                return;

            ImGui.TextDisabled(_markerEditMode ? "Edit Marker" : "New Marker");
            ImGui.Separator();

            ImGui.SetNextItemWidth(220f);
            ImGui.InputText("Label", _markerLabelBuf, (uint)_markerLabelBuf.Length);

            ImGui.Spacing();
            ImGui.TextDisabled("Color");
            for (int i = 0; i < MarkerPalette.Length; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                var hex = MarkerPalette[i];
                bool selected = string.Equals(hex, _markerColorHex, StringComparison.OrdinalIgnoreCase);
                var flags = ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop
                    | (selected ? ImGuiColorEditFlags.None : ImGuiColorEditFlags.NoBorder);
                if (ImGui.ColorButton($"##mk{i}", HexToVec4(hex), flags, new Vector2(22f, 22f)))
                    _markerColorHex = hex;
            }

            ImGui.Spacing();
            bool shared = _markerShared;
            if (ImGui.Checkbox("Shared (visible to web buddies)", ref shared))
                _markerShared = shared;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Local markers stay on this radar only.\nShared markers are broadcast to everyone on the web radar.");

            ImGui.Separator();

            string label = ReadLabelBuffer();
            string mapId = Memory.MapID ?? "";

            if (_markerEditMode)
            {
                if (ImGui.Button("Save") && _markerEditId is not null)
                {
                    MapMarkerManager.Update(_markerEditId, label, _markerColorHex, _markerShared);
                    ToastManager.Success("Marker updated");
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Delete") && _markerEditId is not null)
                {
                    MapMarkerManager.Remove(_markerEditId);
                    ToastManager.Info("Marker deleted");
                    ImGui.CloseCurrentPopup();
                }
            }
            else
            {
                if (ImGui.Button("Add"))
                {
                    MapMarkerManager.Add(mapId, _markerNewWorld, label, _markerColorHex, _markerShared, createdBy: "host");
                    ToastManager.Success(_markerShared ? "Shared marker added" : "Marker added");
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        #endregion

        #region Helpers

        private static void SetLabelBuffer(string text)
        {
            Array.Clear(_markerLabelBuf);
            if (string.IsNullOrEmpty(text))
                return;
            var bytes = Encoding.UTF8.GetBytes(text);
            int n = Math.Min(bytes.Length, _markerLabelBuf.Length - 1);
            Array.Copy(bytes, _markerLabelBuf, n);
        }

        private static string ReadLabelBuffer()
        {
            int len = Array.IndexOf(_markerLabelBuf, (byte)0);
            if (len < 0)
                len = _markerLabelBuf.Length;
            return Encoding.UTF8.GetString(_markerLabelBuf, 0, len);
        }

        private static Vector4 HexToVec4(string hex)
        {
            if (SKColor.TryParse(hex, out var c))
                return new Vector4(c.Red / 255f, c.Green / 255f, c.Blue / 255f, 1f);
            return new Vector4(1f, 0.7f, 0f, 1f);
        }

        #endregion
    }
}
