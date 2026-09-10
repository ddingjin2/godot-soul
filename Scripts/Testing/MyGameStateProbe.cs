using System.Collections.Generic;
using System.Reflection;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;
using UnityTestAgent.Observation;

namespace MyGame.Testing
{
    /// <summary>
    /// Reads the live game into an <see cref="AgentObservation"/> once a tick.
    /// </summary>
    /// <remarks>
    /// **UNITS.** Every position and velocity reported here is in Godot pixels with +Y down, because
    /// that is what the nodes hold. Nothing is converted back to metres: agents and scenario conditions
    /// compare probe values against each other far more often than against an authored number, and one
    /// conversion at the top would put every one of those comparisons a factor of 100 out. The three
    /// places an authored metre figure *is* compared against a probe value live in this file and go
    /// through <see cref="World.U"/>, marked below.
    ///
    /// **What had to be retargeted from Unity.**
    /// - <c>_player.transform.position</c>. <see cref="PlayerController2D"/> is a component node here,
    ///   not the actor: the body is <see cref="PlayerMotor2D"/>, its child-of-the-actor sibling, and
    ///   the body is what actually moves. Reading the controller's own <c>GlobalPosition</c> would
    ///   report the spawn point forever. See <see cref="PlayerBody"/>.
    /// - <c>SceneManager.GetActiveScene().name</c> is <see cref="GameplayBuildShim.ActiveSceneName"/>.
    /// - <c>enemy.GetComponent&lt;Health&gt;()</c> and friends are
    ///   <see cref="NodeExt.GetComponentInChildren{T}"/>: a Unity component on the enemy GameObject is a
    ///   child node of the enemy body here.
    /// - <c>SpriteRenderer.flipX</c> is <c>Sprite2D.FlipH</c>.
    /// - <c>projectile.activeInHierarchy</c> is <c>IsVisibleInTree()</c>; that is what
    ///   <see cref="EnemyProjectilePool"/> writes when it parks a shot.
    /// - <c>rb.linearVelocity</c> on a projectile has no counterpart: this port's
    ///   <see cref="EnemyProjectile"/> moves its own transform and exposes no velocity, so a projectile
    ///   that is not a physics body reports zero. Nothing in this folder reads that field.
    /// - <c>interactable.tag</c> is the node's first Godot group, tags having become groups.
    ///
    /// The reflection pokes at <c>_isAttacking</c> and the three telegraph timers survive intact - the
    /// ported archetypes kept those private field names, so the probe still reads a wind-up nothing
    /// exposes publicly.
    /// </remarks>
    public sealed class MyGameStateProbe : IGameStateProbe
    {
        /// <summary>Unity's 0.75 m, in pixels: probe positions are pixels, this is compared against them.</summary>
        private static readonly float CheckpointReachRadius = World.U(0.75f);

        /// <summary>Unity's 1.5 m "close enough to press the button", in pixels.</summary>
        private static readonly float InteractRadius = World.U(1.5f);

        private readonly PlayerController2D _player;
        private readonly IReadOnlyList<Node> _trackedEnemies;
        private readonly IReadOnlyList<Node> _trackedBosses;
        private readonly IReadOnlyList<Node> _trackedProjectiles;
        private readonly IReadOnlyList<Node> _trackedCheckpoints;
        private readonly IReadOnlyList<Node> _trackedInteractables;

        public MyGameStateProbe(PlayerController2D player, IReadOnlyList<Node> trackedEnemies)
            : this(player, trackedEnemies, null, null, null, null)
        {
        }

        public MyGameStateProbe(
            PlayerController2D player,
            IReadOnlyList<Node> trackedEnemies,
            IReadOnlyList<Node> trackedBosses,
            IReadOnlyList<Node> trackedProjectiles,
            IReadOnlyList<Node> trackedCheckpoints,
            IReadOnlyList<Node> trackedInteractables)
        {
            _player = player;
            _trackedEnemies = trackedEnemies;
            _trackedBosses = trackedBosses;
            _trackedProjectiles = trackedProjectiles;
            _trackedCheckpoints = trackedCheckpoints;
            _trackedInteractables = trackedInteractables;
        }

