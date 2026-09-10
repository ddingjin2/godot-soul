using System.Collections.Generic;
using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    /// <summary>
    /// Keeps a pack of enemies from stacking on the same pixel: a spacing push away from crowded
    /// neighbours and a weak alignment pull towards the pack's average velocity.
    ///
    /// The spacing distances and the force are authored in Unity metres and scaled to pixels here.
    /// </summary>
    public partial class EnemyGroupCombat : Node2D
    {
        [Export] private float preferredSpacing = World.Ppu * 2f;
        [Export] private float spacingForce = World.Ppu * 2f;
        [Export] private float spacingRadius = World.Ppu * 3f;
        [Export] private float alignmentForce = 0.5f;

        private CharacterBody2D _rb;
        private Node2D _player;
        private readonly List<GodotObject> _nearbyEnemies = new();
        private uint _enemyMask;

        public override void _Ready()
        {
            _rb = this.GetComponentInParent<CharacterBody2D>();
            _enemyMask = World.Layer.Enemy;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_rb == null) return;

            if (_player == null)
            {
                _player = GetTree().GetFirstNodeInGroup(World.Group.Player) as Node2D;
            }

            if (_player == null) return;

            ApplySpacingForce((float)delta);
            ApplyAlignmentForce((float)delta);
        }

        // Unity applied these through Rigidbody2D.AddForce(ForceMode2D.Force), which integrates
        // force/mass over the step. There is no force integrator on a CharacterBody2D, so the same
        // acceleration is added to Velocity by hand at unit mass.
        private void ApplySpacingForce(float delta)
        {
            _nearbyEnemies.Clear();
            List<GodotObject> hits = Phys2D.OverlapCircleAll(this, _rb.GlobalPosition, spacingRadius, _enemyMask);

            foreach (GodotObject hit in hits)
            {
                Node other = Phys2D.FindActorInGroup(hit, World.Group.Enemy);
                if (other == null || other == _rb) continue;
                if (other is not Node2D otherBody) continue;

                Vector2 offset = _rb.GlobalPosition - otherBody.GlobalPosition;
                float dist = offset.Length();
                if (dist < preferredSpacing && dist > World.U(0.01f))
                {
                    float force = (preferredSpacing - dist) / preferredSpacing * spacingForce;
                    _rb.Velocity += offset.Normalized() * force * delta;
                }
            }
        }

        private void ApplyAlignmentForce(float delta)
        {
            if (_nearbyEnemies.Count == 0)
                _nearbyEnemies.AddRange(Phys2D.OverlapCircleAll(this, _rb.GlobalPosition, spacingRadius, _enemyMask));

            Vector2 avgVelocity = Vector2.Zero;
            int count = 0;

            foreach (GodotObject hit in _nearbyEnemies)
            {
                Node other = Phys2D.FindActorInGroup(hit, World.Group.Enemy);
                if (other == null || other == _rb) continue;

                if (other is CharacterBody2D body)
                {
                    avgVelocity += body.Velocity;
                    count++;
                }
            }

            if (count > 0)
            {
                avgVelocity /= count;
                Vector2 alignForce = (avgVelocity - _rb.Velocity) * alignmentForce;
                _rb.Velocity += alignForce * delta;
            }
        }
    }
}
