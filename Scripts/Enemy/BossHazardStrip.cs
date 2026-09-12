using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// A patch of ground an attack leaves behind, which hurts anyone standing in it until it burns out.
    /// Chapter two's Ember Pilgrim is what it exists for ([[RainbowChapterBossDesign]] 105): the lunge
    /// itself is answerable, and the strip it leaves is what turns the arena into something the player
    /// has to spend rather than stand in.
    ///
    /// It resolves through <see cref="CombatResolver"/> like every other source of damage, so dodge
    /// i-frames roll through it and the HUD reads it the same way it reads a swing. It owns no schedule
    /// of its own beyond its lifetime: the boss authors the numbers, this counts them down.
    /// </summary>
    /// <remarks>
    /// A guarded target is skipped rather than resolved. <see cref="CombatResolver"/> asks the guard
    /// before it looks at what is hitting, so a tick offered during a parry window came back published
    /// as a successful parry - which paid resonance and parry hit-stop for standing in fire, and the
    /// shipped tick interval and parry cooldown are both 0.5s, so it paid on every tick. Burning ground
    /// is not a swing and must not be answerable like one.
    ///
    /// ponytail: skipping means a held parry still negates the tick, it just earns nothing for it. The
    /// alternative is a hazard-shaped exception inside combat resolution, which is the one place in this
    /// codebase where every severe defect has lived. Revisit if a player can hold a parry through a
    /// whole strip; the cooldown says they cannot.
    ///
    /// The strip is a plain <see cref="Node2D"/> rather than an <see cref="Area2D"/>: it never waits to
    /// be entered, it sweeps on its own tick, which is exactly the Unity behaviour and is why the
    /// interval is the only clock it has.
    /// </remarks>
    public sealed partial class BossHazardStrip : Node2D
    {
        /// <summary>The one place a BossHazardStrip is described. Instanced by RainbowChapterBossBehaviour.</summary>
        public const string ScenePath = "res://Scenes/Effects/BossHazardStrip.tscn";

        private Node2D _owner;
        private float _damage = 6f;
        private float _radius = World.U(1.2f);
        private float _tickInterval = 0.5f;
        private float _tickTimer;
        private float _remaining;
        private bool _configured;

        /// <summary>Seconds of burn left. Zero on an unconfigured strip, which never ticks.</summary>
        public float Remaining => _remaining;

        /// <summary>How many times this strip has resolved damage. The proof a test can assert on.</summary>
        public int TickCount { get; private set; }

        /// <summary>Burn radius, in Godot pixels.</summary>
        public float Radius => _radius;

        /// <summary>
        /// Starts the burn. The first tick lands one interval in, not on the frame it appears: a strip
        /// dropped by a lunge that just connected would otherwise hit twice for the same commitment.
        /// </summary>
        /// <param name="radius">Already in pixels - <see cref="BossAttackProfile"/> converts at load.</param>
        public void Configure(Node2D owner, float damage, float radius, float tickInterval, float duration)
        {
            _owner = owner;
            _damage = Mathf.Max(0f, damage);
            _radius = Mathf.Max(0.01f, radius);
            _tickInterval = Mathf.Max(0.05f, tickInterval);
            _remaining = Mathf.Max(0f, duration);
            _tickTimer = _tickInterval;
            _configured = true;

            if (_remaining <= 0f)
            {
                QueueFree();
            }
        }

        public override void _Process(double delta)
        {
            if (!_configured)
            {
                return;
            }

            _remaining -= (float)delta;
            if (_remaining <= 0f)
            {
                QueueFree();
                return;
            }

            _tickTimer -= (float)delta;
            if (_tickTimer > 0f)
            {
                return;
            }

            _tickTimer = _tickInterval;
            Burn();
        }

        private void Burn()
        {
            var hit = false;

            foreach (GodotObject collider in Phys2D.OverlapCircleAll(this, GlobalPosition, _radius, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(collider, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                // Asked here rather than left to the resolver, which would publish the tick as a parried
                // swing and pay for it. Same lookup CombatResolver makes, so the answer is the same one.
                var guard = player.FindComponent<IDamageGuard>();
                if (guard != null && (guard.IsInParryWindow() || guard.IsInvulnerable()))
                {
                    continue;
                }

                var request = new DamageRequest(
                    GodotObject.IsInstanceValid(_owner) ? _owner : this,
                    player,
                    // Unity took Collider2D.ClosestPoint here; Godot's shape query has no equivalent, and
                    // the body centre is what every consumer of the hit point actually spawns effects on.
                    player.GlobalPosition,
                    // Straight up. A strip has no facing, and a sideways shove out of it would be a
                    // free escape from the thing the player is supposed to have to walk out of.
                    // Vector2.Up is already (0, -1) in Godot, so this needs no sign flip.
                    Vector2.Up,
                    _damage,
                    0f,
                    DamageType.Standard);

                DamageResult result = CombatResolver.Resolve(request);
                if (!result.Applied)
                {
                    continue;
                }

                hit = true;

                if (player.FindComponentInParent<CombatResultBroadcaster>() == null)
                {
                    player.FindComponent<CombatFeedback>()?.Play(result);
                }
            }

            if (hit)
            {
                TickCount++;
            }
        }
    }
}