        public AgentObservation Capture()
        {
            var observation = new AgentObservation();
            observation.Scene.SceneName = GameplayBuildShim.ActiveSceneName;
            observation.Player = CapturePlayer();
            observation.Player.CheckpointId = CaptureReachedCheckpointId();

            if (_trackedEnemies != null)
            {
                for (var i = 0; i < _trackedEnemies.Count; i++)
                {
                    Node enemy = _trackedEnemies[i];
                    if (IsLive(enemy))
                        observation.Enemies.Add(CaptureEnemy(enemy));
                }
            }

            AddBosses(observation);
            AddProjectiles(observation);
            AddInteractables(observation);

            return observation;
        }

        /// <summary>
        /// The player's body. <see cref="PlayerController2D"/> forwards to the motor for velocity and
        /// grounding but not for position, so this is where the position has to come from.
        /// </summary>
        private Node2D PlayerBody =>
            (Node2D)_player.GetComponentInParent<PlayerMotor2D>() ?? _player;

        private PlayerObservation CapturePlayer()
        {
            var playerObservation = new PlayerObservation();
            if (!IsLive(_player))
            {
                playerObservation.IsAlive = false;
                return playerObservation;
            }

            var health = _player.GetComponentInParent<Health>();
            var stamina = _player.GetComponentInParent<StaminaSystem>();
            Vector2 position = PlayerBody.GlobalPosition;
            Vector2 velocity = _player.Velocity;

            playerObservation.Position = ToObservation(position);
            playerObservation.Velocity = ToObservation(velocity);
            playerObservation.FacingDirection = _player.FacingDir;
            playerObservation.HitPoints = RoundHealth(health != null ? health.CurrentHealth : 0f);
            playerObservation.MaxHitPoints = RoundHealth(health != null ? health.MaxHealth : 0f);
            playerObservation.Stamina = stamina != null ? stamina.CurrentStamina : 0f;
            playerObservation.MaxStamina = stamina != null ? stamina.MaxStamina : 0f;
            playerObservation.IsAlive = health == null || !health.IsDead;
            playerObservation.IsGrounded = _player.IsGrounded;
            playerObservation.IsInvulnerable = _player.IsInvulnerable();
            playerObservation.MovementState = _player.CurrentState.ToString();
            playerObservation.CombatState = CaptureCombatState(_player);
            return playerObservation;
        }

        private static EnemyObservation CaptureEnemy(Node enemy)
        {
            var health = enemy.GetComponentInChildren<Health>();
            // The enemy body usually *is* the state machine here, but GetComponentInChildren also
            // answers for a caller that handed over an actor root with the body underneath it.
            var stateMachine = enemy.GetComponentInChildren<EnemyStateMachine>();

            return new EnemyObservation
            {
                Id = enemy.Name,
                Position = ToObservation(NodePosition(enemy)),
                FacingDirection = CaptureFacingDirection(enemy),
                HitPoints = RoundHealth(health != null ? health.CurrentHealth : 0f),
                IsAlive = health == null || !health.IsDead,
                IsAttacking = ReadBoolField(enemy, "_isAttacking"),
                TelegraphState = CaptureEnemyTelegraphState(enemy, stateMachine)
            };
        }

        private void AddBosses(AgentObservation observation)
        {
            if (_trackedBosses == null)
                return;

            for (var i = 0; i < _trackedBosses.Count; i++)
            {
                Node boss = _trackedBosses[i];
                if (!IsLive(boss))
                    continue;

                observation.Bosses.Add(CaptureBoss(boss));
            }
        }

