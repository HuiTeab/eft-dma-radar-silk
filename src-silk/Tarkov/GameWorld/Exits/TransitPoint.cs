// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// A transit point that moves the player between maps. Read from the TransitController dictionary.
    /// Name is static (read once); position is read from memory via the transform chain (falling back
    /// to static JSON map data if that fails); the <see cref="IsActive"/> flag is refreshed by
    /// <see cref="ExfilManager"/> since it flips when the transit opens (~1 min into a raid).
    /// </summary>
    internal sealed class TransitPoint
    {
        /// <summary>Display name (e.g. "Transit to Customs").</summary>
        public string Name { get; }

        /// <summary>World position — read from the live transform chain (same as doors/exfils), with static JSON as the initial fallback until the chain resolves.</summary>
        public Vector3 Position { get; private set; }

        /// <summary>True once <see cref="Position"/> has been read from live memory (vs. the static JSON fallback).</summary>
        public bool PositionResolved { get; private set; }

        /// <summary>Whether this transit is currently active (usable). Refreshed by <see cref="ExfilManager"/>.</summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// True once this transit has been observed open. In once-per-raid mode <see cref="ExfilManager"/>
        /// stops polling after this latches (transits stay open once available).
        /// </summary>
        public bool StatusSettled { get; private set; }

        /// <summary>Address of the transit's <c>active</c> flag — used by <see cref="ExfilManager"/> for scatter refresh.</summary>
        public ulong ActiveAddr { get; }

        /// <summary>Address of the transit object itself (the TransitController dictionary value) — root of the transform chain used to read <see cref="Position"/>.</summary>
        public ulong Base { get; }

        // Cached distance label — avoids per-frame string allocation + MeasureText
        private int _cachedDistVal = -1;
        private string _cachedDistText = "";
        private float _cachedDistWidth;

        public TransitPoint(ulong baseAddr, string mapId)
        {
            if (!Memory.TryReadPtr(baseAddr + Offsets.TransitPoint.parameters, out var parameters, false))
                throw new Exception("Failed to read transit parameters");

            Base = baseAddr;

            // Read destination location (map ID)
            string destinationLabel = "Unknown";
            if (Memory.TryReadPtr(parameters + Offsets.TransitParameters.location, out var locationPtr, false)
                && Memory.TryReadUnityString(locationPtr, out var location)
                && !string.IsNullOrWhiteSpace(location))
            {
                destinationLabel = MapNames.Names.TryGetValue(location, out var friendly)
                    ? friendly
                    : location;
            }

            Name = $"Transit to {destinationLabel}";

            // Position: prefer the live transform chain (scene variant — see TryResolvePositionFromMemory).
            // If it can't resolve this early in the raid, seed with static JSON; ExfilManager re-attempts
            // each refresh until it resolves, which also gives transits without JSON data (e.g. new maps
            // like Icebreaker) a live position.
            if (!TryResolvePositionFromMemory())
                Position = GetStaticPosition(mapId, destinationLabel);

            // Cache the active-flag address so ExfilManager can re-read it each tick, then seed the
            // initial value. Done last so the latch log (in SetActive) sees a valid Name/Position.
            ActiveAddr = parameters + Offsets.TransitParameters.active;
            SetActive(Memory.ReadValue<bool>(ActiveAddr, false));
        }

        /// <summary>Applies a freshly-read active flag from the <see cref="ExfilManager"/> refresh.</summary>
        public void Update(bool active) => SetActive(active);

        /// <summary>Sets the active flag and latches <see cref="StatusSettled"/> once the transit is observed open.</summary>
        private void SetActive(bool active)
        {
            IsActive = active;
            if (active && !StatusSettled)
            {
                StatusSettled = true;
                Log.WriteLine($"[TransitPoint] '{Name}' is now OPEN (status latched) @ {Position}");
            }
        }

        /// <summary>
        /// Attempts to read the transit's world position from live memory. Transits are scene-placed
        /// MonoBehaviours (like BTR path stops / sniper zones), so the short scene chain
        /// (<see cref="UnityOffsets.SceneTransformChain"/>) resolves them — the full 6-hop
        /// <see cref="UnityOffsets.TransformChain"/>'s managed tail returns null for these objects,
        /// which is why the position previously fell back to JSON. The scene chain is tried first,
        /// then the full chain as a safety net. On success updates <see cref="Position"/>, latches
        /// <see cref="PositionResolved"/>, and returns true. Safe to call repeatedly:
        /// <see cref="ExfilManager"/> retries each refresh in case the transform isn't ready yet.
        /// </summary>
        public bool TryResolvePositionFromMemory()
        {
            if (PositionResolved)
                return true;

            if (TryReadPositionVia(UnityOffsets.SceneTransformChain, out var pos)
                || TryReadPositionVia(UnityOffsets.TransformChain, out pos))
            {
                Position = pos;
                PositionResolved = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Walks <paramref name="chain"/> from <see cref="Base"/> to a TransformInternal and reads a
        /// world position from it. Returns false (and <see cref="Vector3.Zero"/>) if the chain can't
        /// resolve or the position is invalid, so the caller can try the next chain.
        /// </summary>
        private bool TryReadPositionVia(uint[] chain, out Vector3 pos)
        {
            pos = Vector3.Zero;
            try
            {
                if (Memory.TryReadPtrChain(Base, chain, out var transformInternal, false))
                {
                    var p = UnityOffsets.ReadWorldPosition(transformInternal);
                    if (p != Vector3.Zero && float.IsFinite(p.X))
                    {
                        pos = p;
                        return true;
                    }
                }
            }
            catch { /* not ready / wrong chain — caller tries the next one */ }

            return false;
        }

        /// <summary>
        /// Draws this transit point on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, Player.Player localPlayer)
        {
            var (fill, text) = IsActive
                ? (SKPaints.PaintTransit, SKPaints.TextTransit)
                : (SKPaints.PaintTransitInactive, SKPaints.TextTransitInactive);

            // Draw diamond marker
            const float s = 5f;
            float x = screenPos.X, y = screenPos.Y;
            using var path = new SKPath();
            path.MoveTo(x, y - s);
            path.LineTo(x + s, y);
            path.LineTo(x, y + s);
            path.LineTo(x - s, y);
            path.Close();

            canvas.DrawPath(path, SKPaints.ShapeBorder);
            canvas.DrawPath(path, fill);

            // Draw name label
            float lx = x + 7f;
            float ly = y + 4.5f;
            canvas.DrawText(Name, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(Name, lx, ly, SKPaints.FontRegular11, text);

            // Draw distance — cached to avoid per-frame string allocation + MeasureText
            int d = (int)Vector3.Distance(localPlayer.Position, Position);
            if (d != _cachedDistVal)
            {
                _cachedDistVal = d;
                _cachedDistText = $"{d}m";
                _cachedDistWidth = SKPaints.FontRegular11.MeasureText(_cachedDistText);
            }
            float dx = x - _cachedDistWidth / 2;
            float dy = y + 16f;
            canvas.DrawText(_cachedDistText, dx + 1, dy + 1, SKPaints.FontRegular11, SKPaints.TextShadow);
            canvas.DrawText(_cachedDistText, dx, dy, SKPaints.FontRegular11, text);
        }

        #region Static Position Lookup

        /// <summary>
        /// Resolves the transit position from the pre-loaded JSON map data.
        /// Matches by fuzzy description comparison (handles "The Labyrinth" vs "Labyrinth" etc.).
        /// </summary>
        private static Vector3 GetStaticPosition(string mapId, string destinationLabel)
        {
            if (string.IsNullOrEmpty(mapId) || !EftDataManager.MapData.TryGetValue(mapId, out var mapData))
                return new Vector3(0, -100, 0);

            if (mapData.Transits is not { Count: > 0 })
                return new Vector3(0, -100, 0);

            var searchTerm = NormalizeForComparison(destinationLabel);

            foreach (var t in mapData.Transits)
            {
                if (t.Description is null)
                    continue;

                var normalized = NormalizeForComparison(t.Description);

                if (normalized.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                    || searchTerm.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    if (t.Position is not null)
                        return t.Position.ToVector3();
                }
            }

            Log.Write(AppLogLevel.Debug,
                $"[TransitPoint] No matching transit for '{destinationLabel}' in map '{mapId}'");
            return new Vector3(0, -100, 0);
        }

        /// <summary>
        /// Normalizes a string for fuzzy comparison (removes "The ", "Transit to ", punctuation).
        /// </summary>
        private static string NormalizeForComparison(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("Transit to ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("The ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("?", "")
                .Replace("!", "")
                .Trim();
        }

        #endregion
    }
}
