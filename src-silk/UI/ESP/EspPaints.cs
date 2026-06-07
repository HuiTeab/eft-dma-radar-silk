// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.UI.ESP
{
    /// <summary>
    /// Cached SkiaSharp paint instances for ESP rendering.
    /// All instances are pre-allocated — never create paints in the render loop.
    /// </summary>
    internal static class EspPaints
    {
        #region Fonts

        public static SKFont FontName { get; } = new(CustomFonts.Regular, 12) { Subpixel = true };
        public static SKFont FontInfo { get; } = new(CustomFonts.Regular, 10) { Subpixel = true };
        public static SKFont FontLoot { get; } = new(CustomFonts.Regular, 10) { Subpixel = true };

        #endregion

        #region Text Shadow

        public static SKPaint TextShadow { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 200),
            IsStroke = false,
            IsAntialias = true,
        };

        #endregion

        #region Box Outline

        public static SKPaint BoxOutline { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            StrokeWidth = 3f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        #endregion

        #region Health Bar

        public static SKPaint HealthBarBg { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 140),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HealthGreen { get; } = new()
        {
            Color = new SKColor(50, 200, 50, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HealthYellow { get; } = new()
        {
            Color = new SKColor(220, 200, 50, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HealthRed { get; } = new()
        {
            Color = new SKColor(220, 50, 50, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        #endregion

        #region Player Type — Box + Text
        // Colors are sourced from UITheme.ForPlayerType — the shared faction palette used by the
        // radar (SKPaints) and the ImGui aimview — so all three overlays stay in agreement. Box
        // paints keep the historical 220 alpha; text paints are fully opaque.

        public static SKPaint BoxUSEC { get; } = MakeBoxPaint(PlayerType.USEC);
        public static SKPaint TextUSEC { get; } = MakeFillPaint(PlayerType.USEC);

        public static SKPaint BoxBEAR { get; } = MakeBoxPaint(PlayerType.BEAR);
        public static SKPaint TextBEAR { get; } = MakeFillPaint(PlayerType.BEAR);

        public static SKPaint BoxPScav { get; } = MakeBoxPaint(PlayerType.PScav);
        public static SKPaint TextPScav { get; } = MakeFillPaint(PlayerType.PScav);

        public static SKPaint BoxTeammate { get; } = MakeBoxPaint(PlayerType.Teammate);
        public static SKPaint TextTeammate { get; } = MakeFillPaint(PlayerType.Teammate);

        public static SKPaint BoxScav { get; } = MakeBoxPaint(PlayerType.AIScav);
        public static SKPaint TextScav { get; } = MakeFillPaint(PlayerType.AIScav);

        public static SKPaint BoxRaider { get; } = MakeBoxPaint(PlayerType.AIRaider);
        public static SKPaint TextRaider { get; } = MakeFillPaint(PlayerType.AIRaider);

        public static SKPaint BoxBoss { get; } = MakeBoxPaint(PlayerType.AIBoss);
        public static SKPaint TextBoss { get; } = MakeFillPaint(PlayerType.AIBoss);

        public static SKPaint BoxSpecial { get; } = MakeBoxPaint(PlayerType.SpecialPlayer);
        public static SKPaint TextSpecial { get; } = MakeFillPaint(PlayerType.SpecialPlayer);

        public static SKPaint BoxStreamer { get; } = MakeBoxPaint(PlayerType.Streamer);
        public static SKPaint TextStreamer { get; } = MakeFillPaint(PlayerType.Streamer);

        public static SKPaint BoxDefault { get; } = MakeBoxPaint(PlayerType.Default);
        public static SKPaint TextDefault { get; } = MakeFillPaint(PlayerType.Default);

        #endregion

        #region Bones

        public static SKPaint BoneLine { get; } = new()
        {
            Color = new SKColor(255, 255, 255, 200),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        #endregion

        #region Loot

        public static SKPaint TextLoot { get; } = MakeFillPaint(200, 200, 200, 210);
        public static SKPaint TextLootImportant { get; } = MakeFillPaint(50, 255, 50, 240);
        public static SKPaint TextLootWishlist { get; } = MakeFillPaint(0, 230, 255, 240);
        public static SKPaint TextLootQuest { get; } = MakeFillPaint(255, 200, 50, 240);

        #endregion

        #region Crosshair

        public static SKPaint Crosshair { get; } = new()
        {
            Color = new SKColor(255, 0, 0, 230),
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint CrosshairDot { get; } = new()
        {
            Color = new SKColor(255, 0, 0, 230),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        #endregion

        #region Energy / Hydration Bars

        public static SKPaint StatusBarBg { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint EnergyFill { get; } = new()
        {
            Color = new SKColor(255, 200, 40, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HydrationFill { get; } = new()
        {
            Color = new SKColor(60, 180, 255, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint StatusBarBorder { get; } = new()
        {
            Color = new SKColor(255, 255, 255, 160),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKFont FontBar { get; } = new(CustomFonts.Regular, 11) { Subpixel = true };

        public static SKPaint TextBar { get; } = MakeFillPaint(255, 255, 255, 240);

        #endregion

        #region Status Text

        public static SKFont FontStatus { get; } = new(CustomFonts.Regular, 14) { Subpixel = true };

        public static SKPaint TextStatus { get; } = MakeFillPaint(255, 220, 60, 240);

        #endregion

        #region Helpers

        /// <summary>
        /// Returns the (box, text) paint pair for a given player type.
        /// </summary>
        public static (SKPaint box, SKPaint text) GetPlayerPaints(PlayerType type) => type switch
        {
            PlayerType.Teammate      => (BoxTeammate, TextTeammate),
            PlayerType.USEC          => (BoxUSEC, TextUSEC),
            PlayerType.BEAR          => (BoxBEAR, TextBEAR),
            PlayerType.PScav         => (BoxPScav, TextPScav),
            PlayerType.AIScav        => (BoxScav, TextScav),
            PlayerType.AIRaider      => (BoxRaider, TextRaider),
            PlayerType.AIBoss        => (BoxBoss, TextBoss),
            PlayerType.SpecialPlayer => (BoxSpecial, TextSpecial),
            PlayerType.Streamer      => (BoxStreamer, TextStreamer),
            _                        => (BoxDefault, TextDefault),
        };

        private static SKPaint MakeBoxPaint(byte r, byte g, byte b, byte a = 220) => new()
        {
            Color = new SKColor(r, g, b, a),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        private static SKPaint MakeFillPaint(byte r, byte g, byte b, byte a = 255) => new()
        {
            Color = new SKColor(r, g, b, a),
            IsStroke = false,
            IsAntialias = true,
        };

        /// <summary>Box (stroke) paint for a player type, colored from the shared <see cref="UITheme"/> palette.</summary>
        private static SKPaint MakeBoxPaint(PlayerType type)
        {
            var c = ToSK(UITheme.ForPlayerType(type), 220);
            return MakeBoxPaint(c.Red, c.Green, c.Blue, c.Alpha);
        }

        /// <summary>Text (fill) paint for a player type, colored from the shared <see cref="UITheme"/> palette.</summary>
        private static SKPaint MakeFillPaint(PlayerType type)
        {
            var c = ToSK(UITheme.ForPlayerType(type), 255);
            return MakeFillPaint(c.Red, c.Green, c.Blue, c.Alpha);
        }

        /// <summary>Convert a normalized ImGui <see cref="Vector4"/> color to an <see cref="SKColor"/> with an explicit alpha.</summary>
        private static SKColor ToSK(Vector4 v, byte a) => new(
            (byte)Math.Clamp((int)MathF.Round(v.X * 255f), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(v.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(v.Z * 255f), 0, 255),
            a);

        #endregion
    }
}
