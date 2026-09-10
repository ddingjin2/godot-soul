using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Player
{
    /// <summary>
    /// The player's front door. Owns nothing itself: it forwards input to <see cref="PlayerMotor2D"/>
    /// and <see cref="PlayerActionController"/>, re-publishes their events, and answers Combat's
    /// <see cref="IDamageGuard"/> and <see cref="IStaggerable"/> questions on their behalf.
    ///
    /// A component node under the player body, which is the motor. Everything it reaches for -
    /// stamina, health, poise, feedback - is either that body or one of its other children.
    /// </summary>
    public partial class PlayerController2D : Node2D, IDamageGuard, IStaggerable
    {
        [Export] private Node2D hitboxAnchor;
        [Export] private DamageHitbox2D damageHitbox;
        [Export] private Sprite2D visual;
        [Export] private StaminaSystem stamina;
        [Export] private CombatFeedback feedback;
        [Export] private AudioFeedback audioFeedback;
        [Export] private PlayerMotor2D motor;
        [Export] private PlayerActionController actions;
        [Export] private DeathStateController deathState;

        // Plain logic, not nodes - see the class comments on both.
        private readonly PlayerStateMachine stateMachine = new();
        private PlayerProgression progression;

        public event Action OnAttackPerformed;
        public event Action OnParrySuccess;
        public event Action OnDodgeStart;
        public event Action OnJump;
        public event Action OnDodgeExecuted;
        public event Action OnStaminaDepleted;

        public Vector2 Velocity => motor != null ? motor.Velocity : Vector2.Zero;
        public bool IsGrounded => motor != null && motor.IsGrounded;
        public bool IsAttacking => actions != null && actions.IsAttacking;
        public bool IsHeavyAttacking => actions != null && actions.IsHeavyAttacking;
        public bool IsDodging => actions != null && actions.IsDodging;
        public bool IsParrying => actions != null && actions.IsParrying;
        public bool IsHealing => actions != null && actions.IsHealing;
        public int HealCharges => actions != null ? actions.HealCharges : 0;
        public int MaxHealCharges => actions != null ? actions.MaxHealCharges : 0;
        public float FacingDir => motor != null ? motor.FacingDirection : _facingDir;

        public bool HasHitThisAttack
        {
            get => actions != null && actions.HasHitThisAttack;
            set
            {
                if (actions != null)
                {
                    actions.HasHitThisAttack = value;
                }
            }
        }

        public float CoyoteTime => motor != null ? motor.CoyoteTime : 0f;
        public float AttackKnockback => actions != null ? actions.AttackKnockback : 0f;
        public float ParryStunDuration => actions != null ? actions.ParryStunDuration : 0f;
        public bool IsInAttackCancelWindow => actions != null && actions.IsInAttackCancelWindow;
        public bool IsStaggered => actions != null && actions.IsStaggered;
        public bool CanHeal => !_healingDisabled;
        public PlayerState CurrentState => stateMachine.CurrentState;

        /// <summary>
        /// The soul sink, composed here rather than added as a node. <see cref="PlayerProgression.EnsureOn"/>
        /// is what everything outside this folder uses to reach it.
        /// </summary>
        public PlayerProgression Progression => progression ??= new PlayerProgression(this);

        private Health _health;
        private Vector2 _moveInput;
        private float _facingDir = 1f;
        private float _sinModifierSpeed = 1f;
        private float _sinModifierDamage = 1f;
        private float _sinModifierDodgeDuration = 1f;
        private float _sinModifierParryWindow = 1f;
        private bool _healingDisabled;
        private SinResonanceController _sinResonance;

        public void ApplySinModifier(SinState state, float speedMod, float damageMod, float dodgeMod, float parryMod, bool disableHealing)
        {
            _ = state;
            _sinModifierSpeed = speedMod;
            _sinModifierDamage = damageMod;
            _sinModifierDodgeDuration = dodgeMod;
            _sinModifierParryWindow = parryMod;
            _healingDisabled = disableHealing;

            // A sin that bans healing also breaks a drink already in progress, otherwise Wrath pressed
            // mid-wind-up still lands the heal it is supposed to forbid. The charge stays spent.
            if (_healingDisabled)
            {
                actions?.CancelHeal();
            }

            ApplyModifiersToComponents();
        }

        public void ResetSinModifiers()
        {
            _sinModifierSpeed = 1f;
            _sinModifierDamage = 1f;
            _sinModifierDodgeDuration = 1f;
            _sinModifierParryWindow = 1f;
            _healingDisabled = false;
            ApplyModifiersToComponents();
        }

        public override void _Ready()
        {
            stamina ??= this.GetComponentInParent<StaminaSystem>();
            feedback ??= this.GetComponentInParent<CombatFeedback>();
            audioFeedback ??= this.GetComponentInParent<AudioFeedback>();
            motor ??= this.GetComponentInParent<PlayerMotor2D>();
            actions ??= this.GetComponentInParent<PlayerActionController>();
            deathState ??= this.GetComponentInParent<DeathStateController>();
            _health = this.GetComponentInParent<Health>();

            if (motor == null)
            {
                // Unity could AddComponent a motor onto the player GameObject. The motor is the body
                // here, so a player without one is a spawner bug rather than something to paper over.
                GD.PushError("PlayerController2D: no PlayerMotor2D on this actor - the player body must be one.");
                return;
            }

            if (actions == null)
            {
                actions = new PlayerActionController { Name = nameof(PlayerActionController) };

                // Deferred, not a plain AddChild: this runs inside the motor's own ready propagation,
                // and Godot refuses a child added to a node that is still setting its children up
                // ("Parent node is busy setting up children"). A plain call leaves `actions` alive but
                // never parented - so it never ticks, IsAttacking and IsParrying read false forever, and
                // the parry guard silently stops working. The reference below is valid either way; only
                // the node's first _Process waits a frame.
                motor.CallDeferred(Node.MethodName.AddChild, actions);
            }

            motor.Initialize();

            // THE TRAP, carried over deliberately: this re-runs Initialize with *this component's*
            // damageHitbox field, which is null unless someone exported one. A hitbox wired from outside
            // before the node entered the tree is therefore clobbered here, which is exactly why
            // SetDamageHitbox exists and why GameplayPlayerSpawner calls it *after* the player is in the
            // tree rather than assigning the field. Do not "fix" this by skipping the call - the spawner
            // depends on the ordering as it stands.
            actions.Initialize(motor, stamina, damageHitbox);
            stateMachine.Initialize(_health, deathState);
            WireComponentEvents();
            WireHealthEvents();
            WireDeathStateEvents();
            WireSinResonanceEvents();
            ApplyModifiersToComponents();

            damageHitbox?.Initialize(hitboxAnchor ?? BodyNode);

            UpdateAttackDirection();
        }

        public override void _ExitTree()
        {
            UnwireHealthEvents();
            UnwireDeathStateEvents();
            UnwireSinResonanceEvents();
        }

        /// <summary>
        /// The sin controller owns the resonance rules but not the actor it modifies, so the player
        /// subscribes to it rather than being handed to it.
        /// </summary>
        private void WireSinResonanceEvents()
        {
            if (_sinResonance != null)
            {
                return;
            }

            _sinResonance = this.GetComponentInParent<SinResonanceController>();
            if (_sinResonance == null)
            {
                return;
            }

            _sinResonance.OnApplyModifiers += ApplySinModifier;
            _sinResonance.OnResetModifiers += ResetSinModifiers;
        }

        private void UnwireSinResonanceEvents()
        {
            if (_sinResonance == null)
            {
                return;
            }

            _sinResonance.OnApplyModifiers -= ApplySinModifier;
            _sinResonance.OnResetModifiers -= ResetSinModifiers;
            _sinResonance = null;
        }

        public override void _Process(double delta)
        {
            if (motor == null || actions == null)
            {
                return;
            }

            bool knockbackActive = motor.Tick(
                (float)delta,
                !actions.IsAttacking && !actions.IsDodging && !actions.IsParrying && !actions.IsHealing);

            if (!knockbackActive)
            {
                actions.Tick((float)delta, FacingDir);
            }

            stateMachine.UpdateState(this);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (motor == null || actions == null)
            {
                return;
            }

            // MoveAndSlide lives inside FixedTick, and this is the physics step it has to run in.
            motor.FixedTick((float)delta, actions.IsDodging);
        }

        /// <summary>Move input in Godot axes: +X right, +Y down.</summary>
        public void SetMoveInput(Vector2 input)
        {
            _moveInput = input.LimitLength(1f);
            motor?.SetMoveInput(_moveInput);
            actions?.SetMoveInput(_moveInput);
        }

        public void RequestJump()
        {
            motor?.RequestJump();
        }

        public void RequestDodge()
        {
            actions?.RequestDodge();
        }

        public void RequestAttack()
        {
            if (!CanRequestAttack())
            {
                actions?.CancelAttack();
                return;
            }

            actions?.RequestAttack();
        }

        public void RequestHeavyAttack()
        {
            if (!CanRequestAttack())
            {
                actions?.CancelAttack();
                return;
            }

            actions?.RequestHeavyAttack();
        }

        public void RequestParry()
        {
            actions?.RequestParry();
        }

        /// <summary>
        /// Reuses the death/spirit gate the attack requests use, and adds the sin ban on top: Wrath sets
        /// <see cref="CanHeal"/> false through <see cref="ApplySinModifier"/> and clears it on reset.
        /// </summary>
        public void RequestHeal()
        {
            if (!CanRequestAttack() || !CanHeal)
            {
                return;
            }

            actions?.RequestHeal();
        }

        public void RefillHealCharges()
        {
            actions?.RefillHealCharges();
        }

        public void EnterCancelWindow()
        {
            actions?.EnterCancelWindow();
        }

        public void AnimationSignalEnableHitbox()
        {
            actions?.AnimationSignalEnableHitbox();
        }

        public void AnimationSignalDisableHitbox()
        {
            actions?.AnimationSignalDisableHitbox();
        }

        public void AnimationSignalAttackEnd()
        {
            actions?.AnimationSignalAttackEnd();
        }

        public bool IsInvulnerable()
        {
            return actions != null && actions.IsInvulnerable();
        }

        public bool IsInParryWindow()
        {
            return actions != null && actions.IsInParryWindow();
        }

        public bool IsInPerfectParryWindow()
        {
            return actions != null && actions.IsInPerfectParryWindow();
        }

        public void NotifyParrySuccess(bool perfect)
        {
            actions?.NotifyParrySuccess(perfect);
        }

        /// <summary>
        /// Raised by <see cref="CombatResolver"/> when the player's poise gauge empties. A normal hit only
        /// cancels the swing (see OnDamageTaken); a poise break also costs the next half second.
        /// </summary>
        public void OnPoiseBroken()
        {
            actions?.Stagger();
            feedback?.TriggerImpactScale();
        }

        public float GetCurrentParryWindow()
        {
            return actions != null ? actions.GetCurrentParryWindow() : 0f;
        }

        /// <summary>
        /// <paramref name="direction"/> is a Godot-space unit vector and <paramref name="force"/> is
        /// already in pixels per second - Combat scales the authored knockback where it loads it, the
        /// same way <see cref="PlayerCombatData"/> does for the player's own swings.
        /// </summary>
        public void ApplyKnockback(Vector2 direction, float force)
        {
            if (IsInvulnerable())
            {
                feedback?.TriggerInvulnerableFlash();
                return;
            }

            motor?.ApplyKnockback(direction * force, 0.1f);
        }

        public void ResetForRespawn()
        {
            actions?.ResetActionState();
            actions?.RefillHealCharges();
            motor?.ResetMotion();
        }

        public void SetHitboxAnchor(Node2D anchor)
        {
            hitboxAnchor = anchor;
            damageHitbox?.Initialize(anchor);

            UpdateAttackDirection();
        }

        /// <summary>
        /// The escape hatch out of the _Ready trap above: hands the action controller a hitbox that was
        /// built after the player entered the tree, and re-anchors it. GameplayPlayerSpawner's only way
        /// of wiring a hitbox that survives.
        /// </summary>
        public void SetDamageHitbox(DamageHitbox2D hitbox)
        {
            damageHitbox = hitbox;
            actions?.SetDamageHitbox(hitbox);

            if (hitboxAnchor != null)
            {
                damageHitbox.Initialize(hitboxAnchor);
            }

            UpdateAttackDirection();
        }

        public void SetDeathStateController(DeathStateController controller)
        {
            if (deathState == controller)
            {
                return;
            }

            UnwireDeathStateEvents();
            deathState = controller;
            WireDeathStateEvents();
        }

        public void SetVisual(Sprite2D spriteRenderer)
        {
            visual = spriteRenderer;
            ApplyFacingToVisual();
        }

        /// <summary>The anchor the swing hangs off, falling back to the player body.</summary>
        public Node2D GetHitboxAnchor()
        {
            return hitboxAnchor ?? BodyNode;
        }

        private Node2D BodyNode => this.GetComponentInParent<CharacterBody2D>() ?? (Node2D)this;

        private void WireComponentEvents()
        {
            motor.OnFacingChanged -= OnMotorFacingChanged;
            motor.OnFacingChanged += OnMotorFacingChanged;

            motor.OnJump -= ForwardJump;
            motor.OnJump += ForwardJump;

            actions.OnAttackDirectionRequested -= UpdateAttackDirection;
            actions.OnAttackDirectionRequested += UpdateAttackDirection;

            actions.OnAttackPerformed -= ForwardAttackPerformed;
            actions.OnAttackPerformed += ForwardAttackPerformed;
            actions.OnParrySuccess -= ForwardParrySuccess;
            actions.OnParrySuccess += ForwardParrySuccess;
            actions.OnDodgeStart -= ForwardDodgeStart;
            actions.OnDodgeStart += ForwardDodgeStart;
            actions.OnDodgeExecuted -= ForwardDodgeExecuted;
            actions.OnDodgeExecuted += ForwardDodgeExecuted;
            actions.OnStaminaDepleted -= ForwardStaminaDepleted;
            actions.OnStaminaDepleted += ForwardStaminaDepleted;
        }

        private void WireHealthEvents()
        {
            if (_health == null)
            {
                return;
            }

            _health.OnDamaged -= OnDamageTaken;
            _health.OnDamaged += OnDamageTaken;
        }

        private void UnwireHealthEvents()
        {
            if (_health == null)
            {
                return;
            }

            _health.OnDamaged -= OnDamageTaken;
        }

        /// <summary>
        /// Taking a hit ends the current swing and staggers the player. Cancelling is what kills the live
        /// hitbox, so an interrupted attack cannot still land after the hit that interrupted it.
        /// </summary>
        private void OnDamageTaken()
        {
            actions?.CancelAttack();
            actions?.CancelHeal();

            if (_health != null && _health.LastKnockback > 0f)
            {
                ApplyKnockback(_health.LastHitDirection, _health.LastKnockback);
            }
        }

        private void WireDeathStateEvents()
        {
            if (deathState == null)
            {
                return;
            }

            deathState.OnDeath -= CancelCombatActions;
            deathState.OnDeath += CancelCombatActions;
            deathState.OnEnterSpiritState -= CancelCombatActions;
            deathState.OnEnterSpiritState += CancelCombatActions;
        }

        private void UnwireDeathStateEvents()
        {
            if (deathState == null)
            {
                return;
            }

            deathState.OnDeath -= CancelCombatActions;
            deathState.OnEnterSpiritState -= CancelCombatActions;
        }

        private void ApplyModifiersToComponents()
        {
            motor?.SetSpeedModifier(_sinModifierSpeed);
            actions?.SetActionModifiers(_sinModifierDamage, _sinModifierDodgeDuration, _sinModifierParryWindow);
        }

        private bool CanRequestAttack()
        {
            _health ??= this.GetComponentInParent<Health>();
            deathState ??= this.GetComponentInParent<DeathStateController>();

            bool healthAllowsAttack = _health == null || !_health.IsDead;
            bool deathStateAllowsAttack = deathState == null || !deathState.IsInSpiritState;
            return healthAllowsAttack && deathStateAllowsAttack;
        }

        private void CancelCombatActions()
        {
            actions?.ResetActionState();
        }

        private void OnMotorFacingChanged(float facingDirection)
        {
            _facingDir = facingDirection >= 0f ? 1f : -1f;
            ApplyFacingToVisual();
            UpdateAttackDirection();
        }

        private void ApplyFacingToVisual()
        {
            if (visual != null)
            {
                visual.FlipH = _facingDir < 0f;
            }
        }

        // TODO: move facing and hitbox mirroring to a dedicated PlayerFacingController once the player FSM is introduced.
        private void UpdateAttackDirection()
        {
            float direction = _facingDir >= 0f ? 1f : -1f;

            if (hitboxAnchor != null)
            {
                // Local position and local scale, exactly as in Unity. X only - no Y sign is involved,
                // so nothing here flips with the axis change.
                Vector2 pos = hitboxAnchor.Position;
                pos.X = Mathf.Abs(pos.X) * direction;
                hitboxAnchor.Position = pos;

                Vector2 scale = hitboxAnchor.Scale;
                scale.X = direction;
                hitboxAnchor.Scale = scale;
            }

            // 0.4 authored metres of reach in front of the anchor, in pixels.
            damageHitbox?.SetOffset(Vector2.Right * World.U(0.4f));
        }

        private void ForwardAttackPerformed() => OnAttackPerformed?.Invoke();
        private void ForwardParrySuccess() => OnParrySuccess?.Invoke();
        private void ForwardDodgeStart() => OnDodgeStart?.Invoke();
        private void ForwardJump() => OnJump?.Invoke();

        private void ForwardDodgeExecuted()
        {
            audioFeedback?.Play(AudioFeedbackCue.Dodge);
            OnDodgeExecuted?.Invoke();
        }

        private void ForwardStaminaDepleted() => OnStaminaDepleted?.Invoke();
    }
}
