using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// Holds a distance and throws. Its whole answer to being closed on is to back off and keep
    /// throwing, so its firing band and its retreat band are the fight.
    /// </summary>
    public partial class RangedCaster : EnemyStateMachine
    {
        [Export] private RangedCasterData tuningData;

        /// <summary>
        /// The projectile scene the pool instances per shot - Unity's prefab reference, which is what it
        /// always should have been. Null is legal and means <see cref="EnemyProjectile.ScenePath"/>: a
        /// caster nobody wired fires the same shot as one the spawner built.
        /// </summary>
        [Export] private PackedScene projectilePrefab;

        [Export] private Node2D telegraphIndicator;

        public event Action OnCastStart;
        public event Action OnShotFired;
        public event Action OnReposition;
        public event Action OnParried;

        public bool IsStunned => _isStunned;
        public bool IsDead => _health.IsDead;
        public float DetectionRange => tuningData.detectionRange;
        public float AttackRange => tuningData.attackRange;

        public void SetTuningData(RangedCasterData data) => tuningData = data;

        /// <param name="prefab">The shot's scene. Null falls back to <see cref="EnemyProjectile.ScenePath"/>.</param>
        /// <param name="tint">Readability's projectile colour, or null to keep the scene's own.</param>
        /// <param name="sortingOrder">Readability's projectile sorting order, or null to keep the scene's own.</param>
        public void SetProjectilePrefab(PackedScene prefab, Color? tint = null, int? sortingOrder = null)
        {
            projectilePrefab = prefab;
            _projectileTint = tint;
            _projectileSortingOrder = sortingOrder;
        }

        /// <summary>
        /// Telegraph base: the pre-mood caster body colour. Blending from here instead of
        /// tuningData.enemyColor keeps the telegraph at #E50FFF after the body is muted.
        /// See Docs/MoodDirection.md "The lerp trap".
        /// Authored as <c>RangedCaster.json.telegraphColor</c>.
        /// </summary>
        private Color TelegraphBase => tuningData.telegraphColor;

        private Health _health;
        private bool _isStunned;
        private float _stunTimer;
        private float _attackCooldownTimer;
        private float _castTelegraphTimer;
        private float _telegraphPulse;
        private bool _isCasting;
        private bool _isStrafing;
        private float _strafeDir = 1f;
        private int _facingDir = 1;
        private Vector2 _startPos;
        private Vector2 _patrolTarget;
        private float _idleTimer;
        private float _lastRepositionTime;
        private float _recoveryTimer;
        private float _repositionCooldown = 1.5f;
        private bool _isRepositioning;
        private EnemyProjectilePool _projectilePool;
        private Color? _projectileTint;
        private int? _projectileSortingOrder;
        /// <summary>Already pixels - <c>RangedCasterData.ScaleToPixels</c> converted the authored metres at load.</summary>
        private float PatrolHalfWidth => tuningData.patrolDistance;

        public override void _Ready()
        {
            base._Ready();

            if (tuningData == null)
            {
                GD.PushError($"{GetType().Name} '{Name}' entered the tree without tuning data; call SetTuningData first.");
                SetProcess(false);
                SetPhysicsProcess(false);
                return;
            }

            _health = this.FindComponent<Health>();
            _startPos = GlobalPosition;
            _patrolTarget = _startPos + (Vector2.Right * PatrolHalfWidth);

            _health.SetHealth(tuningData.maxHealth);
            if (_sr != null)
            {
                _sr.Modulate = tuningData.enemyColor;
            }

            // One source for the shot, not two. This used to build a second, divergent EnemyProjectile
            // tree in code whose sprite had neither texture nor size; loading the shipped scene means a
            // caster nobody wired fires exactly what the spawner's casters fire.
            projectilePrefab ??= GD.Load<PackedScene>(EnemyProjectile.ScenePath);

            // Unity's Start.
            if (this.FindComponent<EnemyGroupCombat>() == null)
            {
                AddChild(new EnemyGroupCombat { Name = "EnemyGroupCombat" });
            }

            if (this.FindComponent<CombatFeedback>() == null)
            {
                AddChild(new CombatFeedback { Name = "CombatFeedback" });
            }
        }

        public override void _Process(double delta)
        {
            if (_health.IsDead)
            {
                return;
            }

            var dt = (float)delta;

            UpdateSensingAndEngagement();

            if (_isStunned)
            {
                _stunTimer -= dt;
                if (_stunTimer <= 0f)
                {
                    _isStunned = false;
                    UpdateVisualColor();
                }

                return;
            }

            if (_currentState == EnemyState.Stunned)
            {
                return;
            }

            if (_isCasting)
            {
                return;
            }

            if (_currentState == EnemyState.Recovery)
            {
                _recoveryTimer -= dt;
                if (_recoveryTimer <= 0f)
                {
                    TransitionTo(EnemyState.Patrol);
                }

                return;
            }

            _attackCooldownTimer -= dt;

            float dist = _player != null ? GlobalPosition.DistanceTo(_player.GlobalPosition) : float.MaxValue;
            float minDist = tuningData.minDistance;
            float repositionDist = tuningData.repositionDistance;

            if (_currentState == EnemyState.Combat || _currentState == EnemyState.Investigate)
            {
                if (dist > disengageDistance)
                {
                    TransitionTo(EnemyState.Recovery);
                }
                else if (dist < minDist - repositionDist)
                {
                    RepositionAway();
                }
                else if (_attackCooldownTimer <= 0f && CanShoot())
                {
                    StartCast();
                }
                else if (dist > AttackRange)
                {
                    // Nothing used to close this gap. The fire test was `dist > minDistance +
                    // repositionDistance` and the cap was detectionRange, so the firing band was the
                    // one-unit shell 6 < d <= 7 and a caster the player walked up to strafed in silence
                    // forever - the exact inverse of what a ranged enemy reads as. attackRange was
                    // exposed the whole time and never read.
                    MoveTowards(_player.GlobalPosition, tuningData.moveSpeed);
                }
                else if (_isStrafing)
                {
                    Strafe();
                }
                else
                {
                    FacePlayer();
                }
            }
            else if (_currentState == EnemyState.Patrol)
            {
                HandlePatrol();
            }
            else if (_currentState == EnemyState.Idle)
            {
                _stateTimer -= dt;
                if (_stateTimer <= 0f)
                {
                    TransitionTo(EnemyState.Patrol);
                }
            }
        }

        protected override void DetectPlayer()
        {
            if (_player != null)
            {
                return;
            }

            float range = tuningData.detectionRange;
            GodotObject hit = Phys2D.OverlapCircle(this, GlobalPosition, range, World.Layer.Player);
            if (hit != null && Phys2D.FindActorInGroup(hit, World.Group.Player) is Node2D player)
            {
                _player = player;
            }
        }

        protected override void HandlePatrol()
        {
            if (_idleTimer > 0f)
            {
                _idleTimer -= GameClock.DeltaTime;
                StopMovement();
                return;
            }

            float speed = tuningData.moveSpeed;
            float dir = Mathf.Sign(_patrolTarget.X - GlobalPosition.X);
            dir = ClampToGroundAhead(ClampHomewardDirection(dir));
            Velocity = new Vector2(dir * speed, Velocity.Y);
            if (Mathf.IsZeroApprox(dir))
            {
                // Turned at a ledge rather than reached the far end, so the leg has to be re-aimed or
                // the patrol stands at the edge pushing into it for the rest of the run.
                TurnPatrolAround();
                return;
            }

            _facingDir = (int)Mathf.Sign(dir);
            if (_sr != null)
            {
                _sr.FlipH = dir < 0f;
            }

            if (Mathf.Abs(GlobalPosition.X - _patrolTarget.X) < World.U(0.3f))
            {
                TurnPatrolAround();
            }
        }

        private void TurnPatrolAround()
        {
            _facingDir *= -1;
            _patrolTarget = _startPos + (Vector2.Right * PatrolHalfWidth * _facingDir);
            _idleTimer = tuningData.patrolIdleTime;
        }

        /// <summary>
        /// Within its own attack range, and no closer than the distance it is trying to keep. The
        /// retreat branch above owns everything nearer than that, so this is the whole firing band:
        /// <c>minDistance - repositionDistance &lt; d &lt;= attackRange</c>, which on the shipped numbers
        /// is 2 to 5 metres rather than the 6 to 7 shell it used to be.
        /// </summary>
        private bool CanShoot()
        {
            if (_player == null)
            {
                return false;
            }

            return GlobalPosition.DistanceTo(_player.GlobalPosition) <= AttackRange;
        }

        private void FacePlayer()
        {
            if (_player == null)
            {
                return;
            }

            _facingDir = _player.GlobalPosition.X > GlobalPosition.X ? 1 : -1;
            if (_sr != null)
            {
                _sr.FlipH = _facingDir < 0;
            }
        }

        private void StartCast()
        {
            _isCasting = true;
            _castTelegraphTimer = tuningData.castTelegraphTime;
            _telegraphPulse = 0f;
            Velocity = Vector2.Zero;
            _isStrafing = false;
            OnCastStart?.Invoke();
            ShowTelegraphVisuals();
            UpdateVisualColor();
        }

        private void ShowTelegraphVisuals()
        {
            if (telegraphIndicator != null)
            {
                telegraphIndicator.Visible = true;
            }

            if (_sr != null)
            {
                _sr.Modulate = TelegraphBase.Lerp(Colors.Magenta, tuningData.telegraphBlend);
            }
        }

        private void StopTelegraphVisuals()
        {
            if (telegraphIndicator != null)
            {
                telegraphIndicator.Visible = false;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_isCasting)
            {
                var dt = (float)delta;
                _castTelegraphTimer -= dt;
                _telegraphPulse += dt * tuningData.telegraphPulseSpeed;

                float pulse = 1f + (Mathf.Sin(_telegraphPulse) * tuningData.telegraphPulseAmplitude);
                Scale = new Vector2(pulse, pulse);

                if (_castTelegraphTimer <= 0f)
                {
                    FireProjectile();
                }
            }

            base._PhysicsProcess(delta);
        }

        private void FireProjectile()
        {
            _isCasting = false;
            _attackCooldownTimer = tuningData.attackCooldown;
            StopTelegraphVisuals();
            Scale = Vector2.One;

            if (_player != null && projectilePrefab != null)
            {
                Vector2 direction = (_player.GlobalPosition - GlobalPosition).Normalized();

                // Built on the first shot rather than in _Ready, because projectilePrefab can still be
                // replaced by the spawner between the two.
                _projectilePool ??= new EnemyProjectilePool(
                    projectilePrefab, SpawnRoot(), _projectileTint, _projectileSortingOrder);

                EnemyProjectile projectile = _projectilePool.Spawn(GlobalPosition);
                if (projectile != null)
                {
                    // Every one of these is already pixels where it is a distance - RangedCasterData
                    // scaled them at load. The knockback used to be a World.U(3f) literal that ignored
                    // the attackKnockback sitting in the same file at the same 3 metres; it reads it now.
                    float projSpeed = tuningData.projectileSpeed;
                    float projDmg = tuningData.projectileDamage;
                    float projKnockback = tuningData.attackKnockback;
                    float projLifetime = tuningData.projectileLifetime;
                    float projArcHeight = tuningData.projectileArcHeight;
                    projectile.Initialize(direction, projSpeed, projDmg, projKnockback, projLifetime, projArcHeight);
                }
            }

            TransitionTo(EnemyState.Recovery);
            _recoveryTimer = recoveryDuration;

            OnShotFired?.Invoke();
            UpdateVisualColor();
        }

        private void Strafe()
        {
            float strafeSpeed = tuningData.strafeSpeed;
            float dir = ClampToGroundAhead(ClampHomewardDirection(_strafeDir));
            Velocity = new Vector2(dir * strafeSpeed, Velocity.Y);
            if (Mathf.IsZeroApprox(dir))
            {
                _strafeDir *= -1f;
            }

            if (GD.Randf() < tuningData.strafeFlipChance)
            {
                _strafeDir *= -1f;
            }
        }

        private void RepositionAway()
        {
            if (_player == null)
            {
                return;
            }

            if (GameClock.Time - _lastRepositionTime < _repositionCooldown)
            {
                return;
            }

            if (_isRepositioning)
            {
                return;
            }

            _lastRepositionTime = GameClock.Time;
            _isRepositioning = true;
            _repositionCooldown = (float)GD.RandRange(
                tuningData.repositionCooldownMin,
                tuningData.repositionCooldownMax);

            float dir = Mathf.Sign(GlobalPosition.X - _player.GlobalPosition.X);

            float strafeDir = GD.Randf() < 0.5f ? -1f : 1f;
            if (Mathf.IsEqualApprox(Mathf.Sign(GlobalPosition.X - _player.GlobalPosition.X), strafeDir))
            {
                strafeDir *= -1f;
            }

            dir = ClampToGroundAhead(ClampHomewardDirection(dir));
            Velocity = new Vector2(dir * tuningData.moveSpeed, 0f);
            _strafeDir = Mathf.IsZeroApprox(dir) ? -Mathf.Sign(GlobalPosition.X - _startPos.X) : dir;

            OnReposition?.Invoke();
            _isStrafing = true;

            EndRepositionAfterDelay();
        }

        /// <summary>Unity's <c>Invoke(nameof(EndReposition), 0.5f)</c>, with the delay authored.</summary>
        private async void EndRepositionAfterDelay()
        {
            float duration = tuningData.repositionDuration;
            await ToSignal(GetTree().CreateTimer(duration), SceneTreeTimer.SignalName.Timeout);

            if (GodotObject.IsInstanceValid(this))
            {
                EndReposition();
            }
        }

        private void EndReposition()
        {
            _isRepositioning = false;
        }

        private float ClampHomewardDirection(float dir)
        {
            if (dir > 0f && GlobalPosition.X >= _startPos.X + PatrolHalfWidth)
            {
                _facingDir = -1;
                _patrolTarget = _startPos + (Vector2.Left * PatrolHalfWidth);
                return 0f;
            }

            if (dir < 0f && GlobalPosition.X <= _startPos.X - PatrolHalfWidth)
            {
                _facingDir = 1;
                _patrolTarget = _startPos + (Vector2.Right * PatrolHalfWidth);
                return 0f;
            }

            return dir;
        }

        // A broken poise gauge interrupts the cast, same as a parry does.
        protected override void OnStaggered() => Stun();

        public void Stun()
        {
            _isStunned = true;
            _stunTimer = tuningData.stunDuration;
            _isCasting = false;
            _isRepositioning = false;
            Velocity = Vector2.Zero;
            Scale = Vector2.One;
            StopTelegraphVisuals();
            UpdateVisualColor();
        }

        private void UpdateVisualColor()
        {
            if (_sr == null)
            {
                return;
            }

            if (_isStunned)
            {
                _sr.Modulate = new Color(0.3529412f, 0.3529412f, 0.34117648f); // ASH_STUN #5A5A57
            }
            else if (_isCasting)
            {
                _sr.Modulate = Colors.Magenta; // frozen: cast frame is a danger read
            }
            else if (_currentState == EnemyState.Recovery)
            {
                _sr.Modulate = new Color(0.24705882f, 0.32941177f, 0.34117648f); // COLD_400 #3F5457
            }
            else
            {
                _sr.Modulate = tuningData.enemyColor;
            }
        }

        protected override void OnEnteredDeadState()
        {
            _isStunned = false;
            _isCasting = false;
            _isStrafing = false;
            _isRepositioning = false;
            _castTelegraphTimer = 0f;
            _recoveryTimer = 0f;
            Scale = Vector2.One;
            StopTelegraphVisuals();
        }

        public void TakeDamage(float damage, Vector2 direction, float knockback)
        {
            _health.ApplyDamage(damage, direction, knockback);
            Velocity = direction * knockback;
        }
    }
}
