using System;
using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// Holds one enemy as the player's focus, and pins the player's facing to it while it is held.
    ///
    /// In a 2D fight facing is the whole of aiming: every swing, the hitbox offset and the sprite flip
    /// all read <see cref="PlayerMotor2D.FacingDirection"/>, which follows the move input. That makes
    /// backing away from an enemy the same input as turning your back on it, so retreating and still
    /// swinging is impossible without this. Lock-on separates the two - move where you like, keep
    /// swinging at what you picked.
    ///
    /// Targets are found by collider on the Enemy layer and filtered by <see cref="Health"/>, so this
    /// never learns what an archetype is and stays inside the Player namespace's one dependency.
    /// </summary>
    public partial class PlayerLockOn : Node2D
    {
        /// <summary>How far the player can reach to grab a target, in pixels.</summary>
        [Export] private float lockOnRange = World.U(8f);

        /// <summary>How far a held target may drift before the lock drops. Wider than the grab range so a target sitting on the edge does not flicker.</summary>
        [Export] private float lockOnBreakRange = World.U(11f);

        /// <summary>Raised on every acquire and every drop, with null meaning "nothing held".</summary>
        public event Action<Node2D> OnTargetChanged;

        public Node2D Target => _target;
        public bool HasTarget => _target != null;
        public float LockOnRange => lockOnRange;

        private Node2D _target;
        private Health _targetHealth;
        private PlayerMotor2D _motor;
        private Health _health;

        public override void _Ready()
        {
            EnsureRefs();
        }

        /// <summary>
        /// Resolved on demand, not only in _Ready: a test runner drives this component without the scene
        /// tree, where _Ready never runs, and a null motor there would silently stop facing from
        /// following the target at all - a test that passes for the wrong reason.
        /// </summary>
        private void EnsureRefs()
        {
            _motor ??= this.GetComponentInParent<PlayerMotor2D>();
            _health ??= this.GetComponentInParent<Health>();
        }

        /// <summary>
        /// Unity cleared the lock in OnDisable, because pause and the victory panel both stop play by
        /// disabling the input receiver and a disabled lock-on would leave the facing override latched
        /// with nothing left to clear it. Godot has no enable/disable callback, so the leaving-the-tree
        /// case is covered here and a caller that parks the node instead is expected to call
        /// <see cref="Clear"/> itself.
        /// </summary>
        public override void _ExitTree()
        {
            Clear();
        }

        public void Configure(float range, float breakRange)
        {
            lockOnRange = range;
            lockOnBreakRange = breakRange;

            // A shrunk range applies to what is already held, not only to the next grab.
            if (_target != null && OutOfRange(_target))
            {
                Clear();
            }
        }

        /// <summary>Grab the nearest live enemy, or let go of the one already held.</summary>
        public void Toggle()
        {
            if (_target != null)
            {
                Clear();
                return;
            }

            Acquire();
        }

        public void Acquire()
        {
            EnsureRefs();

            Node2D nearest = null;
            float nearestSqr = float.MaxValue;
            Vector2 origin = GlobalPosition;

            List<GodotObject> hits = Phys2D.OverlapCircleAll(this, origin, lockOnRange, World.Layer.Enemy);
            foreach (GodotObject hit in hits)
            {
                // The hitbox a boss swings with is an area on the same body as its solid collider, so
                // without the live-Health filter the same enemy is found twice and a dead one is found
                // at all.
                var health = (hit as Node).GetComponentInParent<Health>();
                if (health == null || health.IsDead)
                {
                    continue;
                }

                if ((health as Node).GetComponentInParent<CharacterBody2D>() is not Node2D body)
                {
                    continue;
                }

                float sqr = (body.GlobalPosition - origin).LengthSquared();
                if (sqr >= nearestSqr)
                {
                    continue;
                }

                nearestSqr = sqr;
                nearest = body;
            }

            if (nearest == null)
            {
                return;
            }

            _target = nearest;
            _targetHealth = nearest.GetComponent<Health>();
            OnTargetChanged?.Invoke(_target);
        }

        public void Clear()
        {
            if (_target == null)
            {
                return;
            }

            _target = null;
            _targetHealth = null;
            _motor?.ClearFacingOverride();
            OnTargetChanged?.Invoke(null);
        }

        public override void _Process(double delta)
        {
            Tick();
        }

        /// <summary>Drops a lock that is no longer valid and points facing at one that is.</summary>
        public void Tick()
        {
            if (_target == null)
            {
                return;
            }

            EnsureRefs();

            // Dying holding a lock would keep the corpse turned at whatever killed it through the whole
            // spirit walk, and the respawn puts the enemy back somewhere else anyway.
            if (_health != null && _health.IsDead)
            {
                Clear();
                return;
            }

            // The target node itself is freed on death by EnemyDeathCleanup, so the validity check and
            // the IsDead check are both load-bearing: one covers the frames before cleanup runs.
            if (!IsInstanceValid(_target) || _targetHealth == null || !IsInstanceValid(_targetHealth)
                || _targetHealth.IsDead || OutOfRange(_target))
            {
                Clear();
                return;
            }

            float dx = _target.GlobalPosition.X - GlobalPosition.X;

            // Dead centre has no side to face. Holding the last facing beats picking one, which would
            // make the player spin every frame an enemy stands exactly on top of them.
            if (Mathf.Abs(dx) > World.U(0.01f))
            {
                _motor?.SetFacingOverride(dx);
            }
        }

        private bool OutOfRange(Node2D target)
        {
            return (target.GlobalPosition - GlobalPosition).LengthSquared()
                   > lockOnBreakRange * lockOnBreakRange;
        }
    }
}
