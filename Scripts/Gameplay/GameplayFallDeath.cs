using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The kill plane under the arena. Unity tested <c>position.y &lt; deathY</c> with a negative
    /// authored line; Godot's +Y points down, so the line is positive and the test flips to
    /// <c>&gt;</c>. Both changes happen once, in <see cref="Initialize"/> and in _Process.
    /// </summary>
    public partial class GameplayFallDeath : Node2D
    {
        /// <summary>
        /// The line, in Godot pixels (+Y down). Written by <see cref="Initialize"/> from the scene
        /// layout file; no initialiser since K7b, and a plane that never got the call reports itself.
        /// </summary>
        [Export] private float deathY;


        /// <summary>
        /// Seconds the plane refuses to fire again after a pit death. Authored as
        /// <c>WorldTuning.json.fallDeathRespawnLockout</c>; seconds on both sides, so nothing is scaled.
        /// Its own <c>_Ready</c> below reads the file, which is why this one needs no guard the way
        /// <see cref="deathY"/> does - and no initialiser either (K7b): a build with no
        /// <c>WorldTuning.json</c> has already been refused by <c>GameplayBootstrap</c>.
        /// </summary>
        [Export] private float respawnLockout;

        private Health _health;
        private float _lastFallDeathTime = -999f;
        private bool _configured;

        /// <summary>
        /// <paramref name="deathYValue"/> is already Godot space: <c>GameplaySceneDefaults.FallDeathY</c>
        /// turned the authored Unity -8 into +800 when it loaded, so nothing is converted here.
        /// </summary>
        public void Initialize(float deathYValue)
        {
            _configured = true;
            deathY = deathYValue;
        }

        public override void _Ready()
        {
            _health = this.GetComponentInParent<Health>();

            WorldTuningData world = GameplayTuningCatalog.Load()?.WorldTuning;
            if (world != null)
                respawnLockout = world.fallDeathRespawnLockout;

            CallDeferred(nameof(CheckConfigured));
        }

        private void CheckConfigured() => TuningGuard.Check(this, _configured, "Initialize");

        public override void _Process(double delta)
        {
            if (_health == null || _health.IsDead)
                return;

            // Below the line is a larger y in Godot.
            if (GlobalPosition.Y < deathY)
                return;

            if (GameClock.Time - _lastFallDeathTime < respawnLockout)
                return;

            _lastFallDeathTime = GameClock.Time;
            _health.ApplyDamage(_health.MaxHealth, Vector2.Up, 0f, true);
        }
    }
}