        private static BossObservation CaptureBoss(Node boss)
        {
            var health = boss.GetComponentInChildren<Health>();
            var wrath = boss.GetComponentInChildren<WrathMiniBoss>();
            var rainbow = boss.GetComponentInChildren<RainbowChapterBossBehaviour>();

            var observation = new BossObservation
            {
                Id = boss.Name,
                HitPoints = RoundHealth(health != null ? health.CurrentHealth : 0f),
                IsAlive = health == null || !health.IsDead
            };

            if (wrath != null)
            {
                observation.Phase = wrath.IsInPhaseTwo ? 2 : 1;
                observation.CurrentPattern = ReadEnumField(wrath, "_currentAttack");
                observation.TelegraphState = wrath.IsStunned ? "Stunned" : ReadBoolField(wrath, "_isAttacking") ? "Attacking" : "";
            }
            else if (rainbow != null)
            {
                observation.Id = rainbow.BossName;
                observation.Phase = rainbow.IsPhaseTwo ? 2 : 1;
                observation.CurrentPattern = rainbow.CurrentAttackProfile != null ? rainbow.CurrentAttackProfile.AttackId : "";
                observation.TelegraphState = rainbow.IsStunned ? "Stunned"
                    : rainbow.IsAttackActive ? "Active"
                    : rainbow.IsAttackRunning ? "Telegraph"
                    : "";
                observation.IsAlive = !rainbow.IsDefeated;
            }

            return observation;
        }

        private void AddProjectiles(AgentObservation observation)
        {
            if (_trackedProjectiles == null)
                return;

            for (var i = 0; i < _trackedProjectiles.Count; i++)
            {
                Node projectile = _trackedProjectiles[i];

                // A parked shot is pooled and waiting, not in flight. Reporting one would put a phantom
                // threat in the agent's observation at the caster's feet. EnemyProjectilePool parks a
                // shot by hiding it and disabling its process mode, so visibility is the live test.
                if (!IsLive(projectile) || !IsActiveInTree(projectile))
                    continue;

                observation.Projectiles.Add(new ProjectileObservation
                {
                    Id = projectile.Name,
                    Position = ToObservation(NodePosition(projectile)),
                    Velocity = ToObservation(BodyVelocity(projectile)),
                    IsHostile = projectile.GetComponentInChildren<EnemyProjectile>() != null
                });
            }
        }

        private void AddInteractables(AgentObservation observation)
        {
            if (_trackedInteractables == null)
                return;

            for (var i = 0; i < _trackedInteractables.Count; i++)
            {
                Node interactable = _trackedInteractables[i];
                if (!IsLive(interactable))
                    continue;

                Vector2 position = NodePosition(interactable);
                observation.Interactables.Add(new InteractableObservation
                {
                    Id = interactable.Name,
                    Position = ToObservation(position),
                    InteractableType = FirstGroup(interactable),
                    CanInteract = IsLive(_player) && PlayerBody.GlobalPosition.DistanceTo(position) <= InteractRadius
                });
            }
        }

        private static string CaptureCombatState(PlayerController2D player)
        {
            if (player.IsHeavyAttacking)
                return "HeavyAttack";
            if (player.IsAttacking)
                return "LightAttack";
            if (player.IsDodging)
                return "Dodge";
            if (player.IsParrying)
                return "Parry";
            return "";
        }

        private string CaptureReachedCheckpointId()
        {
            if (!IsLive(_player) || _trackedCheckpoints == null)
                return "";

            Vector2 playerPosition = PlayerBody.GlobalPosition;
            for (var i = 0; i < _trackedCheckpoints.Count; i++)
            {
                Node checkpointObject = _trackedCheckpoints[i];
                if (!IsLive(checkpointObject))
                    continue;

                var checkpoint = checkpointObject.GetComponentInChildren<Checkpoint>();
                if (checkpoint == null)
                    continue;

                if (playerPosition.DistanceTo(checkpoint.RespawnPosition) <= CheckpointReachRadius)
                    return checkpoint.CheckpointId;
            }

            return "";
        }

