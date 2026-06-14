// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using System.Numerics;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Shell
{
    /// <summary>
    /// Lightweight first-run welcome tour. Shows a short series of card-style
    /// overlays explaining the main UX entry points (sidebar, status bar,
    /// on-map interactions, preset selector). Driven entirely by ImGui —
    /// no Skia, no input grab, no impact on the radar render path.
    ///
    /// Steps are versioned: new installs see the full tour once, while users who
    /// already finished an earlier tour get a short "What's new" pass showing only
    /// the steps added since (tracked via <see cref="SilkConfig.TourVersionSeen"/>).
    /// </summary>
    internal static class FirstRunTour
    {
        /// <summary>
        /// Bump when adding steps so existing users get a "What's new" pass.
        /// Tag the new steps with <c>AddedIn = TourVersion</c>.
        /// </summary>
        private const int TourVersion = 2;

        public static bool IsOpen { get; private set; }

        private static int _step;
        private static bool _autoTriggered;

        private sealed record Step(string Title, string Body, string? Tip, int AddedIn = 1);

        /// <summary>Steps shown this session — full list, or just the new ones in What's-new mode.</summary>
        private static IReadOnlyList<Step> _activeSteps = [];

        private static readonly Step[] _steps =
        [
            new(
                "Welcome to the radar",
                "Quick tour of the layout and the latest features. You can skip any time — " +
                "the radar still works the way you remember.\n\n" +
                "Players, their position, and aim direction are the radar's primary signal — " +
                "everything else hangs off that.",
                "Press → / Enter to continue, Esc to skip."),
            new(
                "Left sidebar — your main controls",
                "The icon column on the left toggles the big panels:\n" +
                "  P  Players      [1]\n" +
                "  L  Loot         [2]\n" +
                "  A  Aimview      [3]\n" +
                "  Q  Quests       [4]\n" +
                "  S  Settings     [5]\n" +
                "  *  ESP overlay  [E]\n\n" +
                "Press Tab to hide / show the whole sidebar.",
                "Hover any icon to see its hotkey hint."),
            new(
                "Bottom status bar — at-a-glance vitals",
                "Big-chip readout designed for AnyDesk / TV viewing:\n" +
                "  STATUS   — In Raid / In Hideout\n" +
                "  PLAYERS  — total · teammates / PMC / scavs / AI breakdown\n" +
                "  VITALS   — energy / hydration, colored when low\n" +
                "  FPS  ·  DMA  ·  MAP   (right side)\n\n" +
                "Click the v / ^ chevron to collapse the bar entirely.",
                "Players are split T (teammate) / P (PMC) / S (player scav) / AI."),
            new(
                "Work directly on the map",
                "The radar canvas itself is interactive:\n\n" +
                "  Right-click          — place / edit a map marker (label, color, shared)\n" +
                "  Shift + Left-click   — mark a player as your teammate. Saved by their\n" +
                "                         account ID and re-applied automatically every raid.\n" +
                "                         Manage saved marks in Settings → Teammates.\n" +
                "  Drag                 — the kill feed and player counter overlays move\n" +
                "                         wherever you drop them.",
                "Hover any dot on the radar for a detail tooltip.",
                AddedIn: 2),
            new(
                "Presets — switch a radar config in one click",
                "The preset combo in the top menu bar bundles every radar-layer + " +
                "player-display toggle into named profiles:\n" +
                "  Stealth  ·  Loot Run  ·  PvP  ·  Quests  ·  Custom\n\n" +
                "Bind the Previous / Next Preset hotkeys in the Hotkeys panel to cycle them " +
                "from your second keyboard.",
                "Drift from a built-in preset auto-flips you to Custom. That's it — happy hunting."),
        ];

        /// <summary>
        /// Open the full tour from the start — first run, or via the
        /// "Show Welcome Tour" button in Settings → General.
        /// </summary>
        public static void Open()
        {
            IsOpen = true;
            _step = 0;
            _activeSteps = _steps;
        }

        /// <summary>
        /// Open only the steps added after <paramref name="sinceVersion"/> — the
        /// "What's new" pass for users who already finished an earlier tour.
        /// </summary>
        private static void OpenWhatsNew(int sinceVersion)
        {
            var fresh = new List<Step>();
            foreach (var s in _steps)
                if (s.AddedIn > sinceVersion)
                    fresh.Add(s);

            if (fresh.Count == 0)
            {
                Finish(); // nothing new to show — just persist the version bump
                return;
            }

            IsOpen = true;
            _step = 0;
            _activeSteps = fresh;
        }

        /// <summary>Close the tour without marking it complete (so it can reappear).</summary>
        public static void Close()
        {
            IsOpen = false;
            _step = 0;
        }

        /// <summary>
        /// Close the tour and persist the seen version so it never auto-opens
        /// again until new steps are added.
        /// </summary>
        public static void Finish()
        {
            IsOpen = false;
            _step = 0;
            var cfg = SilkProgram.Config;
            if (!cfg.FirstRunTourCompleted || cfg.TourVersionSeen < TourVersion)
            {
                cfg.FirstRunTourCompleted = true;
                cfg.TourVersionSeen = TourVersion;
                cfg.MarkDirty();
            }
        }

        /// <summary>
        /// Call once per frame. Auto-opens the tour on first run, then draws it
        /// while open. No-op once dismissed.
        /// </summary>
        public static void Draw()
        {
            // Auto-open on first run, but only after the radar has had a frame to settle —
            // we wait for the menu bar to be present so layout numbers are valid.
            if (!_autoTriggered)
            {
                _autoTriggered = true;
                var cfg = SilkProgram.Config;

                // Pre-versioning installs only have the bool — treat "completed" as v1 seen.
                int seen = cfg.TourVersionSeen;
                if (seen == 0 && cfg.FirstRunTourCompleted)
                    seen = 1;

                if (seen == 0)
                    Open();                 // brand-new install: full tour
                else if (seen < TourVersion)
                    OpenWhatsNew(seen);     // returning user: only the new cards
            }

            if (!IsOpen || _activeSteps.Count == 0)
                return;

            // Esc anywhere skips.
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                Finish();
                return;
            }

            var io = ImGui.GetIO();
            var viewport = ImGui.GetMainViewport();
            float scale = SilkProgram.Config.UIScale;

            // Dim full-screen backdrop.
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.SetNextWindowSize(io.DisplaySize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.55f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            const ImGuiWindowFlags backdropFlags = ImGuiWindowFlags.NoTitleBar
                                                   | ImGuiWindowFlags.NoResize
                                                   | ImGuiWindowFlags.NoMove
                                                   | ImGuiWindowFlags.NoScrollbar
                                                   | ImGuiWindowFlags.NoSavedSettings
                                                   | ImGuiWindowFlags.NoInputs
                                                   | ImGuiWindowFlags.NoFocusOnAppearing
                                                   | ImGuiWindowFlags.NoNav
                                                   | ImGuiWindowFlags.NoBringToFrontOnFocus;

            if (ImGui.Begin("##tour_backdrop", backdropFlags))
            {
                // Empty — just paints the dim layer.
            }
            ImGui.End();
            ImGui.PopStyleVar(3);

            // Foreground card.
            int idx = Math.Clamp(_step, 0, _activeSteps.Count - 1);
            var step = _activeSteps[idx];
            bool whatsNew = _activeSteps.Count != _steps.Length;

            var cardSize = new Vector2(560f * scale, 320f * scale);
            var cardPos = viewport.Pos + (viewport.Size - cardSize) * 0.5f;

            ImGui.SetNextWindowPos(cardPos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(cardSize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.98f);

            const ImGuiWindowFlags cardFlags = ImGuiWindowFlags.NoTitleBar
                                                | ImGuiWindowFlags.NoResize
                                                | ImGuiWindowFlags.NoMove
                                                | ImGuiWindowFlags.NoCollapse
                                                | ImGuiWindowFlags.NoSavedSettings;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f * scale, 18f * scale));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);

            if (ImGui.Begin("##tour_card", cardFlags))
            {
                // Step counter (top-right), with a "What's new" badge in update mode.
                string counter = whatsNew
                    ? $"What's new · {idx + 1} / {_activeSteps.Count}"
                    : $"{idx + 1} / {_activeSteps.Count}";
                float counterW = ImGui.CalcTextSize(counter).X;
                ImGui.SetCursorPosX(cardSize.X - counterW - 20f * scale);
                ImGui.TextColored(new Vector4(0.55f, 0.58f, 0.62f, 1f), counter);

                // Title — accented cyan, bold-feeling via larger spacing.
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.30f, 0.85f, 1.00f, 1f));
                ImGui.TextUnformatted(step.Title);
                ImGui.PopStyleColor();
                ImGui.Separator();
                ImGui.Spacing();

                // Body — wraps to card width.
                ImGui.PushTextWrapPos(cardSize.X - 40f * scale);
                ImGui.TextWrapped(step.Body);
                ImGui.PopTextWrapPos();

                // Spacer, then tip + buttons pinned to bottom.
                float footerH = ImGui.GetFrameHeightWithSpacing() + (step.Tip is null ? 0f : ImGui.GetTextLineHeightWithSpacing()) + 8f;
                float avail = ImGui.GetContentRegionAvail().Y;
                if (avail > footerH)
                    ImGui.Dummy(new Vector2(0, avail - footerH));

                if (step.Tip is not null)
                {
                    ImGui.TextColored(new Vector4(0.55f, 0.60f, 0.65f, 1f), step.Tip);
                    ImGui.Spacing();
                }

                // Buttons row: Skip (left) — Back / Next / Done (right).
                float btnH = 32f * scale;
                if (ImGui.Button("Skip", new Vector2(80f * scale, btnH)))
                {
                    Finish();
                    ImGui.End();
                    ImGui.PopStyleVar(2);
                    return;
                }

                bool hasBack = idx > 0;
                bool isLast = idx >= _activeSteps.Count - 1;

                float rightW = (isLast ? 110f : 90f) * scale + (hasBack ? (80f * scale + 8f) : 0f);
                ImGui.SameLine(cardSize.X - rightW - 20f * scale);

                if (hasBack)
                {
                    if (ImGui.Button("Back", new Vector2(80f * scale, btnH)))
                        _step = Math.Max(0, _step - 1);
                    ImGui.SameLine();
                }

                bool advance = false;
                if (isLast)
                {
                    if (ImGui.Button("Done", new Vector2(110f * scale, btnH)))
                        advance = true;
                }
                else
                {
                    if (ImGui.Button("Next  →", new Vector2(90f * scale, btnH)))
                        advance = true;
                }

                // Keyboard shortcuts: Right-arrow / Enter advance, Left-arrow goes back.
                if (!io.WantTextInput)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, false) || ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.Space, false))
                        advance = true;
                    if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false) && hasBack)
                        _step = Math.Max(0, _step - 1);
                }

                if (advance)
                {
                    if (isLast)
                        Finish();
                    else
                        _step = Math.Min(_activeSteps.Count - 1, _step + 1);
                }
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
        }
    }
}
