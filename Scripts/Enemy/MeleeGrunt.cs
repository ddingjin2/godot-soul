using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// The baseline enemy: patrols a leg, notices, closes, telegraphs, swings, recovers.
    /// </summary>
    public partial class MeleeGrunt : EnemyStateMachine
    {
        [Export] private MeleeGruntData tuningData;
        [Export] private Node2D attackPoint;
        [Export] private Node2D telegraphIndicator;

        public event Action OnAttackStart;
        public event Action OnParried;
        public event Action OnDetectedPlayer;
        public event Action OnStunned;
        public event Action OnAttackHit;

        public bool IsStunned => _isStunned;
        public bool IsDead => _health.IsDead;
        public bool IsInRecovery => _currentState == EnemyState.Recovery;
        public float DetectionRange => tuningData != null ? tuningData.detectionRange : World.U(5f);
        public float AttackRange => tuningData != null ? tuningData.attackRange : World.U(1.5f);
        public float AttackRadius => tuningData != null ? tuningData.attackRadius : World.U(0.8f);

        public void SetTuningData(MeleeGruntData data) => tuningData = data;

        public void SetAttackPoint(Node2D point) => attackPoint = point;

        /// <summary>
        /// Frozen telegraph base: the pre-mood grunt body colour. The telegraph blends toward yellow
        /// from here rather than from tuningData.enemyColor, so muting the body cannot drag the
        /// danger read dark. Output stays #F7C20E. See Docs/MoodDirection.md "The lerp trap".
        /// </summary>
        private static readonly Color TelegraphBase = new Color(0.9f, 0.2f, 0.18f);

        private Health _health;

        /// <summary>
        /// Shadows <see cref="EnemyStateMachine._sr"/> and is never assigned, exactly as in the Unity
        /// source, where it was declared <c>private new SpriteRenderer _sr</c>. Every colour write below
        /// is therefore inert on a grunt. Kept rather than fixed: this is a port, the shipped fight is
        /// the one with the dead colour path, and the shadowing itself is what the reflection-based tests
        /// look for by name.
        /// </summary>
        private new Sprite2D _sr;

        private Vector2 _startPos;
        private Vector2 _patrolTarget;
        private float _idleTimer;
        private bool _isStunned;
        private float _stunTimer;
        private float _attackCooldownTimer;
        private float _telegraphTimer;
        private float _telegraphPulse;
        private bool _isAttacking;
        private bool _hasHitInAttack;
        private int _facingDir = 1;
        private float _recoveryTimer;

        public override void _Ready()
        {
            // Base wires the sprite, the health-depleted hook and the enemy group.
            base._Ready();

            _health = this.FindComponent<Health>();

            if (tuningData != null)
            {
                _health.SetHealth(tuningData.maxHealth);
                if (_sr != null)
                {
                    _sr.Modulate = tuningData.enemyColor;
                }
            }

            _startPos = GlobalPosition;
            _patrolTarget = _startPos + (Vector2.Right * (tuningData?.patrolDistance ?? World.U(3f)));

            // Unity's Start. Godot has one entry point, so the two run back to back.
            if (this.FindComponent<CombatFeedback>() == null)
            {
                AddChild(new CombatFeedback { Name = "CombatFeedback" });
            }

            if (this.FindComponent<EnemyGroupCombat>() == null)
            {
                AddChild(new EnemyGroupCombat { Name = "EnemyGroupCombat" });
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

            float dist = _player != null ? GlobalPosition.DistanceTo(_player.GlobalPosition) : float.MaxValue;

            if (_isAttacking)
            {
                _telegraphTimer -= dt;

                float pulseSpeed = tuningData?.telegraphPulseSpeed ?? 8f;
                _telegraphPulse += dt * pulseSpeed;

                float pulse = 1f + (Mathf.Sin(_telegraphPulse) * 0.15f);
                if (attackPoint != null)
                {
                    attackPoint.Scale = Vector2.One * pulse;
                }

                Scale = new Vector2(pulse, pulse);

                if (_telegraphTimer <= 0f && !_hasHitInAttack)
                {
                    PerformAttack();
                }
                else if (_telegraphTimer <= -(tuningData?.attackDuration ?? 0.3f))
                {
                    _isAttacking = false;
                    _attackCooldownTimer = tuningData?.attackCooldown ?? 1.5f;
                    StopTelegraphVisuals();
                    TransitionTo(EnemyState.Recovery);
                    _recoveryTimer = recoveryDuration;
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
                }

                return;
            }

            if (_currentState == EnemyState.Combat || _currentState == EnemyState.Investigate)
            {
                if (dist <= (tuningData?.attackRange ?? World.U(1.5f)) && _attackCooldownTimer <= 0f)
                {
                    StartAttack();
                }
                else if (dist > disengageDistance)
                {
                    TransitionTo(EnemyState.Recovery);
                }
                else if (dist > (tuningData?.attackRange ?? World.U(1.5f)))
                {
                    MoveTowards(_player.GlobalPosition, tuningData?.moveSpeed ?? World.U(2f));
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
                OnDetectedPlayer?.Invoke();
            }
        }

        protected override void HandlePatrol()
        {
            if (_idleTimer > 0f)
            {
                _idleTimer -= GameClock.DeltaTime;
                return;
            }

            Vector2 dir = (_patrolTarget - GlobalPosition).Normalized();

            // A patrol turns at a ledge instead of walking off it, which is what lets a grunt stand on
            // a platform at all. The chase below is deliberately not clamped: an enemy that has seen
            // the player is committed and follows them off.
            if (Mathf.IsZeroApprox(ClampToGroundAhead(dir.X)))
            {
                StopMovement();
                TurnPatrolAround();
                return;
            }

            Move(dir.X, tuningData?.moveSpeed ?? World.U(2f));

            if (GlobalPosition.DistanceTo(_patrolTarget) < World.U(0.2f))
            {
                TurnPatrolAround();
            }
        }

        private void TurnPatrolAround()
        {
            _patrolTarget = _startPos + ((_patrolTarget - _startPos) * -1f);
            _idleTimer = tuningData?.patrolIdleTime ?? 1f;
        }

        /// <summary>Shadows the base <c>MoveTowards</c> so a grunt's move goes through its own attack and stun gates.</summary>
        private new void MoveTowards(Vector2 target, float speed)
        {
            float dir = Mathf.Sign(target.X - GlobalPosition.X);
            Move(dir, speed);
        }

        private void Move(float dir, float speed)
        {
            if (_isAttacking || _isStunned)
            {
                return;
            }

            Velocity = new Vector2(dir * speed, Velocity.Y);
            _facingDir = (int)Mathf.Sign(dir);
            if (_sr != null)
            {
                _sr.FlipH = dir < 0f;
            }
        }

        private void StartAttack()
        {
            _isAttacking = true;
            _telegraphTimer = tuningData?.telegraphTime ?? 0.6f;
            _hasHitInAttack = false;
            _telegraphPulse = 0f;
            Velocity = Vector2.Zero;
            OnAttackStart?.Invoke();
            ShowTelegraphVisuals();
            UpdateVisualColor();
        }

        private void ShowTelegraphVisuals()
        {
            if (telegraphIndicator != null)
            {
                telegraphIndicator.Visible = true;
            }

            if (attackPoint != null && _sr != null)
            {
                _sr.Modulate = TelegraphBase.Lerp(Colors.Yellow, 0.7f);
            }
        }

        private void StopTelegraphVisuals()
        {
            if (telegraphIndicator != null)
            {
                telegraphIndicator.Visible = false;
            }

            Scale = Vector2.One;
        }

        private void PerformAttack()
        {
            _hasHitInAttack = true;
            StopTelegraphVisuals();

            this.FindComponent<CombatFeedback>()?.TriggerImpactScale();

            float radius = tuningData?.attackRadius ?? World.U(0.8f);
            Vector2 origin = attackPoint != null
                ? attackPoint.GlobalPosition
                : GlobalPosition + (Vector2.Right * _facingDir * World.U(0.5f));

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, origin, radius, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                float dmg = tuningData != null ? tuningData.attackDamage : 10f;
                float knb = tuningData != null ? tuningData.attackKnockback : World.U(5f);
                var request = new DamageRequest(
                    this,
                    player,
                    player.GlobalPosition,
                    Vector2.Right * _facingDir,
                    dmg,
                    knb,
                    DamageType.Standard);

                DamageResult result = CombatResolver.Resolve(request);

                if (result.WasParried)
                {
                    ApplyParry(result.WasPerfectParried);
                    return;
                }

                if (result.Applied)
                {
                    if (player.FindComponentInParent<CombatResultBroadcaster>() == null)
                    {
                        player.FindComponent<CombatFeedback>()?.Play(result);
                    }

                    OnAttackHit?.Invoke();
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
            _stunTimer = (tuningData?.stunDuration ?? 0.8f) * (perfect ? 1.6f : 1f);
            _isAttacking = false;
            _telegraphTimer = 0f;
            Velocity = Vector2.Zero;
            StopTelegraphVisuals();
            OnStunned?.Invoke();
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
            else if (_isAttacking)
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
            _isAttacking = false;
            _isStunned = false;
            _telegraphTimer = 0f;
            _recoveryTimer = 0f;
            StopTelegraphVisuals();
        }

        public void TakeDamage(float damage, Vector2 direction, float knockback)
        {
            _health.ApplyDamage(damage, direction, knockback);
            Velocity = direction * knockback;
        }
    }
}