        private static string CaptureEnemyTelegraphState(Node enemy, EnemyStateMachine stateMachine)
        {
            if (stateMachine != null && stateMachine.CurrentState == EnemyState.Stunned)
                return "Stunned";

            if (ReadBoolField(enemy, "_isAttacking"))
                return ReadPositiveFloatField(enemy, "_telegraphTimer") || ReadPositiveFloatField(enemy, "_leapTelegraphTimer") || ReadPositiveFloatField(enemy, "_castTelegraphTimer")
                    ? "Telegraphing"
                    : "Attacking";

            return stateMachine != null ? stateMachine.CurrentState.ToString() : "";
        }

        private static float CaptureFacingDirection(Node enemy)
        {
            var sprite = enemy.GetComponentInChildren<Sprite2D>();
            if (sprite != null)
                return sprite.FlipH ? -1f : 1f;

            return 1f;
        }

        /// <summary>
        /// Unity looked at three components on the enemy GameObject; here the enemy body *is* the state
        /// machine, so the node itself and its child nodes are the same search.
        /// </summary>
        private static bool ReadBoolField(Node target, string fieldName)
        {
            if (ReadBoolField((object)target, fieldName))
                return true;

            foreach (Node child in target.GetChildren())
            {
                if (ReadBoolField((object)child, fieldName))
                    return true;
            }

            return false;
        }

        private static bool ReadBoolField(object target, string fieldName)
        {
            if (target == null)
                return false;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.FieldType == typeof(bool) && (bool)field.GetValue(target);
        }

        private static bool ReadPositiveFloatField(Node target, string fieldName)
        {
            if (ReadPositiveFloatField((object)target, fieldName))
                return true;

            foreach (Node child in target.GetChildren())
            {
                if (ReadPositiveFloatField((object)child, fieldName))
                    return true;
            }

            return false;
        }

        private static bool ReadPositiveFloatField(object target, string fieldName)
        {
            if (target == null)
                return false;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.FieldType == typeof(float) && (float)field.GetValue(target) > 0f;
        }

        private static string ReadEnumField(object target, string fieldName)
        {
            if (target == null)
                return "";

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            object value = field != null ? field.GetValue(target) : null;
            return value != null ? value.ToString() : "";
        }

        /// <summary>Unity's overloaded <c>== null</c>, which reported a destroyed object as null.</summary>
        private static bool IsLive(GodotObject node) => GodotObject.IsInstanceValid(node);

        /// <summary>
        /// Unity's <c>activeInHierarchy</c>. Godot splits "active" into visibility, process mode and
        /// collision (see <c>GameplayBuildShim.SetActive</c>); visibility is the half a pooled shot is
        /// parked with, and the only half that is cheap to ask about here.
        /// </summary>
        private static bool IsActiveInTree(Node node) =>
            node.IsInsideTree() && (node is not CanvasItem item || item.IsVisibleInTree());

        private static Vector2 NodePosition(Node node) =>
            node is Node2D node2D ? node2D.GlobalPosition : Vector2.Zero;

        /// <summary>Zero for anything that is not a physics body - see the units note on the class.</summary>
        private static Vector2 BodyVelocity(Node node) => node switch
        {
            CharacterBody2D body => body.Velocity,
            RigidBody2D body => body.LinearVelocity,
            _ => Vector2.Zero,
        };

        /// <summary>Unity's <c>GameObject.tag</c>. Tags became Godot groups in this port.</summary>
        private static string FirstGroup(Node node)
        {
            foreach (StringName group in node.GetGroups())
                return group.ToString();

            return "";
        }

        private static Vector2Observation ToObservation(Vector2 value)
        {
            return new Vector2Observation(value.X, value.Y);
        }

        private static int RoundHealth(float value)
        {
            return Mathf.RoundToInt(value);
        }
    }
}
