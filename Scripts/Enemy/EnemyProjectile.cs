using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// A caster's shot. It moves itself rather than riding the physics solver - the Unity version wrote
    /// <c>transform.position</c> every frame - so the body is a plain <see cref="Node2D"/> and only the
    /// contact test is a physics object: one <see cref="Area2D"/> child, authored in
    /// <see cref="ScenePath"/> along with its shape, its layer and its mask.
    ///
    /// UNITS: every distance below is in Godot pixels. The inline defaults are wrapped in
    /// <see cref="World.U"/>; the values <see cref="Initialize"/> is handed come from
    /// <see cref="RangedCasterData"/>, which converted them at load.
    /// </summary>
    public partial class EnemyProjectile : Node2D
    {
        /// <summary>
        /// The one place a shot is described. Instanced by <see cref="EnemyProjectilePool"/>, and by
        /// <see cref="RangedCaster"/> itself when nobody handed it a scene.
        /// </summary>
        public const string ScenePath = "res://Scenes/Effects/EnemyProjectile.tscn";

        private float speed = World.U(5f);
        private float damage = 8f;
        private float knockback = World.U(3f);
        private float lifetime = 5f;
        private float arcHeight = World.U(0.5f);

        private Vector2 _direction;
        private float _elapsedTime;
        private bool _isInitialized;
        private EnemyProjectilePool _pool;
        private Area2D _hitArea;

        internal void SetPool(EnemyProjectilePool pool)
        {
            _pool = pool;
        }

        public override void _Ready()
        {
            // HitArea, its CircleShape2D, its layer (EnemyHitbox) and its mask (Player | Ground) are
            // authored in EnemyProjectile.tscn. This used to build them per instance, because the
            // template the pool duplicated never entered the tree and so had none.
            _hitArea = this.FindComponent<Area2D>();

            if (_hitArea == null)
            {
                // A shot built with `new` rather than instanced. It still flies and still times out; it
                // simply cannot hit anything, which is what a body with no collider means everywhere else.
                return;
            }

            _hitArea.BodyEntered += OnHit;
            _hitArea.AreaEntered += OnHit;
        }

        /// <summary>
        /// Repaints the shot from <c>GameplayReadabilityDefaults</c>. The scene ships the colour the
        /// code defaults carry; this is how an override in <c>Resources/Art/Readability.json</c> still
        /// reaches a projectile now that there is no template left to dress.
        /// </summary>
        internal void SetAppearance(Color? tint, int? sortingOrder)
        {
            Sprite2D sprite = this.FindComponent<Sprite2D>();
            if (sprite == null)
            {
                return;
            }

            if (tint.HasValue)
            {
                sprite.Modulate = tint.Value;
            }

            if (sortingOrder.HasValue)
            {
                sprite.ZIndex = sortingOrder.Value;
            }
        }

        public void Initialize(Vector2 direction, float projectileSpeed, float projectileDamage, float projectileKnockback)
        {
            _direction = direction.Normalized();
            speed = projectileSpeed;
            damage = projectileDamage;
            knockback = projectileKnockback;
            _isInitialized = true;

            // Reset, not just set. A pooled projectile arrives carrying the flight time of its last
            // life, and the arc is a function of that clock - reused shots would start mid-arc.
            _elapsedTime = 0f;

            if (_hitArea != null)
            {
                _hitArea.SetDeferred(Area2D.PropertyName.Monitoring, true);
            }
        }

        /// <summary>
        /// Back to the pool if there is one, freed if there is not - which is the case for every
        /// projectile the test runners build by hand.
        /// </summary>
        private void Despawn()
        {
            _isInitialized = false;

            if (_hitArea != null)
            {
                _hitArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
            }

            if (_pool != null)
            {
                _pool.Release(this);
            }
            else
            {
                QueueFree();
            }
        }

        public override void _Process(double delta)
        {
            if (!_isInitialized)
            {
                return;
            }

            var dt = (float)delta;
            _elapsedTime += dt;

            // The lifetime used to be a Destroy(gameObject, lifetime) scheduled inside Initialize,
            // which stacked a second timed destroy every time one object was initialized twice - the
            // normal case once projectiles are reused. Counted here, so it resets with the shot.
            if (_elapsedTime >= lifetime)
            {
                Despawn();
                return;
            }

            // The arc phase was `elapsed * speed * 0.2` against a speed in metres per second. Speed is
            // now pixels per second, so the phase is divided back down - otherwise the shot would
            // oscillate a hundred times faster than it was authored to.
            float t = _elapsedTime * speed * 0.2f / World.Ppu;
            float yOffset = Mathf.Sin(t * Mathf.Pi) * arcHeight;

            Vector2 movement = _direction * speed * dt;

            // The arc lifts the shot. Unity added the offset to y because +Y was up there; Godot's +Y is
            // down, so the same lift is a subtraction.
            Position = new Vector2(
                Position.X + movement.X,
                Position.Y + movement.Y - (yOffset * dt * 5f));
        }

        private void OnHit(Node2D other)
        {
            // A pooled projectile sitting idle is disabled, so this cannot fire on one; a projectile
            // that already hit something this frame can still be brushed by a second collider before
            // the deactivation lands, and would resolve its damage twice.
            if (!_isInitialized)
            {
                return;
            }

            if (Phys2D.FindActorInGroup(other, World.Group.Player) is Node2D player)
            {
                var request = new DamageRequest(
                    this,
                    player,
                    // Unity used Collider2D.ClosestPoint; the projectile's own position is the contact
                    // point closely enough, and it is the one Godot can give without a second query.
                    GlobalPosition,
                    _direction,
                    damage,
                    knockback,
                    DamageType.Standard);

                CombatResolver.Resolve(request);
                Despawn();
            }
            else if (Phys2D.IsOnLayer(other, World.Layer.Ground))
            {
                Despawn();
            }
        }
    }
}
