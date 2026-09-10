using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// Takes a dead enemy out of the fight before its body is freed: velocity to zero, shapes off,
    /// hitboxes off, behaviour off. Without it a corpse keeps sliding, keeps blocking and keeps hitting
    /// on the frames between the killing blow and the delayed free.
    /// </summary>
    /// <remarks>
    /// Unity put this next to <c>Health</c> on the enemy GameObject. Godot has no components, so it is a
    /// child node of the enemy and <c>Health</c> is its sibling - which is why every lookup starts at
    /// <see cref="Actor"/> rather than at <c>this</c>.
    /// </remarks>
    public sealed partial class EnemyDeathCleanup : Node
    {
        [Export] private float destroyDelay;

        private Health _health;
        private bool _cleanupRequested;

        /// <summary>The enemy this cleanup belongs to - Unity's <c>gameObject</c>.</summary>
        private Node Actor => GetParent() ?? this;

        public override void _EnterTree()
        {
            _health ??= Actor.FindComponent<Health>();

            if (_health != null)
            {
                _health.OnHealthDepleted += HandleDeath;
            }
        }

        public override void _ExitTree()
        {
            if (_health != null)
            {
                _health.OnHealthDepleted -= HandleDeath;
            }
        }

        private void HandleDeath()
        {
            CleanupNow();
        }

        public void CleanupNow()
        {
            if (_cleanupRequested)
            {
                return;
            }

            _cleanupRequested = true;
            StopPhysics();
            DisableColliders();
            DisableHitboxes();
            DisableEnemyBehaviours();

            if (!Engine.IsEditorHint())
            {
                FreeActorAfterDelay();
            }
        }

        /// <summary>
        /// Unity's <c>Destroy(gameObject, destroyDelay)</c>. A zero delay still goes through the timer so
        /// the body is not freed out from under the frame that killed it.
        /// </summary>
        private async void FreeActorAfterDelay()
        {
            Node actor = Actor;

            if (destroyDelay > 0f)
            {
                await ToSignal(GetTree().CreateTimer(destroyDelay), SceneTreeTimer.SignalName.Timeout);
            }

            if (GodotObject.IsInstanceValid(actor))
            {
                actor.QueueFree();
            }
        }

        /// <summary>
        /// Unity zeroed the Rigidbody2D, made it kinematic and switched simulation off. A
        /// CharacterBody2D is already kinematic and moves only when something drives it, so stopping it
        /// is stopping its physics step.
        /// </summary>
        private void StopPhysics()
        {
            if (Actor is CharacterBody2D body)
            {
                body.Velocity = Vector2.Zero;
                body.SetPhysicsProcess(false);
            }
        }

        private void DisableColliders()
        {
            foreach (CollisionShape2D shape in Actor.FindComponentsInChildren<CollisionShape2D>())
            {
                // Deferred: a shape cannot be switched off from inside the physics callback that is
                // reporting the hit which killed this enemy.
                shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
            }
        }

        private void DisableHitboxes()
        {
            foreach (DamageHitbox2D hitbox in Actor.FindComponentsInChildren<DamageHitbox2D>())
            {
                hitbox.SetActive(false);
            }
        }

        private void DisableEnemyBehaviours()
        {
            foreach (EnemyStateMachine behaviour in Actor.FindComponentsInChildren<EnemyStateMachine>())
            {
                behaviour.SetProcess(false);
                behaviour.SetPhysicsProcess(false);
            }
        }
    }
}
