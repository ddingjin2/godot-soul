using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// Dodge, attack, parry, heal and stagger: every timed action the player has, and the input buffers
    /// in front of them. A component node under the player body - it has no geometry of its own, but it
    /// is a Node2D so it sits in the same 2D subtree as the rest of the player's parts.
    /// </summary>
    public partial class PlayerActionController : Node2D
    {
        public enum AttackPhase
        {
            None,
            Active,
            Recovery,
            Cooldown
        }

        // Dodge. Spatial defaults are pre-scaled to pixels; PlayerCombatData.Load scales the authored file.
        [Export] private float dodgeSpeed = World.U(15f);
        [Export] private float dodgeDuration = 0.2f;
        [Export] private float dodgeCooldown = 0.8f;
        [Export] private float dodgeInvulnerabilityTime = 0.15f;

        // Attack
        [Export] private float attackDuration = 0.3f;
        [Export] private float attackCooldown = 0.5f;
        [Export] private float attackActiveWindow = 0.1f;
        [Export] private float attackDamage = 20f;
        [Export] private float attackKnockback = World.U(4f);
        [Export] private float attackCancelWindow = 0.08f;

        // Heavy attack
        [Export] private float heavyAttackDuration = 0.45f;
        [Export] private float heavyAttackCooldown = 0.85f;
        [Export] private float heavyAttackActiveWindow = 0.16f;
        [Export] private float heavyAttackDamageMultiplier = 1.8f;
        [Export] private float heavyAttackKnockbackMultiplier = 1.4f;

        // Parry
        [Export] private float parryWindow = 0.2f;
        [Export] private float perfectParryWindow = 0.08f;
        [Export] private float parryCooldown = 0.5f;
        [Export] private float parryStunDuration = 0.8f;
        [Export] private float perfectParryStunMultiplier = 1.6f;

        // Stamina costs
        [Export] private float attackStaminaCost = 20f;
        [Export] private float heavyAttackStaminaCost = 35f;
        [Export] private float dodgeStaminaCost = 25f;
        [Export] private float parryStaminaCost = 15f;

        // Heal
        [Export] private float healAmount = 40f;
        [Export] private int maxHealCharges = 3;

        // Kept level with PlayerResources.json. The JSON overrides this on every real spawn, but the
        // exported fallback is what a synthetic player gets and what a reader sees first, and a 0.6
        // sitting next to an authored 0.9 reads as the shipped number to whoever looks next.
        [Export] private float healWindup = 0.9f;

        [Export] private float staggerDuration = 0.5f;
        [Export] private float inputBufferTime = 0.15f;

        // Combo
        [Export] private int maxComboSteps = 3;
        [Export] private float comboStepDamageMultiplier = 1.15f;

        public event Action OnAttackPerformed;
        public event Action OnParrySuccess;
        public event Action OnDodgeStart;
        public event Action OnDodgeExecuted;

        // Declared and forwarded by PlayerController2D, but never raised here - it was never raised in
        // the Unity source either. Kept so the seam stays where the port found it.
#pragma warning disable CS0067
        public event Action OnStaminaDepleted;
#pragma warning restore CS0067

        public bool IsAttacking => _isAttacking;
        public bool IsDodging => _isDodging;
        public bool IsParrying => _isParrying;
        public bool IsHeavyAttacking => _isHeavyAttacking;
        public bool IsHealing => _isHealing;
        public int HealCharges => _healCharges;
        public int MaxHealCharges => maxHealCharges;
        public bool HasHitThisAttack { get; set; }
        public float AttackKnockback => _isHeavyAttacking ? attackKnockback * heavyAttackKnockbackMultiplier : attackKnockback;
        public float ParryStunDuration => parryStunDuration;
        public float PerfectParryStunDuration => parryStunDuration * perfectParryStunMultiplier;
        public bool IsInAttackCancelWindow => _inCancelWindow;
        public AttackPhase CurrentAttackPhase => _attackPhase;
        public int ComboStep => _comboStep;
        public bool IsStaggered => _staggerTimer > 0f;

        public event Action OnAttackDirectionRequested;

        private PlayerMotor2D _motor;
        private StaminaSystem _stamina;
        private Health _health;
        private DamageHitbox2D _damageHitbox;
        private Vector2 _moveInput;
        private float _dodgeBuffer;
        private float _attackBuffer;
        private float _heavyAttackBuffer;
        private float _parryBuffer;
        private bool _isAttacking;
        private bool _isHeavyAttacking;
        private bool _isDodging;
        private bool _isParrying;
        private bool _isHealing;
        private int _healCharges;
        private float _healBuffer;
        private float _healTimer;
        private bool _inCancelWindow;
        private AttackPhase _attackPhase;
        private int _comboStep;
        private float _attackDurationTimer;
        private float _attackCooldownTimer;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;
        private float _parryTimer;
        private float _parryActiveTimer;
        private float _perfectParryTimer;
        private float _attackActiveTimer;
        private float _cancelWindowTimer;
        private float _staggerTimer;
        private float _damageModifier = 1f;
        private float _dodgeDurationModifier = 1f;
        private float _parryWindowModifier = 1f;

        /// <summary>
        /// The Unity signature took the <c>Rigidbody2D</c> the dodge writes velocity into. That body is
        /// now <see cref="PlayerMotor2D"/> itself, so the motor is what arrives here instead.
        /// </summary>
        public void Initialize(PlayerMotor2D motor, StaminaSystem stamina, DamageHitbox2D damageHitbox)
        {
            _motor = motor;
            _stamina = stamina;
            _damageHitbox = damageHitbox;
        }

        public override void _Ready()
        {
            _motor ??= this.GetComponentInParent<PlayerMotor2D>();
            _stamina ??= this.GetComponentInParent<StaminaSystem>();
            _health ??= this.GetComponentInParent<Health>();

            _healCharges = maxHealCharges;
        }

        public void SetDamageHitbox(DamageHitbox2D hitbox)
        {
            _damageHitbox = hitbox;
        }

        /// <summary>
        /// Base damage of a light swing at combo step zero. Heavy attacks and later combo steps multiply
        /// this, so raising it lifts every attack the player has. Exposed so <see cref="PlayerProgression"/>
        /// can hold a Strength level against it without going through <see cref="ApplyTuning"/>, which
        /// would put every other combat number back to the authored value at the same time.
        /// </summary>
        public float AttackDamage => attackDamage;

        public void SetAttackDamage(float value)
        {
            attackDamage = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Move input in Godot's axes: +X right, <b>+Y down</b>. The down-dodge test below reads it that
        /// way, where the Unity source tested for a negative y.
        /// </summary>
        public void SetMoveInput(Vector2 input)
        {
            _moveInput = input.LimitLength(1f);
        }

        public void SetActionModifiers(float damageModifier, float dodgeDurationModifier, float parryWindowModifier)
        {
            _damageModifier = damageModifier;
            _dodgeDurationModifier = dodgeDurationModifier;
            _parryWindowModifier = parryWindowModifier;
        }

        public void ApplyTuning(PlayerCombatData tuning)
        {
            if (tuning == null)
            {
                return;
            }

            dodgeSpeed = tuning.dodgeSpeed;
            dodgeDuration = tuning.dodgeDuration;
            dodgeCooldown = tuning.dodgeCooldown;
            dodgeInvulnerabilityTime = tuning.dodgeInvulnerabilityTime;
            attackDuration = tuning.attackDuration;
            attackCooldown = tuning.attackCooldown;
            attackActiveWindow = tuning.attackActiveWindow;
            attackDamage = tuning.attackDamage;
            attackKnockback = tuning.attackKnockback;
            attackCancelWindow = tuning.attackCancelWindow;
            heavyAttackDuration = tuning.heavyAttackDuration;
            heavyAttackCooldown = tuning.heavyAttackCooldown;
            heavyAttackActiveWindow = tuning.heavyAttackActiveWindow;
            heavyAttackDamageMultiplier = tuning.heavyAttackDamageMultiplier;
            heavyAttackKnockbackMultiplier = tuning.heavyAttackKnockbackMultiplier;
            parryWindow = tuning.parryWindow;
            perfectParryWindow = tuning.perfectParryWindow;
            parryCooldown = tuning.parryCooldown;
            parryStunDuration = tuning.parryStunDuration;
            perfectParryStunMultiplier = tuning.perfectParryStunMultiplier;
            staggerDuration = tuning.staggerDuration;
            inputBufferTime = tuning.inputBufferTime;
            maxComboSteps = tuning.maxComboSteps;
            comboStepDamageMultiplier = tuning.comboStepDamageMultiplier;
            attackStaminaCost = tuning.attackStaminaCost;
            heavyAttackStaminaCost = tuning.heavyAttackStaminaCost;
            dodgeStaminaCost = tuning.dodgeStaminaCost;
            parryStaminaCost = tuning.parryStaminaCost;
        }

        /// <summary>
        /// Heal numbers live in <see cref="PlayerResourceData"/> next to the other player resources, so they
        /// arrive separately from the combat tuning above. Charges refill like stamina does on a retune.
        /// </summary>
        public void ApplyResourceTuning(PlayerResourceData tuning)
        {
            if (tuning == null)
            {
                return;
            }

            healAmount = tuning.healAmount;
            maxHealCharges = tuning.maxHealCharges;
            healWindup = tuning.healWindup;
            _healCharges = maxHealCharges;
        }

        public void RequestDodge()
        {
            _dodgeBuffer = inputBufferTime;
        }

        public void RequestAttack()
        {
            _attackBuffer = inputBufferTime;
        }

        public void RequestHeavyAttack()
        {
            _heavyAttackBuffer = inputBufferTime;
        }

        public void CancelAttack()
        {
            _attackBuffer = 0f;
            _heavyAttackBuffer = 0f;
            _isAttacking = false;
            _isHeavyAttacking = false;
            HasHitThisAttack = false;
            _inCancelWindow = false;
            _comboStep = 0;
            _attackPhase = _attackCooldownTimer > 0f ? AttackPhase.Cooldown : AttackPhase.None;
            _attackDurationTimer = 0f;
            _attackActiveTimer = 0f;
            _cancelWindowTimer = 0f;

            _damageHitbox?.SetActive(false);
        }

        /// <summary>
        /// Breaks the current action and locks out new ones for <see cref="staggerDuration"/>.
        /// Raised by the player's poise gauge through <see cref="MyGame.Combat.IStaggerable"/>; a plain hit
        /// only cancels the swing, a poise break also takes the next moment away.
        /// </summary>
        public void Stagger()
        {
            CancelAttack();
            CancelHeal();
            _isDodging = false;
            EndParry();
            _dodgeBuffer = 0f;
            _parryBuffer = 0f;
            _staggerTimer = staggerDuration;
        }

        public void ResetActionState()
        {
            CancelAttack();
            CancelHeal();

            _staggerTimer = 0f;
            _dodgeBuffer = 0f;
            _attackBuffer = 0f;
            _heavyAttackBuffer = 0f;
            _parryBuffer = 0f;
            _isDodging = false;
            _isParrying = false;
            _isHeavyAttacking = false;
            _inCancelWindow = false;
            _attackPhase = AttackPhase.None;
            _attackCooldownTimer = 0f;
            _dodgeTimer = 0f;
            _dodgeCooldownTimer = 0f;
            _parryTimer = 0f;
            _parryActiveTimer = 0f;
            _perfectParryTimer = 0f;
            _cancelWindowTimer = 0f;
        }

        public void RequestParry()
        {
            _parryBuffer = inputBufferTime;
        }

        public void RequestHeal()
        {
            _healBuffer = inputBufferTime;
        }

        /// <summary>
        /// Breaks a drink in progress. The charge is NOT refunded: being interrupted has to cost something
        /// other than time, or drinking mid-fight carries no risk.
        /// </summary>
        public void CancelHeal()
        {
            _isHealing = false;
            _healTimer = 0f;
            _healBuffer = 0f;
        }

        public void RefillHealCharges()
        {
            _healCharges = maxHealCharges;
        }

        public void Tick(float deltaTime, float facingDirection)
        {
            HandleDodge(_moveInput.X, _moveInput.Y, facingDirection, deltaTime);
            HandleAttack(deltaTime);
            HandleParry(facingDirection);
            HandleHeal(deltaTime);
            UpdateTimersUI(deltaTime);
            DecayInputBuffers(deltaTime);
        }

        public bool IsInvulnerable()
        {
            return _isDodging && _dodgeTimer > dodgeDuration - dodgeInvulnerabilityTime;
        }

        public bool IsInParryWindow()
        {
            return _isParrying;
        }

        public bool IsInPerfectParryWindow()
        {
            return _isParrying && _perfectParryTimer > 0f;
        }

        public void NotifyParrySuccess(bool perfect)
        {
            _ = perfect;
            OnParrySuccess?.Invoke();
        }

        public float GetCurrentParryWindow()
        {
            return parryWindow * _parryWindowModifier;
        }

        public void EnterCancelWindow()
        {
            _inCancelWindow = true;
            _cancelWindowTimer = attackCancelWindow;
        }

        /// <summary>
        /// Optional early signal from an animation clip. Timers stay the fallback authority, so the clip only
        /// gets to move the active window, never to bypass it: re-arming the timer keeps a late event from
        /// opening a hitbox that the timer would close on the same frame.
        /// </summary>
        public void AnimationSignalEnableHitbox()
        {
            if (!_isAttacking || _damageHitbox == null)
            {
                return;
            }

            OnAttackDirectionRequested?.Invoke();
            _damageHitbox.SetActive(true);
            _attackPhase = AttackPhase.Active;
            _attackActiveTimer = _isHeavyAttacking ? heavyAttackActiveWindow : attackActiveWindow;
        }

        public void AnimationSignalDisableHitbox()
        {
            _damageHitbox?.SetActive(false);

            if (_isAttacking && _attackPhase == AttackPhase.Active)
            {
                _attackPhase = AttackPhase.Recovery;
                EnterCancelWindow();
            }
        }

        public void AnimationSignalAttackEnd()
        {
            if (!_isAttacking)
            {
                return;
            }

            _isAttacking = false;
            _isHeavyAttacking = false;
            _attackDurationTimer = 0f;
            _attackPhase = _attackCooldownTimer > 0f ? AttackPhase.Cooldown : AttackPhase.None;
        }

        private void HandleDodge(float h, float v, float facingDirection, float deltaTime)
        {
            bool canDodge = _dodgeBuffer > 0f
                && !IsStaggered
                && !_isHealing
                && _dodgeCooldownTimer <= 0f
                && !_isDodging
                && (!_isAttacking || _inCancelWindow)
                && (_stamina == null || _stamina.CanDodge);

            if (canDodge)
            {
                _dodgeBuffer = 0f;

                // Dash out of attack recovery. CancelAttack kills the hitbox so the cancelled swing cannot hit.
                if (_isAttacking)
                {
                    CancelAttack();
                }

                float dirX = h != 0f ? Mathf.Sign(h) : facingDirection;

                // Y FLIP: Unity read a downward stick as a negative y and dodged toward -1. Down is +Y
                // here, so the test and the direction both flip; this is still the ground-slide dodge.
                float dirY = v > 0.3f ? 1f : 0f;

                Vector2 dodgeDir = new Vector2(dirX, dirY).Normalized();
                if (dodgeDir.X == 0f && dodgeDir.Y == 0f)
                {
                    dodgeDir = new Vector2(facingDirection, 0f);
                }

                // The motor is the body, so this writes the same velocity the Rigidbody2D used to take.
                // The motor leaves it alone for the length of the dodge: canMoveAndJump is false while
                // dodging and its gravity step is skipped, exactly as in Unity.
                if (_motor != null)
                {
                    _motor.Velocity = dodgeDir * dodgeSpeed;
                }

                _isDodging = true;
                _dodgeTimer = dodgeDuration * _dodgeDurationModifier;
                _dodgeCooldownTimer = dodgeCooldown;

                _stamina?.Spend(dodgeStaminaCost);

                OnDodgeStart?.Invoke();
                OnDodgeExecuted?.Invoke();
            }

            if (_isDodging)
            {
                _dodgeTimer -= deltaTime;
                if (_dodgeTimer <= 0f)
                {
                    _isDodging = false;
                }
            }
        }

        private void HandleAttack(float deltaTime)
        {
            if (_heavyAttackBuffer > 0f)
            {
                TryStartAttack(true);
            }
            else if (_attackBuffer > 0f)
            {
                TryStartAttack(false);
            }

            if (_isAttacking)
            {
                // Ages before the window can open again, so a window opened this frame gets a full frame of life.
                if (_inCancelWindow)
                {
                    _cancelWindowTimer -= deltaTime;
                    if (_cancelWindowTimer <= 0f)
                    {
                        _inCancelWindow = false;
                    }
                }

                _attackActiveTimer -= deltaTime;
                if (_attackActiveTimer <= 0f && _attackPhase == AttackPhase.Active)
                {
                    _damageHitbox?.SetActive(false);

                    _attackPhase = AttackPhase.Recovery;
                    EnterCancelWindow();
                }

                _attackDurationTimer -= deltaTime;
                if (_attackDurationTimer <= 0f)
                {
                    _isAttacking = false;
                    _isHeavyAttacking = false;
                    _attackPhase = _attackCooldownTimer > 0f ? AttackPhase.Cooldown : AttackPhase.None;
                }
            }
        }

        private void TryStartAttack(bool heavy)
        {
            if (IsStaggered || _isHealing)
            {
                return;
            }

            // A light attack pressed inside the cancel window chains into the next combo step and skips the
            // cooldown. Anything else has to wait for the previous swing and its cooldown to finish.
            bool chaining = _isAttacking
                && _inCancelWindow
                && !heavy
                && !_isHeavyAttacking
                && _comboStep + 1 < maxComboSteps;

            if (_isAttacking && !chaining)
            {
                return;
            }

            if (!chaining && _attackCooldownTimer > 0f)
            {
                return;
            }

            float staminaCost = heavy ? heavyAttackStaminaCost : attackStaminaCost;
            if (_stamina != null && _stamina.CurrentStamina < staminaCost)
            {
                return;
            }

            if (heavy)
            {
                _heavyAttackBuffer = 0f;
            }
            else
            {
                _attackBuffer = 0f;
            }

            _comboStep = chaining ? _comboStep + 1 : 0;
            _isAttacking = true;
            _isHeavyAttacking = heavy;
            _attackPhase = AttackPhase.Active;
            _attackDurationTimer = heavy ? heavyAttackDuration : attackDuration;
            _attackCooldownTimer = heavy ? heavyAttackCooldown : attackCooldown;
            _attackActiveTimer = heavy ? heavyAttackActiveWindow : attackActiveWindow;
            HasHitThisAttack = false;
            _inCancelWindow = false;
            _cancelWindowTimer = 0f;

            if (_damageHitbox != null)
            {
                OnAttackDirectionRequested?.Invoke();
                float comboScale = Mathf.Pow(comboStepDamageMultiplier, _comboStep);
                _damageHitbox.SetDamage(attackDamage * comboScale * (heavy ? heavyAttackDamageMultiplier : 1f) * _damageModifier);
                _damageHitbox.SetDamageType(heavy ? DamageType.Heavy : DamageType.Standard);
                _damageHitbox.SetActive(true);
            }

            _stamina?.Spend(staminaCost);

            OnAttackPerformed?.Invoke();
        }

        private void HandleParry(float facingDirection)
        {
            _ = facingDirection;
            float window = parryWindow * _parryWindowModifier;
            bool canParry = _parryBuffer > 0f
                && !IsStaggered
                && !_isHealing
                && _parryTimer <= 0f
                && !_isAttacking
                && !_isDodging
                && (_stamina == null || _stamina.CanParry);

            if (canParry)
            {
                _parryBuffer = 0f;
                _isParrying = true;
                _parryActiveTimer = window;
                _perfectParryTimer = Mathf.Min(perfectParryWindow, window);
                _parryTimer = parryCooldown;

                _stamina?.Spend(parryStaminaCost);
            }
        }

        /// <summary>
        /// The charge is spent up front and the health only lands when the wind-up ends, so a hit inside the
        /// window (see PlayerController2D.OnDamageTaken calling <see cref="CancelHeal"/>) costs the charge and
        /// gives nothing back.
        /// </summary>
        private void HandleHeal(float deltaTime)
        {
            if (_healBuffer > 0f)
            {
                TryStartHeal();
            }

            if (!_isHealing)
            {
                return;
            }

            _healTimer -= deltaTime;
            if (_healTimer > 0f)
            {
                return;
            }

            _isHealing = false;

            // Initialize does not carry Health, so it is resolved here for anything that skipped _Ready.
            _health ??= this.GetComponentInParent<Health>();
            _health?.Heal(healAmount);
        }

        private void TryStartHeal()
        {
            // An empty flask refuses the press outright instead of holding it. The buffer forgives a press
            // made a fraction too early during another action; it must never queue a drink against a charge
            // the player does not have, or a rest minutes later hands the flask back and it empties itself
            // on the same frame.
            if (_healCharges <= 0)
            {
                _healBuffer = 0f;
                return;
            }

            if (_isHealing || IsStaggered || _isAttacking || _isDodging || _isParrying)
            {
                return;
            }

            _healBuffer = 0f;
            _healCharges--;
            _isHealing = true;
            _healTimer = healWindup;
        }

        private void EndParry()
        {
            _isParrying = false;
            _parryActiveTimer = 0f;
            _perfectParryTimer = 0f;
        }

        private void UpdateTimersUI(float deltaTime)
        {
            // Ticks with the rest of the action timers, so an active knockback (which skips Tick entirely)
            // holds the stagger open rather than running it down while the player is already helpless.
            if (_staggerTimer > 0f)
            {
                _staggerTimer = Mathf.Max(0f, _staggerTimer - deltaTime);
            }

            _dodgeCooldownTimer -= deltaTime;
            _attackCooldownTimer -= deltaTime;
            if (!_isAttacking && _attackPhase == AttackPhase.Cooldown && _attackCooldownTimer <= 0f)
            {
                _attackPhase = AttackPhase.None;
            }

            _parryTimer -= deltaTime;
            if (_perfectParryTimer > 0f)
            {
                _perfectParryTimer -= deltaTime;
            }

            if (_isParrying)
            {
                _parryActiveTimer -= deltaTime;
                if (_parryActiveTimer <= 0f)
                {
                    EndParry();
                }
            }
        }

        /// <summary>
        /// A request survives <see cref="inputBufferTime"/> instead of the single frame it arrived on, so a
        /// press during recovery or cooldown fires as soon as the action becomes legal instead of being dropped.
        /// </summary>
        private void DecayInputBuffers(float deltaTime)
        {
            _dodgeBuffer = Mathf.Max(0f, _dodgeBuffer - deltaTime);
            _attackBuffer = Mathf.Max(0f, _attackBuffer - deltaTime);
            _heavyAttackBuffer = Mathf.Max(0f, _heavyAttackBuffer - deltaTime);
            _parryBuffer = Mathf.Max(0f, _parryBuffer - deltaTime);

            // Heal was the one buffer missing from this list. Without it a press refused for want of a
            // charge never expired: it sat here until a rest refilled, then drank itself on the next tick.
            _healBuffer = Mathf.Max(0f, _healBuffer - deltaTime);
        }
    }
}
