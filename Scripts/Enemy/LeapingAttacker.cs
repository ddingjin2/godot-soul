using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// Keeps its distance, then crosses it in one committed arc and is wide open where it lands. The
    /// landing window is the whole trade: the leap is unanswerable in the air and free damage on the
    /// ground.
    /// </summary>
    /// <remarks>
    /// AXIS: the leap is the one place in this folder where the Unity/Godot Y flip changes real
    /// numbers. <c>leapHeight</c> is authored as an upward launch speed, so it is applied as a negative
    /// Y here, and "falling" - which was <c>velocity.y &lt;= 0</c> in Unity - is <c>Velocity.Y &gt;= 0</c>.
    /// </remarks>
    public partial class LeapingAttacker : EnemyStateMachine
    {
        [Export] private LeapingAttackerData tuningData;
        [Export] private Node2D telegraphIndicator;

        public event Action OnLeapStart;
        public event Action OnLeapLand;
        public event Action OnParried;
        public event Action OnVulnerable;
        public event Action OnRecoveryEnd;

        public bool IsStunned => _isStunned;
        public bool IsDead => _health.IsDead;
        public bool IsLeaping => _isLeaping;
        public bool IsVulnerableAfterLand => _isVulnerable;
        public float DetectionRange => tuningData != null ? tuningData.detectionRange : World.U(5f);
        public float AttackRange => tuningData != null ? tuningData.attackRange : World.U(1.5f);

        public void SetTuningData(LeapingAttackerData data) => tuningData = data;

        /// <summary>
        /// Telegraph base: the pre-mood leaper body colour. Blending from here instead of
        /// tuningData.enemyColor keeps the telegraph at #FFD900 after the body is muted.
        /// See Docs/MoodDirection.md "The lerp trap".
        /// Authored as <c>LeapingAttacker.json.telegraphColor</c>; this is the file-less fallback.
        /// </summary>
        private Color TelegraphBase => tuningData?.telegraphColor ?? new Color(1f, 0.5f, 0f);

        private static readonly Vector2 FallbackHalfExtents = new Vector2(World.U(0.3f), World.U(0.5f));

        private Health _health;
        private bool _isStunned;
        private float _stunTimer;
        private float _attackCooldownTimer;
        private bool _isLeaping;
        private bool _isVulnerable;
        private float _vulnerableTimer;
        private float _leapTelegraphTimer;
        private float _telegraphPulse;
        private bool _isPreparingLeap;
        private bool _hasHitInLeap;
        private int _facingDir = 1;
        private Vector2 _startPos;
        private Vector2 _patrolTarget;
        private float _idleTimer;
        private Vector2 _leapTarget;
        private float _recoveryTimer;
        /// <summary>
        /// Already pixels - <c>LeapingAttackerData.ScaleToPixels</c> converted the authored metres at
        /// load. Also the combat leash, not just the walk: see <see cref="ClampHomewardDirection"/>.
        /// </summary>
        private float PatrolHalfWidth => tuningData?.patrolDistance ?? World.U(3f);

        public override void _Ready()
        {
            base._Ready();

            _health = this.FindComponent<Health>();
            _startPos = GlobalPosition;
            _patrolTarget = _startPos + (Vector2.Right * PatrolHalfWidth);

            if (tuningData != null)
            {
                _health.SetHealth(tuningData.maxHealth);
                if (_sr != null)
                {
                    _sr.Modulate = tuningData.enemyColor;
                }
            }

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

            _attackCooldownTimer -= dt;

            if (_isVulnerable)
            {
                _vulnerableTimer -= dt;
                if (_vulnerableTimer <= 0f)
                {
                    _isVulnerable = false;
                    UpdateVisualColor();
                }

                return;
            }

            if (_currentState == EnemyState.Recovery)
            {
                _recoveryTimer -= dt;
                if (_recoveryTimer <= 0f)
                {
                    TransitionTo(EnemyState.Patrol);
                    OnRecoveryEnd?.Invoke();
                }

                return;
            }

            if (_isLeaping || _isPreparingLeap)
            {
                return;
            }

            float dist = _player != null ? GlobalPosition.DistanceTo(_player.GlobalPosition) : float.MaxValue;

            if (_currentState == EnemyState.Combat || _currentState == EnemyState.Investigate)
            {
                if (dist <= (tuningData?.attackRange ?? World.U(1.5f)) && _attackCooldownTimer <= 0f)
                {
                    StartLeapAttack();
                }
                else if (dist > disengageDistance)
                {
                    TransitionTo(EnemyState.Recovery);
                }
                else
                {
                    MaintainDistance();
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

            float range = tuningData != null ? tuningData.detectionRange : World.U(5f);
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

            float speed = tuningData != null ? tuningData.moveSpeed : World.U(2f);

            // Turns at a ledge rather than stepping off, so a leaper can hold a platform. The chase and
            // the leap itself are left unclamped on purpose - crossing a gap is what a leaper is for.
            if (Mathf.IsZeroApprox(ClampToGroundAhead(Mathf.Sign(_patrolTarget.X - GlobalPosition.X))))
            {
                StopMovement();
                TurnPatrolAround();
                return;
            }

            MoveTowards(_patrolTarget, speed);

            if (Mathf.Abs(GlobalPosition.X - _patrolTarget.X) < World.U(0.3f))
            {
                TurnPatrolAround();
            }
        }

        private void TurnPatrolAround()
        {
            _facingDir *= -1;
            _patrolTarget = _startPos + (Vector2.Right * PatrolHalfWidth * _facingDir);
            _idleTimer = tuningData?.patrolIdleTime ?? 0.5f;
        }

        private void MaintainDistance()
        {
            if (_player == null)
            {
                return;
            }

            float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
            float desiredDist = tuningData != null ? tuningData.maintainDistance : World.U(4f);
            float speed = tuningData != null ? tuningData.moveSpeed : World.U(2f);

            // Already pixels, like desiredDist beside it: LeapingAttackerData scaled both at load.
            float deadband = tuningData?.maintainDistanceDeadband ?? World.U(1f);

            if (dist > desiredDist + deadband)
            {
                MoveTowards(_player.GlobalPosition, speed);
            }
            else if (dist < desiredDist - deadband)
            {
                MoveAwayFrom(_player.GlobalPosition, speed);
            }
            else
            {
                StopMovement();
            }
        }

        /// <summary>Shadows the base <c>MoveTowards</c> so a leaper's move goes through its own gates.</summary>
        private new void MoveTowards(Vector2 target, float speed)
        {
            float dir = Mathf.Sign(target.X - GlobalPosition.X);
            Move(dir, speed);
        }

        private void MoveAwayFrom(Vector2 target, float speed)
        {
            float dir = Mathf.Sign(GlobalPosition.X - target.X);
            Move(dir, speed);
        }

        private void Move(float dir, float speed)
        {
            if (_isLeaping || _isPreparingLeap || _isStunned)
            {
                return;
            }

            dir = ClampHomewardDirection(dir);
            Velocity = new Vector2(dir * speed, Velocity.Y);
            if (Mathf.IsZeroApprox(dir))
            {
                return;
            }

            _facingDir = (int)Mathf.Sign(dir);
            if (_sr != null)
            {
                _sr.FlipH = dir < 0f;
            }
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

        private void StartLeapAttack()
        {
            if (_player == null)
            {
                return;
            }

            _isPreparingLeap = true;
            _leapTelegraphTimer = tuningData?.leapTelegraphTime ?? 0.8f;
            _telegraphPulse = 0f;
            _leapTarget = _player.GlobalPosition;
            Velocity = Vector2.Zero;
            OnLeapStart?.Invoke();
            ShowTelegraphVisuals();
            UpdateVisualColor();
        }

        private void ShowTelegraphVisuals()
        {
            if (telegraphIndicator != null)
            {
                telegraphIndicator.Visible = true;
            }

            if (_sr != null && tuningData != null)
            {
                _sr.Modulate = TelegraphBase.Lerp(Colors.Yellow, tuningData?.telegraphBlend ?? 0.7f);
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
            var dt = (float)delta;

            if (_isPreparingLeap)
            {
                _leapTelegraphTimer -= dt;
                _telegraphPulse += dt * (tuningData?.telegraphPulseSpeed ?? 8f);

                float pulse = 1f + (Mathf.Sin(_telegraphPulse) * (tuningData?.telegraphPulseAmplitude ?? 0.15f));
                Scale = Vector2.One * pulse;

                if (_leapTelegraphTimer <= 0f)
                {
                    ExecuteLeap();
                }
            }
            else if (_isLeaping)
            {
                CheckLeapLanding();
            }

            // Gravity and the move step last, so the leap velocity chosen above is the one applied.
            base._PhysicsProcess(delta);
        }

        private void ExecuteLeap()
        {
            _isPreparingLeap = false;
            _isLeaping = true;
            _hasHitInLeap = false;
            StopTelegraphVisuals();
            Scale = Vector2.One;

            float leapSpeed = tuningData != null ? tuningData.leapSpeed : World.U(10f);
            Vector2 direction = (_leapTarget - GlobalPosition).Normalized();

            float heightBoost = tuningData != null ? tuningData.leapHeight : World.U(3f);

            // Y FLIP: leapHeight is authored as an upward launch speed. Unity wrote it straight into
            // velocity.y because +Y was up there; Godot's +Y is down, so up is the negative one.
            Velocity = new Vector2(direction.X * leapSpeed, -heightBoost);
        }

        private void CheckLeapLanding()
        {
            // Y FLIP: falling was velocity.y <= 0 in Unity's +Y-up world and is Velocity.Y >= 0 here.
            if (Velocity.Y >= 0f && IsGroundedForLanding())
            {
                Land();
            }
        }

        private bool IsGroundedForLanding()
        {
            Rect2 bounds = this.BodyBounds(FallbackHalfExtents);
            float probeDistance = (bounds.Size.Y * 0.5f) + World.U(0.08f);
            Vector2 centre = bounds.Position + (bounds.Size * 0.5f);

            return Phys2D.Raycast(this, centre, Vector2.Down, probeDistance, World.Layer.GroundProbe);
        }

        private void Land()
        {
            _isLeaping = false;
            Velocity = Vector2.Zero;

            CheckLeapHit();

            float vulnTime = tuningData != null ? tuningData.landingVulnerabilityTime : 0.5f;
            _isVulnerable = true;
            _vulnerableTimer = vulnTime;

            _attackCooldownTimer = tuningData != null ? tuningData.attackCooldown : 2f;
            TransitionTo(EnemyState.Recovery);
            _recoveryTimer = recoveryDuration + vulnTime;

            OnLeapLand?.Invoke();
            OnVulnerable?.Invoke();
            UpdateVisualColor();
        }

        private void CheckLeapHit()
        {
            if (_hasHitInLeap)
            {
                return;
            }

            float range = tuningData != null ? tuningData.attackRange : World.U(1.5f);

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, GlobalPosition, range, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                _hasHitInLeap = true;

                float dmg = tuningData != null ? tuningData.attackDamage : 10f;
                float knb = tuningData != null ? tuningData.attackKnockback : World.U(5f);
                var request = new DamageRequest(
                    this,
                    player,
                    player.GlobalPosition,
                    // The landing throws the player up. Vector2.Up is already (0, -1) in Godot, so this
                    // is the same direction the Unity version meant and needs no sign flip.
                    Vector2.Up,
                    dmg,
                    knb,
                    DamageType.Standard);

                DamageResult result = CombatResolver.Resolve(request);

                if (result.WasParried)
                {
                    ApplyParry(result.WasPerfectParried);
                    return;
                }

                if (result.Applied && player.FindComponentInParent<CombatResultBroadcaster>() == null)
                {
                    player.FindComponent<CombatFeedback>()?.Play(result);
                }
            }
        }

        private void ApplyParry(bool perfect)
        {
            Stun(perfect);
            OnParried?.Invoke();
        }

        // A broken poise gauge opens the same window a perfect parry does.
        protected override void OnStaggered() => Stun(true);

        public void Stun(bool perfect = false)
        {
            _isStunned = true;
            _stunTimer = (tuningData?.stunDuration ?? 0.8f) * (perfect ? perfectParryStunMultiplier : 1f);
            _isLeaping = false;
            _isPreparingLeap = false;
            _isVulnerable = false;
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
            else if (_isVulnerable)
            {
                _sr.Modulate = new Color(0.43137255f, 0.54901963f, 0.56078434f); // COLD_350 #6E8C8F, punish window
            }
            else if (_isPreparingLeap || _isLeaping)
            {
                _sr.Modulate = Colors.Yellow; // frozen: attack frame is a danger read
            }
            else if (_currentState == EnemyState.Recovery)
            {
                _sr.Modulate = new Color(0.24705882f, 0.32941177f, 0.34117648f); // COLD_400 #3F5457
            }
            else if (tuningData != null)
            {
                _sr.Modulate = tuningData.enemyColor;
            }
        }

        protected override void OnEnteredDeadState()
        {
            _isStunned = false;
            _isLeaping = false;
            _isPreparingLeap = false;
            _isVulnerable = false;
            _recoveryTimer = 0f;
            Scale = Vector2.One;
            StopTelegraphVisuals();
        }

        public void TakeDamage(float damage, Vector2 direction, float knockback)
        {
            if (_isVulnerable)
            {
                damage *= tuningData?.landingPunishMultiplier ?? 2f;
            }

            _health.ApplyDamage(damage, direction, knockback);
            Velocity = direction * knockback;
        }
    }
}
