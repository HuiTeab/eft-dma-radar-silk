// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;

using eft_dma_radar.Silk.Tarkov.GameWorld.Player;

namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Shared ImGui style tokens — colors, radii, border thicknesses, focus
    /// styling. Single source of truth so the desktop UI has one accent, one
    /// radius family, one border weight, and one focus style as called for in
    /// Phase 6 of the UX modernization plan.
    /// </summary>
    internal static class UITheme
    {
        // ── Accent (active state) ───────────────────────────────────────────
        /// <summary>The one and only "on" accent. Used by toggles, focus ring,
        /// active sidebar slot, top-bar pill on-state, etc.</summary>
        public static readonly Vector4 Accent       = new(0.00f, 0.80f, 0.80f, 1f);
        public static readonly Vector4 AccentSoft   = new(0.00f, 0.80f, 0.80f, 0.35f);
        public static readonly Vector4 AccentFaint  = new(0.00f, 0.80f, 0.80f, 0.18f);

        // ── Radii (one family, three sizes) ─────────────────────────────────
        /// <summary>Inner controls (steppers, small buttons, chips).</summary>
        public const float RadiusSmall  = 4f;
        /// <summary>Generic frames, toggle rows, pills.</summary>
        public const float RadiusMedium = 6f;
        /// <summary>Windows, popups, panels.</summary>
        public const float RadiusLarge  = 10f;

        // ── Border weights ──────────────────────────────────────────────────
        /// <summary>Thin border for sections / dividers.</summary>
        public const float BorderThin   = 1f;
        /// <summary>Default border for windows, popups, focus ring.</summary>
        public const float BorderDefault = 1.5f;
        /// <summary>Emphasis border for selected / focused elements.</summary>
        public const float BorderFocus  = 2f;

        // ── Focus ring color (matches Accent so kbd/gamepad nav matches
        // the same active-state language) ───────────────────────────────────
        public static readonly Vector4 FocusRing = Accent;

        // ── Status / semantic colors ────────────────────────────────────────
        public static readonly Vector4 Green   = new(0.30f, 0.69f, 0.31f, 1f);
        public static readonly Vector4 Red     = new(0.94f, 0.33f, 0.31f, 1f);
        public static readonly Vector4 Orange  = new(1.00f, 0.60f, 0.00f, 1f);
        public static readonly Vector4 Yellow  = new(1.00f, 0.84f, 0.00f, 1f);
        public static readonly Vector4 Gold    = new(1.00f, 0.84f, 0.00f, 1f);
        public static readonly Vector4 Cyan    = Accent;
        public static readonly Vector4 Blue    = new(0.40f, 0.60f, 1.00f, 1f);
        public static readonly Vector4 Magenta = new(0.85f, 0.44f, 0.84f, 1f);
        public static readonly Vector4 Kappa   = new(0.58f, 0.44f, 0.86f, 1f);
        public static readonly Vector4 White   = new(1f, 1f, 1f, 1f);

        // ── Neutrals ────────────────────────────────────────────────────────
        public static readonly Vector4 Grey    = new(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Vector4 Slate   = new(0.47f, 0.56f, 0.61f, 1f);
        public static readonly Vector4 Dim     = new(1f, 1f, 1f, 0.38f);

        // ── Accent (auto-save hint, highlight) ──────────────────────────────
        public static readonly Vector4 AccentGreen = new(0.55f, 0.75f, 0.55f, 1f);

        // ── Player-type colors ──────────────────────────────────────────────
        // Single source of truth for faction colors across the radar (SKPaints), the Skia ESP
        // window (EspPaints), and the ImGui aimview (AimviewWidget). Values mirror SKPaints'
        // Paint* colors byte-for-byte — keep them in sync if either side changes.
        public static readonly Vector4 PlayerTeammate    = new(0.314f, 0.863f, 0.314f, 1f); // 80,220,80
        public static readonly Vector4 PlayerUSEC        = new(0.902f, 0.235f, 0.235f, 1f); // 230,60,60
        public static readonly Vector4 PlayerBEAR        = new(0.275f, 0.510f, 0.902f, 1f); // 70,130,230
        public static readonly Vector4 PlayerPScav       = new(0.863f, 0.863f, 0.863f, 1f); // 220,220,220
        public static readonly Vector4 PlayerAIScav      = new(0.941f, 0.902f, 0.235f, 1f); // 240,230,60
        public static readonly Vector4 PlayerAIBoss      = new(0.902f, 0.196f, 0.902f, 1f); // 230,50,230
        public static readonly Vector4 PlayerAIRaider    = new(1.000f, 0.706f, 0.118f, 1f); // 255,180,30
        public static readonly Vector4 PlayerSpecial     = new(1.000f, 0.353f, 0.627f, 1f); // 255,90,160
        public static readonly Vector4 PlayerStreamer    = new(0.667f, 0.471f, 1.000f, 1f); // 170,120,255
        public static readonly Vector4 PlayerDefault     = new(0.94f, 0.90f, 0.24f, 1f);

        // ── Aimview / overlay ───────────────────────────────────────────────
        public static readonly Vector4 OverlayCrosshair  = new(1f, 1f, 1f, 0.40f);
        public static readonly Vector4 OverlayBackground = new(0f, 0f, 0f, 0.75f);
        public static readonly Vector4 OverlayDotOutline = new(0f, 0f, 0f, 0.60f);
        public static readonly Vector4 OverlayShadow     = new(0f, 0f, 0f, 0.80f);
        public static readonly Vector4 OverlayBorder     = new(0.4f, 0.4f, 0.4f, 0.60f);
        public static readonly Vector4 OverlayCorpse     = new(0.85f, 0.55f, 0.20f, 0.90f);
        public static readonly Vector4 OverlayContainer  = new(0.39f, 0.78f, 0.86f, 0.85f);
        public static readonly Vector4 OverlayLoot         = new(0.78f, 0.78f, 0.78f, 0.85f);
        public static readonly Vector4 OverlayLootImportant = new(0.20f, 1.0f, 0.20f, 1.0f);
        public static readonly Vector4 OverlayLootWishlist  = new(0.00f, 0.90f, 1.0f, 1.0f);
        public static Vector4 ForPlayerType(PlayerType type) => type switch
        {
            PlayerType.Teammate      => PlayerTeammate,
            PlayerType.USEC          => PlayerUSEC,
            PlayerType.BEAR          => PlayerBEAR,
            PlayerType.PScav         => PlayerPScav,
            PlayerType.AIScav        => PlayerAIScav,
            PlayerType.AIRaider      => PlayerAIRaider,
            PlayerType.AIBoss        => PlayerAIBoss,
            PlayerType.SpecialPlayer => PlayerSpecial,
            PlayerType.Streamer      => PlayerStreamer,
            _                        => PlayerDefault,
        };
    }
}
