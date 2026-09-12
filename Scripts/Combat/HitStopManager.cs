using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    public partial class HitStopManager : Node
    {
        public static HitStopManager Instance { get; private set; }

        private float _pauseTimer;
        // Multiplier, authored in CombatTuning.json as hitStopPauseScale. Unitless - not scaled.
        private float _pauseScale = CombatTuningData.Shared.hitStopPauseScale;
        private float _normalTimeScale = 1f;
        private bool _active;
        private bool _suppress;

        /// <summary>
        /// Turned on by whoever owns a hard freeze (pause menu, victory) for as long as it holds the
        /// timescale. Hit stop then writes nothing to <see cref="GameClock.TimeScale"/>, so a hit landing
        /// on the same frame as the freeze - live combat can do that right up to the victory frame -
        /// cannot thaw it. Instance state, not static: a static flag would outlive the scene reload and
        /// leave the next run with hit stop silently dead.
        /// </summary>
        public bool Suppress
        {
            get => _suppress;
            set
            {
                if (_suppress == value)
                    return;

                _suppress = value;

                if (!_suppress)
                    return;

                // Hand the timescale over clean: drop the episode but leave GameClock.TimeScale alone -
                // the freeze owner is about to write, or has already written, its own.
                _pauseTimer = 0f;
                _active = false;
            }
        }

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }
            Instance = this;

            // Engine.TimeScale scales the frame delta, so the countdown has to run off the unscaled
            // clock - and _Process must keep ticking through a paused tree to end the episode at all.
            ProcessMode = ProcessModeEnum.Always;
        }

        public void TriggerHitStop(float duration)
        {
            if (_suppress) return;
            if (!GraphicsOptions.HitStop) return;

            _pauseTimer = Mathf.Max(_pauseTimer, duration);
        }

        public override void _Process(double delta)
        {
            if (_suppress)
            {
                _pauseTimer = 0f;
                return;
            }

            if (_pauseTimer > 0f)
            {
                _pauseTimer -= GameClock.UnscaledDeltaTime;
                _active = true;
                GameClock.TimeScale = _pauseScale;
            }
            else if (_active)
            {
                // Restore once, on the frame the episode ends. Writing this every frame is what used to
                // stomp the pause and victory freezes back to 1 the frame after they were set.
                _active = false;
                GameClock.TimeScale = _normalTimeScale;
            }
        }

        public override void _ExitTree()
        {
            // A duplicate killed by the _Ready guard must not normalize the clock - it never owned it,
            // and a live pause or victory freeze may be holding the timescale at 0 right now.
            if (Instance != this)
                return;

            Instance = null;
            GameClock.TimeScale = 1f;
        }
    }
}
