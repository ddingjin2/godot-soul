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
        /// <summary>The line, in Godot pixels (+Y down). Unity's authored -6 is +600 here.</summary>
        [Export] private float deathY = World.Ppu * 6f;


        [Export] private float respawnLockout = 1f;

        private Health _health;
        private float _lastFallDeathTime = -999f;

        /// <summary>
        /// <paramref name="deathYValue"/> is already Godot space: <c>GameplaySceneDefaults.FallDeathY</c>
        /// turned the authored Unity -8 into +800 when it loaded, so nothing is converted here.
        /// </summary>
        public void Initialize(float deathYValue)
        {
            deathY = deathYValue;
        }

        public override void _Ready()
        {
            _health = this.GetComponentInParent<Health>();
        }

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
