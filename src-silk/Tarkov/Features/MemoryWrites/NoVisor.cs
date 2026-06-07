// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    /// <summary>
    /// Removes the helmet visor (face-shield) overlay by forcing the FPS camera's
    /// <c>VisorEffect.Intensity</c> to 0; restores it to 1 when disabled. The game re-applies the
    /// effect (e.g. on gear changes / respawn), so while active the live intensity is re-read each
    /// tick and the write re-issued whenever it drifts back off our target.
    /// </summary>
    public sealed class NoVisor : MemWriteFeature<NoVisor>
    {
        private const float VisorDisabled = 0f;
        private const float VisorEnabled  = 1f;

        private bool  _lastEnabledState;
        private ulong _cachedComponent;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.NoVisor;
            set => SilkProgram.Config.MemWrites.NoVisor = value;
        }

        /// <summary>Visor state is static — a slow re-check is enough to beat the game re-enabling it.</summary>
        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                // While active, re-read the live intensity so we notice the game restoring the visor
                // and re-issue the write below — otherwise _lastEnabledState would mask the drift.
                if (Enabled && _cachedComponent.IsValidVirtualAddress())
                {
                    float current = Memory.ReadValue<float>(_cachedComponent + Offsets.VisorEffect.Intensity, false);
                    _lastEnabledState = current == VisorDisabled;
                }

                if (Enabled == _lastEnabledState)
                    return;

                var comp = GetComponent(game);
                if (!comp.IsValidVirtualAddress())
                    return;

                float target = Enabled ? VisorDisabled : VisorEnabled;
                writes.AddValueEntry(comp + Offsets.VisorEffect.Intensity, target);
                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[NoVisor] {(Enabled ? "Enabled" : "Disabled")} (Intensity {target})");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[NoVisor]: {ex.Message}");
                _cachedComponent = default;
            }
        }

        private ulong GetComponent(LocalGameWorld game)
        {
            if (_cachedComponent.IsValidVirtualAddress())
                return _cachedComponent;

            var fps = game.CameraManager?.FPSCamera ?? 0ul;
            if (!fps.IsValidVirtualAddress()) return 0;

            var comp = GOM.GetComponentFromBehaviour(fps, "VisorEffect");
            if (comp.IsValidVirtualAddress())
                _cachedComponent = comp;
            return comp;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedComponent  = default;
        }

        public override void OnRaidEnd()
        {
            _lastEnabledState = default;
            _cachedComponent  = default;
        }
    }
}
