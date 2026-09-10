using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// The first enemy the project had: patrol, notice, close, telegraph, swing. It predates
    /// <see cref="EnemyStateMachine"/> and deliberately does not sit on it - the archetypes replaced it,
    /// and it is kept because the earliest scenes and tests still build one.
    /// </summary>
    /// <remarks>
    /// UNITS: the inline defaults are Unity metres wrapped in <see cref="World.U"/>, because this class
    /// has no tuning asset to convert them at load.
    ///
    /// Gravity is applied here rather than by the engine, for the reason
    /// <see cref="EnemyStateMachine"/> spells out: project.godot leaves default gravity at zero.
    /// </remarks>
    public partial class GameplayEnemy2D : CharacterBody2D
    {
        // Patrol
        [Export] private float moveSpeed = World.U(2f);
        [Export] private float patrolDistance = World.U(3f);
        [Export] private float idleTime = 1f;

        // Detection
        [Export] private float detectionRange = World.U(5f);
        [Export] private float attackRange = World.U(1.5f);

        // Attack
        [Export] private float telegraphTime = 0.6f;
        [Export] private float attackTime = 0.3f;
        [Export] private float attackCooldown = 1.5f;
        [Export] private float attackDamage = 15f;
        [Export] private float attackKnockback = World.U(5f);
        [Export] private Node2D attackPoint;
        [Export] private float attackRadius = World.U(0.8f);

        // Stun
        [Export] private float stunDuration = 0.8f;

        public event Action OnDeath;
        public event Action OnAttackStart;
        public event Action OnParried;

        public bool IsStunned => _isStunned;
        public bool IsDead => _health.IsDead;

        private static readonly float Gravity = World.U(9.81f);

        private Health _health;
        private Node2D _player;
        private Vector2 _startPos;
        private Vector2 _patrolTarget;
        private float _idleTimer;
        private bool _isPatrolling = true;
        private bool _isStunned;
        private float _attackCooldownTimer;
        private float _telegraphTimer;
        private bool _isAttacking;
        private bool _hasHitInAttack;
        private int _facingDir = 1;
        private float _telegraphPulse;
        private Sprite2D _sr;

        public override void _Ready()
        {
            AddToGroup(World.Group.Enemy);

            _health = this.FindComponent<Health>();
            _sr = this.FindComponent<Sprite2D>();
            _startPos = GlobalPosition;
            _patrolTarget = _startPos + (Vector2.Right * patrolDistance);
        }

        public override void _Process(double delta)
        {
            if (_health.IsDead)
            {
                return;
            }

            var dt = (float)delta;

            if (_isStunned)
            {
                _stunTimer -= dt;
                if (_stunTimer <= 0f)
                {
                    _isStunned = false;
                }

                return;
            }

            _attackCooldownTimer -= dt;

            if (_player != null)
            {
                float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);

                if (dist <= attackRange && _attackCooldownTimer <= 0f && !_isAttacking)
                {
                    StartAttack();
                }
                else if (dist > detectionRange * 0.7f)
                {
                    _player = null;
                    _isPatrolling = true;
                }
                else if (dist > attackRange)
                {
                    MoveTowards(_player.GlobalPosition);
                }
            }
            else
            {
                DetectPlayer();
                if (_isPatrolling)
                {
                    Patrol();
                }
            }
        }

        private void DetectPlayer()
        {
            if (_player != null)
            {
                return;
            }

            GodotObject hit = Phys2D.OverlapCircle(this, GlobalPosition, detectionRange, World.Layer.Player);
            if (hit != null && Phys2D.FindActorInGroup(hit, World.Group.Player) is Node2D player)
            {
                _player = player;
                _isPatrolling = false;
            }
        }

        private void Patrol()
        {
            if (_idleTimer > 0f)
            {
                _idleTimer -= GameClock.DeltaTime;
                return;
            }

            Vector2 dir = (_patrolTarget - GlobalPosition).Normalized();
            Move(dir.X);

            if (GlobalPosition.DistanceTo(_patrolTarget) < World.U(0.2f))
            {
                _patrolTarget = _startPos + ((_patrolTarget - _startPos) * -1f);
                _idleTimer = idleTime;
            }
        }

        private void MoveTowards(Vector2 target)
        {
            float dir = Mathf.Sign(target.X - GlobalPosition.X);
            Move(dir);
        }

        private void Move(float dir)
        {
            if (_isAttacking || _isStunned)
            {
                return;
            }

            Velocity = new Vector2(dir * moveSpeed, Velocity.Y);
            _facingDir = (int)Mathf.Sign(dir);
            if (_sr != null)
            {
                _sr.FlipH = dir < 0f;
            }
        }

        private void StartAttack()
        {
            _isAttacking = true;
            _telegraphTimer = telegraphTime;
            _telegraphPulse = 0f;
            _hasHitInAttack = false;
            Velocity = Vector2.Zero;
            OnAttackStart?.Invoke();
        }

        protected virtual void UpdateAttack()
        {
            if (!_isAttacking)
            {
                return;
            }

            float dt = GameClock.FixedDeltaTime;

            _telegraphTimer -= dt;
            _telegraphPulse += dt * 8f;
            float pulse = 1f + (Mathf.Sin(_telegraphPulse) * 0.15f);
            Scale = new Vector2(pulse, pulse);

            if (_telegraphTimer <= 0f && !_hasHitInAttack)
            {
                PerformAttack();
            }
            else if (_telegraphTimer <= -attackTime)
            {
                _isAttacking = false;
                Scale = Vector2.One;
                _attackCooldownTimer = attackCooldown;
            }
        }

        private void PerformAttack()
        {
            _hasHitInAttack = true;

            Vector2 origin = attackPoint != null
                ? attackPoint.GlobalPosition
                : GlobalPosition + (Vector2.Right * _facingDir * World.U(0.5f));

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, origin, attackRadius, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                var request = new DamageRequest(
                    this,
                    player,
                    player.GlobalPosition,
                    Vector2.Right * _facingDir,
                    attackDamage,
                    attackKnockback,
                    DamageType.Standard);

                DamageResult result = CombatResolver.Resolve(request);

                if (result.WasParried)
                {
                    ApplyParry(result.WasPerfectParried);
                    return;
                }
            }
        }

        private void ApplyParry(bool perfect)
        {
            Stun(perfect);
            OnParried?.Invoke();
        }

        public void Stun(bool perfect = false)
        {
            _isStunned = true;
            _stunTimer = stunDuration * (perfect ? 1.6f : 1f);
            _isAttacking = false;
            _telegraphTimer = 0f;
            Scale = Vector2.One;
            Velocity = Vector2.Zero;
        }

        private float _stunTimer;

        public override void _PhysicsProcess(double delta)
        {
            UpdateAttack();

            if (!IsOnFloor())
            {
                Velocity = new Vector2(Velocity.X, Velocity.Y + (Gravity * (float)delta));
            }

            MoveAndSlide();
        }

        public virtual void TakeDamage(float damage, Vector2 direction, float knockback)
        {
            _health.ApplyDamage(damage, direction, knockback);
            Velocity = direction * knockback;

            if (_health.IsDead)
            {
                OnDeath?.Invoke();
            }
        }
    }
}
