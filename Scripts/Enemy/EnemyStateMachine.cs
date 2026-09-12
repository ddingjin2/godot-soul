using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    public enum EnemyState
    {
        Idle,
        Patrol,
        Investigate,
        Combat,
        Recovery,
        Stunned,
        Dead
    }

    /// <summary>
    /// The sensing, engagement, staggering and death every enemy archetype shares. Each archetype adds
    /// its own attack loop on top and overrides the handlers it cares about.
    /// </summary>
    /// <remarks>
    /// Unity drove these bodies with a <c>Rigidbody2D</c> written every FixedUpdate; here the enemy
    /// <em>is</em> a <see cref="CharacterBody2D"/>, so <c>_rb.linearVelocity</c> is simply
    /// <see cref="CharacterBody2D.Velocity"/> and there is no separate body reference to keep in step.
    ///
    /// Gravity comes from here rather than from the engine. Unity's enemies rode
    /// <c>Physics2D.gravity</c> at <c>gravityScale = 1</c>; project.godot sets
    /// <c>physics/2d/default_gravity</c> to zero because every actor in this port supplies its own, so
    /// it is applied below in pixels and pointing at +Y, which is down in Godot. The number itself is
    /// authored - <c>WorldTuning.json.enemyGravity</c>, in metres, pushed in by <see cref="Configure"/>.
    /// </remarks>
    public abstract partial class EnemyStateMachine : CharacterBody2D, IStaggerable
    {
        // State Transitions. Authored in WorldTuning.json (enemyIdleToPatrolTime,
        // enemyInvestigateDuration, enemyRecoveryDuration, enemyDisengageDistance) and pushed in by
        // Configure, which is the ONLY source: K5 took the initialisers out, because a copy of the
        // shipped numbers here is the "code fallback" decision D1 forbids. An enemy that never had
        // Configure called on it now leashes at zero distance and flips state on the frame it changes
        // one - visibly broken rather than quietly running on numbers nobody authored. The three times
        // are seconds; disengageDistance is PIXELS - the JSON authors 8 metres.
        [Export] protected float idleToPatrolTime;
        [Export] protected float investigateDuration;
        [Export] protected float recoveryDuration;
        [Export] protected float disengageDistance;

        public event Action<EnemyState> OnStateChanged;
        public event Action OnEnteredCombat;
        public event Action OnExitedCombat;

        public EnemyState CurrentState => _currentState;

        protected EnemyState _currentState = EnemyState.Idle;
        protected float _stateTimer;
        protected Node2D _player;
        protected Sprite2D _sr;
        private Health _health;

        /// <summary>
        /// Unity's <c>Rigidbody2D.bodyType = Static</c> on a defeated boss: gravity and the move step
        /// both stop, and nothing can push the body around while the victory beat plays out.
        /// </summary>
        protected bool _bodyFrozen;

        /// <summary>
        /// Downward acceleration in <b>pixels</b> per second squared. Authored in metres as
        /// <c>WorldTuning.json.enemyGravity</c> and pushed in by <see cref="Configure"/>. No initialiser:
        /// the copy that used to be here was Unity's <c>Physics2D.gravity</c> magnitude written a second
        /// time, and an enemy nobody configured is a wiring fault, not an enemy that falls at a different
        /// rate (PLAN_CLOSEOUT D1/K5b).
        /// </summary>
        protected float gravity;

        /// <summary>
        /// Half-extents used for the footing probe when a body carries no collision shape at all - every
        /// test fixture. The shipped enemy bodies are 0.6 x 1.0 metres.
        /// </summary>
        private static readonly Vector2 FallbackHalfExtents = new Vector2(World.U(0.3f), World.U(0.5f));

        /// <summary>
        /// How far past the body's own edge the footing probe looks, and how far down it looks for a
        /// floor, in <b>pixels</b>. The reach is a little over a body width so the turn happens before
        /// the centre of mass is over the drop; the depth is deliberately shallow, because a step down
        /// is footing and a storey down is a ledge. Authored in metres as
        /// <c>WorldTuning.json.enemyLedgeProbeForward</c> / <c>.enemyLedgeProbeDepth</c> and pushed in by
        /// <see cref="Configure"/>, which is the only writer.
        /// </summary>
        private float _ledgeProbeAhead;
        private float _ledgeProbeDepth;

        /// <summary>
        /// How much longer a perfect parry's stun lasts than an ordinary one. Player-owned
        /// (<c>PlayerCombat.json.perfectParryStunMultiplier</c>) and pushed down here by the spawner,
        /// because <c>MyGame.Enemy</c> may not read the player's design file. Every archetype's
        /// <c>Stun(bool perfect)</c> multiplies by this. <see cref="Configure"/> is the only writer.
        /// </summary>
        protected float perfectParryStunMultiplier;

        /// <summary>
        /// Set by <see cref="Configure"/> and read once, in <see cref="_Ready"/>. Every shipped enemy is
        /// configured while it is still detached - <c>GameplayEnemySpawner.InstantiateEnemy</c> does it
        /// before the actor is placed - so an enemy that reaches the tree without it has none of the
        /// eight shared numbers: no gravity, no leash, no stun reward. That is a wiring fault and is
        /// reported as one rather than run (PLAN_CLOSEOUT D1/K5b).
        /// </summary>
        private bool _configured;

        /// <summary>
        /// The shared behaviour numbers <c>WorldTuning.json</c> and <c>PlayerCombat.json</c> own, pushed
        /// in by <c>GameplayEnemySpawner</c> while the body is still detached - the same shape
        /// <c>Poise.Configure</c> uses, and for the same reason: this namespace sits below
        /// <c>MyGame.Gameplay</c> and below <c>MyGame.Player</c>, so it cannot fetch them itself.
        /// </summary>
        /// <remarks>
        /// Every distance is expected in <b>pixels</b> - <c>WorldTuningData.ScaleToPixels</c> has already
        /// run on them. A caller handing over raw authored metres would leave every enemy floating and
        /// blind, and nothing would go red.
        /// </remarks>
        public void Configure(
            float gravityPixels,
            float disengageDistancePixels,
            float idleToPatrol,
            float investigate,
            float recovery,
            float ledgeProbeAheadPixels,
            float ledgeProbeDepthPixels,
            float perfectParryMultiplier)
        {
            gravity = gravityPixels;
            disengageDistance = disengageDistancePixels;
            idleToPatrolTime = idleToPatrol;
            investigateDuration = investigate;
            recoveryDuration = recovery;
            _ledgeProbeAhead = ledgeProbeAheadPixels;
            _ledgeProbeDepth = ledgeProbeDepthPixels;
            perfectParryStunMultiplier = perfectParryMultiplier;
            _configured = true;
        }

        public override void _Ready()
        {
            AddToGroup(World.Group.Enemy);

            _sr = this.FindComponent<Sprite2D>();
            _health = this.FindComponent<Health>();
            if (_health != null)
            {
                // Unity also had to re-create a null UnityEvent field here; a C# event needs no such
                // defence, so the guard is gone and only the subscription remains.
                _health.OnHealthDepleted += EnterDeadState;
            }

            // After the wiring, not before it: a misconfigured enemy still has to report a death and
            // still has to come apart cleanly when the scene does.
            if (!_configured)
            {
                GD.PushError($"EnemyStateMachine: '{Name}' entered the tree without Configure(); it has no gravity, leash or stun multiplier and does not run.");
                SetProcess(false);
                SetPhysicsProcess(false);
            }
        }

        public override void _ExitTree()
        {
            if (_health != null)
            {
                _health.OnHealthDepleted -= EnterDeadState;
            }
        }

        public override void _Process(double delta)
        {
            if (_currentState == EnemyState.Dead)
            {
                return;
            }

            if (_player == null)
            {
                DetectPlayer();
            }

            UpdateStateMachine();
        }

        /// <summary>
        /// Gravity and the move step, which Unity's rigidbody did on its own. Archetypes with their own
        /// fixed-step work override this and call <c>base._PhysicsProcess(delta)</c> last, so the body
        /// moves with the velocity that frame's logic just chose.
        /// </summary>
        public override void _PhysicsProcess(double delta)
        {
            if (_bodyFrozen)
            {
                Velocity = Vector2.Zero;
                return;
            }

            if (!IsOnFloor())
            {
                Velocity = new Vector2(Velocity.X, Velocity.Y + (gravity * (float)delta));
            }

            MoveAndSlide();
        }

        protected virtual void UpdateStateMachine()
        {
            if (_currentState == EnemyState.Dead)
            {
                return;
            }

            float dist = _player != null ? GlobalPosition.DistanceTo(_player.GlobalPosition) : float.MaxValue;

            switch (_currentState)
            {
                case EnemyState.Idle:
                    HandleIdle();
                    if (_player != null)
                    {
                        TransitionTo(EnemyState.Investigate);
                    }

                    break;

                case EnemyState.Patrol:
                    HandlePatrol();
                    if (_player != null)
                    {
                        TransitionTo(EnemyState.Investigate);
                    }

                    break;

                case EnemyState.Investigate:
                    _stateTimer -= GameClock.DeltaTime;
                    HandleInvestigate();
                    if (_stateTimer <= 0f)
                    {
                        TransitionTo(EnemyState.Combat);
                    }

                    break;

                case EnemyState.Combat:
                    if (dist > disengageDistance)
                    {
                        TransitionTo(EnemyState.Recovery);
                    }

                    break;

                case EnemyState.Recovery:
                    _stateTimer -= GameClock.DeltaTime;
                    HandleRecovery();
                    if (_stateTimer <= 0f)
                    {
                        TransitionTo(EnemyState.Patrol);
                    }

                    break;

                case EnemyState.Stunned:
                    HandleStunned();
                    break;

                case EnemyState.Dead:
                    break;
            }
        }

        protected void TransitionTo(EnemyState newState)
        {
            if (_currentState == EnemyState.Dead && newState != EnemyState.Dead)
            {
                return;
            }

            if (_currentState == newState)
            {
                return;
            }

            EnemyState prev = _currentState;
            _currentState = newState;
            _stateTimer = GetStateDuration(newState);
            OnStateChanged?.Invoke(newState);

            // Patrol and Idle are the unengaged states, so arriving at one has to forget who was being
            // fought. Until this, _player was cleared only by death - and both states transition straight
            // back to Investigate the moment it is non-null, so an enemy that had ever seen the player
            // re-engaged on the next frame from any distance, looping Patrol - Investigate - Combat -
            // Recovery forever. Invisible in a 36-unit arena, where the player is never far enough away
            // to leave; in a 300-unit level it is every enemy in the chapter walking at you at once.
            // Here rather than at each Recovery-to-Patrol call because five archetypes make that
            // transition in nine places, and the leash has to mean the same thing in all of them.
            // Detection re-acquires next frame if the player really is still in range, and the ranges
            // sit under disengageDistance (5-7 against 8) so there is hysteresis rather than flapping.
            if (newState == EnemyState.Patrol || newState == EnemyState.Idle)
            {
                _player = null;
            }

            if (newState == EnemyState.Combat && prev != EnemyState.Combat)
            {
                OnEnteredCombat?.Invoke();
            }
            else if (prev == EnemyState.Combat && newState != EnemyState.Combat)
            {
                OnExitedCombat?.Invoke();
            }
        }

        private float GetStateDuration(EnemyState state)
        {
            return state switch
            {
                EnemyState.Idle => idleToPatrolTime,
                EnemyState.Investigate => investigateDuration,
                EnemyState.Recovery => recoveryDuration,
                _ => 0f
            };
        }

        protected virtual void DetectPlayer()
        {
            GodotObject hit = Phys2D.OverlapCircle(this, GlobalPosition, GetDetectionRange(), World.Layer.Player);
            if (hit != null && Phys2D.FindActorInGroup(hit, World.Group.Player) is Node2D player)
            {
                _player = player;
            }
        }

        protected void UpdateSensingAndEngagement()
        {
            if (_currentState == EnemyState.Dead)
            {
                return;
            }

            if (_player == null)
            {
                DetectPlayer();
            }

            if (_player != null && (_currentState == EnemyState.Idle || _currentState == EnemyState.Patrol))
            {
                TransitionTo(EnemyState.Investigate);
            }
        }

        /// <summary>
        /// Routed here by <see cref="CombatResolver"/> when this enemy's poise gauge empties. Each
        /// archetype forwards it to its own stun path, which already knows how to drop the telegraph,
        /// kill the swing and repaint the sprite - a stagger is the same interruption a parry causes,
        /// so there is no second recovery routine to keep in step with the first.
        /// </summary>
        public void OnPoiseBroken()
        {
            if (_currentState == EnemyState.Dead)
            {
                return;
            }

            StopMovement();
            OnStaggered();
        }

        protected virtual void OnStaggered() { }

        protected virtual void HandleIdle() { }

        protected virtual void HandlePatrol() { }

        protected virtual void HandleInvestigate() { }

        protected virtual void HandleRecovery() { }

        protected virtual void HandleStunned() { }

        protected virtual void OnEnteredDeadState() { }

        /// <summary>
        /// Detection radius, in Godot pixels. Abstract because the body that used to be here was a
        /// 5 m literal no archetype could reach: the four hand-built ones override
        /// <see cref="DetectPlayer"/> outright and the chapter boss overrides this (PLAN_CLOSEOUT K5b
        /// item 4). Every implementation reads its own design file.
        /// </summary>
        protected abstract float GetDetectionRange();

        protected void EnterDeadState()
        {
            if (_currentState == EnemyState.Dead)
            {
                return;
            }

            _player = null;
            StopMovement();
            TransitionTo(EnemyState.Dead);
            OnEnteredDeadState();
        }

        protected void MoveTowards(Vector2 target, float speed)
        {
            if (_currentState == EnemyState.Dead)
            {
                return;
            }

            float dir = Mathf.Sign(target.X - GlobalPosition.X);
            Velocity = new Vector2(dir * speed, Velocity.Y);
            if (_sr != null)
            {
                _sr.FlipH = dir < 0f;
            }
        }

        /// <summary>
        /// The direction back, or zero when the floor runs out that way. Nothing in this project did
        /// ledge detection, so a patrol on a platform walked off the end of it - which is why every one
        /// of the eight chapters puts all sixty of its placements on the arena floor.
        /// </summary>
        /// <remarks>
        /// Deliberately not applied to a chase. An enemy that has seen the player is committed and
        /// steps off after them; it is the loose movement - patrol, strafe, backing away from a player
        /// who has closed - that has no business leaving the platform it was placed on.
        ///
        /// ponytail: a single downward ray, so a one-tile gap it could step over reads the same as a
        /// cliff. That is the conservative way round - the failure is a patrol that turns early, not
        /// one that walks into the void.
        /// </remarks>
        protected float ClampToGroundAhead(float dir)
        {
            if (Mathf.IsZeroApprox(dir))
            {
                return dir;
            }

            Rect2 bounds = this.BodyBounds(FallbackHalfExtents);

            // Unity read bounds.min.y for the underside of the body and stepped 0.05 up from it. Godot's
            // +Y is down, so the underside is bounds.End.Y and "just above it" subtracts.
            var origin = new Vector2(
                dir > 0f ? bounds.End.X + _ledgeProbeAhead : bounds.Position.X - _ledgeProbeAhead,
                bounds.End.Y - World.U(0.05f));

            return Phys2D.Raycast(this, origin, Vector2.Down, _ledgeProbeDepth, World.Layer.GroundProbe)
                ? dir
                : 0f;
        }

        protected void StopMovement()
        {
            Velocity = new Vector2(0f, Velocity.Y);
        }

        protected int GetFacingDir()
        {
            return _player != null ? (_player.GlobalPosition.X > GlobalPosition.X ? 1 : -1) : 1;
        }

        /// <summary>
        /// Where a node this enemy spawns belongs. Unity's <c>Instantiate</c> with no parent dropped the
        /// object at the root of the active scene, and hazard strips in particular rely on being a root
        /// object rather than a child of the boss.
        /// </summary>
        protected Node SpawnRoot() => GetTree()?.CurrentScene ?? GetParent() ?? this;
    }
}
