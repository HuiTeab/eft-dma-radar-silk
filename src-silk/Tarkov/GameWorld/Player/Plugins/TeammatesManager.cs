// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// Manual teammate tagging. Flips a player's <see cref="Player.Type"/> between
    /// <see cref="PlayerType.Teammate"/> and their original classification, remembering the previous
    /// type so it can be restored on untag.
    ///
    /// <para>Marks are <b>persisted</b> via <see cref="Memory.Teammates"/> (keyed by Account/Profile id)
    /// so they survive restarts and re-raids. <see cref="Apply"/> re-flags saved teammates as soon as
    /// their id resolves during discovery / gear refresh.</para>
    /// </summary>
    internal static class TeammatesManager
    {
        /// <summary>
        /// Toggle the teammate flag on <paramref name="player"/> (the radar Shift+click action).
        /// Persists the change to <see cref="Memory.Teammates"/>.
        /// Returns the new state: <c>true</c> = now flagged as teammate, <c>false</c> = restored.
        /// </summary>
        public static bool Toggle(Player? player)
        {
            if (player is null || player.IsLocalPlayer || !player.IsAlive)
                return false;

            if (player.IsManualTeammate)
            {
                // Restore original classification and drop the saved mark.
                if (player.OriginalType is { } original)
                    player.Type = original;
                player.IsManualTeammate = false;
                player.OriginalType = null;
                Memory.Teammates.Remove(player.AccountId, player.ProfileId);
                Log.WriteLine($"[TeammatesManager] Removed teammate flag from '{player.Name}'.");
                return false;
            }

            // Flag as teammate.
            player.OriginalType ??= player.Type;
            player.Type = PlayerType.Teammate;
            player.IsManualTeammate = true;

            // Persist when we have at least one stable id. If neither is resolved yet
            // (just-discovered ally), Apply() will persist it once an id arrives.
            if (!string.IsNullOrEmpty(player.AccountId) || !string.IsNullOrEmpty(player.ProfileId))
                Memory.Teammates.Add(new TeammateEntry
                {
                    AccountId = player.AccountId ?? string.Empty,
                    ProfileId = player.ProfileId ?? string.Empty,
                    Name = player.Name ?? string.Empty,
                });

            Log.WriteLine($"[TeammatesManager] Flagged '{player.Name}' as teammate.");
            return true;
        }

        /// <summary>
        /// Applies the saved-teammate state to <paramref name="player"/>. Called at discovery and
        /// whenever an id resolves (gear refresh / dogtag cache). No-op unless the feature is enabled
        /// and one of the player's ids is on the saved list.
        /// </summary>
        public static void Apply(Player? player)
        {
            if (player is null || player.IsLocalPlayer)
                return;
            if (!SilkProgram.Config.TeammatesEnabled)
                return;

            bool hasId = !string.IsNullOrEmpty(player.AccountId) || !string.IsNullOrEmpty(player.ProfileId);
            if (!hasId)
                return;

            // Already flagged this session — make sure the mark is persisted now that an
            // id is available (covers marking a player before their id had resolved).
            if (player.IsManualTeammate)
            {
                if (!Memory.Teammates.Contains(player.AccountId, player.ProfileId))
                    Memory.Teammates.Add(new TeammateEntry
                    {
                        AccountId = player.AccountId ?? string.Empty,
                        ProfileId = player.ProfileId ?? string.Empty,
                        Name = player.Name ?? string.Empty,
                    });
                return;
            }

            if (player.Type == PlayerType.Teammate)
                return;

            if (Memory.Teammates.Contains(player.AccountId, player.ProfileId))
            {
                player.OriginalType ??= player.Type;
                player.Type = PlayerType.Teammate;
                player.IsManualTeammate = true;
                Log.WriteLine($"[TeammatesManager] Applied saved teammate to '{player.Name}'.");
                // Heads-up that a saved friend is in this raid (ToastManager is thread-safe).
                eft_dma_radar.Silk.UI.Shell.ToastManager.Info($"Teammate in raid: {player.Name}");
            }
        }

        /// <summary>
        /// Un-flags any currently-loaded player whose id matches and restores their original type.
        /// Used after a saved teammate is removed/cleared from the Settings tab so the change is
        /// reflected immediately without waiting for the next raid.
        /// </summary>
        public static void RestoreMatching(string? accountId, string? profileId)
        {
            var players = Memory.Players;
            if (players is null)
                return;

            foreach (var p in players)
            {
                if (p is null || !p.IsManualTeammate)
                    continue;

                bool match =
                    (!string.IsNullOrEmpty(accountId) && string.Equals(p.AccountId, accountId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(profileId) && string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
                if (!match)
                    continue;

                if (p.OriginalType is { } original)
                    p.Type = original;
                p.IsManualTeammate = false;
                p.OriginalType = null;
            }
        }

        /// <summary>
        /// Unflags all manually-tagged teammates in <paramref name="players"/> and restores their
        /// original <see cref="PlayerType"/>. In-memory only — saved marks are kept. Called on raid end.
        /// </summary>
        public static void Reset(IEnumerable<Player> players)
        {
            foreach (var p in players)
            {
                if (p is null || !p.IsManualTeammate)
                    continue;
                if (p.OriginalType is { } original)
                    p.Type = original;
                p.IsManualTeammate = false;
                p.OriginalType = null;
            }
        }
    }
}
