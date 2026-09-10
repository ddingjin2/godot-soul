using System;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// Chapter one's fight: three hand-written patterns, a phase change with a rage window, an arena the
    /// boss cannot leave, and an intro that waits for whatever is being staged over it.
    ///
    /// Deliberately NOT rebuilt on <see cref="RainbowChapterBossBehaviour"/>. It is the tuned encounter
    /// the whole slice was balanced against, and the P0 runner reaches into its private members by name.
    /// </summary>
    public partial class WrathMiniBoss : EnemyStateMachine, IBossEncounter
    {
        [Export] private WrathMiniBossData tuningData;
        [Export] private BossEncounterData encounterData;
        [Export] private Node2D telegraphIndicator;
        [Export] private Node2D introTriggerZone;

        public event Action OnAttackStart;
        public event Action OnSlash;
        public event Action OnSlam;
        public event Action OnRageRush;
        public event Action OnPhaseChange;
        public event Action OnParried;
        public event Action OnDeath;
        public event Action OnVictory;
        public event Action OnIntroStart;
        public event Action OnIntroEnd;
        public event Action OnRecoveryWindowStart;
        public event Action OnRecoveryWindowEnd;
        public event Action OnRageModeStart;
        public event Action OnRageModeEnd;

        public bool IsStunned => _isStunned;
        public bool IsDead => _health.IsDead;
        public bool IsInPhaseTwo => _isPhaseTwo;
        public bool IsInVictoryState => _isVictorious;
        public bool IsInIntro => _isInIntro;
        public bool IsInRageMode => _isInRageMode;
        public float DetectionRange => encounterData != null ? encounterData.detectionRange : DefaultDetectionRange;
        public float AttackRange => tuningData != null ? tuningData.slashRange : World.U(1.8f);

        public void SetTuningData(WrathMiniBossData data) => tuningData = data;

        /// <summary>
        /// The encounter around the fight - arena reach, intro patience, punish window, phase-two
        /// pattern. Null leaves every constant below in place, which is what the P0 runner's synthetic
        /// boss gets and what the fight was tuned against.
        /// </summary>
        public void SetEncounterData(BossEncounterData data) => encounterData = data;

        /// <summary>
        /// Replaces the colour the body settles back to after a telegraph, a flash or a phase change.
        /// The telegraph tints themselves are untouched - a warning nobody can see is not a warning.
        /// </summary>
        /// <remarks>
        /// Additive, and it writes nothing but the cache: <c>_originalColor</c> is read once in
        /// <see cref="_Ready"/> from the tuning asset and never re-read, so overwriting it is the only way
        /// in from outside. The caller paints the sprite itself.
        /// </remarks>
        public void SetRestingColor(Color color) => _originalColor = color;

        /// <summary>
        /// Holds the intro sequence at its tail until <see cref="ReleaseIntroHold"/>, for a listener on
        /// <see cref="OnIntroStart"/> that is staging something over the intro. It is a flag rather than
        /// a call out because that listener lives in the Gameplay namespace, which this one does not
        /// reference.
        /// </summary>
        /// <remarks>
        /// Default is no wait. With nobody listening - the P0 runner builds the boss with no cutscene
        /// layer at all - the sequence runs straight through, and the hold is capped either way.
        /// </remarks>
        public void HoldIntro() => _introHold = true;

        public void ReleaseIntroHold() => _introHold = false;

        /// <summary>
        /// <see cref="IBossEncounter"/> in terms of what this class already had. Deliberately second
        /// names rather than renames of the members: the existing names are wired into the spawner, the
        /// tests and the triggers.
        ///
        /// <c>Defeated</c> is <see cref="OnVictory"/>, not <see cref="OnDeath"/>: death fires on the
        /// killing blow, victory fires after the body has been read, which is the beat the panel waits
        /// for.
        /// </summary>
        event Action IBossEncounter.IntroStarted
        {
            add => OnIntroStart += value;
            remove => OnIntroStart -= value;
        }

        event Action IBossEncounter.Defeated
        {
            add => OnVictory += value;
            remove => OnVictory -= value;
        }

        Node2D IBossEncounter.BossObject => this;

        // Chapter one's fight predates the authored data path, so it has no name of its own to read -
        // the gate table in [[WorldSetting]] is where this one comes from.
        string IBossEncounter.BossName => "Crimson Warden";

        private enum AttackType { None, Slash, Slam, RageRush }

        /// <summary>
        /// Telegraph base: the pre-mood boss body colour. Blending from here instead of _originalColor
        /// keeps slash at #F7C20F and slam at #F70F0F after the body is muted.
        /// See Docs/MoodDirection.md "The lerp trap".
        /// Authored as <c>WrathMiniBoss.json.telegraphColor</c>; this is the file-less fallback.
        /// </summary>
        private Color TelegraphBase => tuningData?.telegraphColor ?? new Color(0.9f, 0.2f, 0.2f);

        /// <summary>#4A4A47 - the boss stops being a threat.</summary>
        private static readonly Color DefeatedColor = new Color(0.2901961f, 0.2901961f, 0.2784314f);

        private Health _health;
        private bool _isStunned;
        private float _stunTimer;
        private float _attackCooldownTimer;
        private bool _isAttacking;
        private AttackType _currentAttack;
        private float _telegraphTimer;
        private float _telegraphPulse;
        private bool _hasHitInAttack;
        private bool _isPhaseTwo;
        private bool _isVictorious;
        private bool _isInIntro = true;
        private bool _hasIntroTriggered;
        private bool _introHold;
        private bool _isInRageMode;
        private float _rageModeTimer;
        private int _attackPatternIndex;
        private float _rushElapsed;
        private float _recoveryTimer;
        private float _postAttackRecoveryTime = 0.5f;
        private bool _inRecoveryWindow;
        private Color _originalColor;
        private int _facingDir = 1;
        private Vector2 _spawnPosition;

        // Fallbacks, used whenever no BossEncounterData is bound. The intro cap follows from the
        // sequence's own beats being ~1.3s and CutsceneDirection.md capping the whole intro at 4.0s.
        private const float DefaultIntroHoldTimeout = 2.7f;
        private static readonly float DefaultArenaLeftOffset = World.U(5f);
        private static readonly float DefaultArenaRightOffset = World.U(1.1f);
        private static readonly float DefaultDetectionRange = World.U(8f);
        private const float DefaultRageSpeedMultiplier = 1.2f;
        private const float DefaultPhaseTwoRecoveryMultiplier = 0.8f;
        private const float DefaultVictoryPresentationDelay = 1.5f;

        private float IntroHoldTimeout => encounterData != null ? encounterData.introHoldTimeout : DefaultIntroHoldTimeout;
        private float ArenaLeftOffset => encounterData != null ? encounterData.arenaLeftOffset : DefaultArenaLeftOffset;
        private float ArenaRightOffset => encounterData != null ? encounterData.arenaRightOffset : DefaultArenaRightOffset;

        public override void _Ready()
        {
            // Base wires _sr and the health-depleted hook that drives EnemyState.Dead. Unity spelled the
            // same relationship as `protected override void Awake()` calling base.Awake() first; the
            // ported tests still look this member up by name, so the shape stays.
            base._Ready();

            _health = this.FindComponent<Health>();
            _spawnPosition = GlobalPosition;

            if (tuningData != null)
            {
                _health.SetHealth(tuningData.maxHealthBoss);
                if (_sr != null)
                {
                    _originalColor = tuningData.bossColor;
                    _sr.Modulate = _originalColor;
                    _sr.SetSpriteSize(tuningData.bossBodySize);
                }

                ResizeCapsule(tuningData.bossBodySize);
            }

            _health.OnHealthChanged += CheckPhaseChange;
            _health.OnHealthDepleted += OnBossDeath;

            if (introTriggerZone != null)
            {
                introTriggerZone.Visible = true;
            }

            if (this.FindComponent<CombatFeedback>() == null)
            {
                AddChild(new CombatFeedback { Name = "CombatFeedback" });
            }
        }

        public override void _ExitTree()
        {
            base._ExitTree();

            if (_health == null)
            {
                return;
            }

            _health.OnHealthChanged -= CheckPhaseChange;
            _health.OnHealthDepleted -= OnBossDeath;
        }

        /// <summary>Unity's <c>CapsuleCollider2D.size</c>, which is a radius and a height in Godot.</summary>
        private void ResizeCapsule(Vector2 sizePx)
        {
            if (this.FindComponent<CollisionShape2D>()?.Shape is CapsuleShape2D capsule)
            {
                capsule.Radius = sizePx.X * 0.5f;
                capsule.Height = sizePx.Y;
            }
        }

        private void CheckPhaseChange(float currentHealth)
        {
            if (_isPhaseTwo || _isVictorious)
            {
                return;
            }

            float threshold = tuningData != null ? tuningData.phaseThreshold : 0.5f;
            if (_health.NormalizedHealth <= threshold)
            {
                EnterPhaseTwo();
            }
        }

        private void EnterPhaseTwo()
        {
            _isPhaseTwo = true;
            _isInRageMode = true;
            _rageModeTimer = tuningData?.rageModeDuration ?? 5f;

            TriggerPhaseTransitionEffect();
            OnPhaseChange?.Invoke();
            OnRageModeStart?.Invoke();
            UpdateVisualColor();
        }

        private void TriggerPhaseTransitionEffect()
        {
            HitStopManager.Instance?.TriggerHitStop(tuningData?.phaseTransitionHitStop ?? 0.15f);
            CameraShake.Instance?.TriggerShake(CameraShakePreset.BossPhase);
            this.FindComponent<CombatFeedback>()?.TriggerImpactScale();

            PhaseTransitionFlash();
        }

        private async void PhaseTransitionFlash()
        {
            if (_sr == null)
            {
                return;
            }

            _sr.Modulate = Colors.Red;
            await ToSignal(GetTree().CreateTimer(tuningData?.phaseFlashRedTime ?? 0.1f), SceneTreeTimer.SignalName.Timeout);

            if (!GodotObject.IsInstanceValid(this) || _sr == null)
            {
                return;
            }

            _sr.Modulate = Colors.White;
            await ToSignal(GetTree().CreateTimer(tuningData?.phaseFlashWhiteTime ?? 0.05f), SceneTreeTimer.SignalName.Timeout);

            if (!GodotObject.IsInstanceValid(this) || _sr == null)
            {
                return;
            }

            _sr.Modulate = new Color(1f, 0.3f, 0.3f);
        }

        private void OnBossDeath()
        {
            if (_isVictorious)
            {
                return;
            }

            _isVictorious = true;
            _isAttacking = false;
            Velocity = Vector2.Zero;

            // Unity switched the Rigidbody2D to Static here. A CharacterBody2D has no body type, so the
            // same "nothing moves this any more" is the base class's freeze flag.
            _bodyFrozen = true;

            if (_sr != null)
            {
                _sr.Modulate = DefeatedColor;
            }

            OnDeath?.Invoke();

            VictorySequence();
        }

        /// <summary>The shipped 40/30/30 split, kept for a boss built with no encounter data.</summary>
        private static int DefaultPhaseTwoPattern(float roll)
        {
            if (roll < 0.4f)
            {
                return 0;
            }

            return roll < 0.7f ? 1 : 2;
        }

        private async void VictorySequence()
        {
            await ToSignal(
                GetTree().CreateTimer(encounterData != null ? encounterData.victoryPresentationDelay : DefaultVictoryPresentationDelay),
                SceneTreeTimer.SignalName.Timeout);

            if (!GodotObject.IsInstanceValid(this))
            {
                return;
            }

            if (_sr != null)
            {
                _sr.Modulate = new Color(0.36862746f, 0.36862746f, 0.3529412f); // #5E5E5A, death settled
            }

            OnVictory?.Invoke();
            QueueFree();
        }

        public override void _Process(double delta)
        {
            if (_health.IsDead && !_isVictorious)
            {
                return;
            }

            if (_isVictorious)
            {
                return;
            }

            var dt = (float)delta;

            UpdateSensingAndEngagement();

            if (_isInIntro)
            {
                if (!_hasIntroTriggered && _player != null)
                {
                    _hasIntroTriggered = true;
                    BossIntroSequence();
                }

                return;
            }

            if (_isStunned)
            {
                _stunTimer -= dt;
                if (_stunTimer <= 0f)
                {
                    _isStunned = false;
                    _inRecoveryWindow = true;
                    OnRecoveryWindowStart?.Invoke();
                    StartRecoveryTimer();
                    UpdateVisualColor();
                }

                return;
            }

            if (_currentState == EnemyState.Stunned)
            {
                return;
            }

            if (_isInRageMode)
            {
                _rageModeTimer -= dt;
                if (_rageModeTimer <= 0f)
                {
                    _isInRageMode = false;
                    OnRageModeEnd?.Invoke();
                }
            }

            if (_isAttacking)
            {
                UpdateAttackTelegraph();
                return;
            }

            if (_inRecoveryWindow)
            {
                _recoveryTimer -= dt;
                if (_recoveryTimer <= 0f)
                {
                    _inRecoveryWindow = false;
                    OnRecoveryWindowEnd?.Invoke();
                }

                return;
            }

            _attackCooldownTimer -= dt;

            float dist = _player != null ? GlobalPosition.DistanceTo(_player.GlobalPosition) : float.MaxValue;

            if (_currentState == EnemyState.Combat || _currentState == EnemyState.Investigate)
            {
                if (dist > disengageDistance)
                {
                    TransitionTo(EnemyState.Recovery);
                }
                else if (dist > AttackRange && _attackCooldownTimer <= 0f)
                {
                    MoveTowardsWithinArena(_player.GlobalPosition, GetCurrentSpeed());
                }
                else if (dist <= AttackRange && _attackCooldownTimer <= 0f)
                {
                    SelectAndStartAttack();
                }
            }
            else if (_currentState == EnemyState.Patrol)
            {
                if (dist <= DetectionRange)
                {
                    TransitionTo(EnemyState.Investigate);
                }
            }
            else if (_currentState == EnemyState.Recovery)
            {
                _recoveryTimer -= dt;
                if (_recoveryTimer <= 0f)
                {
                    TransitionTo(EnemyState.Patrol);
                }
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

        private async void BossIntroSequence()
        {
            // Raised here, not on ready: a listener stages the intro cutscene off this event, and the
            // intro does not actually begin until a player is known. A listener that wants the sequence
            // to wait for it calls HoldIntro from inside this Invoke, before the tail below reads it.
            OnIntroStart?.Invoke();

            if (_sr != null)
            {
                _sr.Modulate = Colors.Black;
            }

            await ToSignal(GetTree().CreateTimer(tuningData?.introBlackoutTime ?? 0.5f), SceneTreeTimer.SignalName.Timeout);
            if (!GodotObject.IsInstanceValid(this))
            {
                return;
            }

            HitStopManager.Instance?.TriggerHitStop(tuningData?.introImpactHitStop ?? 0.1f);

            if (_sr != null)
            {
                _sr.Modulate = _originalColor;
            }

            CameraShake.Instance?.TriggerShake(CameraShakePreset.BossPhase);

            await ToSignal(GetTree().CreateTimer(tuningData?.introSettleTime ?? 0.8f), SceneTreeTimer.SignalName.Timeout);
            if (!GodotObject.IsInstanceValid(this))
            {
                return;
            }

            // Wait out a listener's intro shot, but never on its word alone. Unscaled, because the shot
            // it is waiting for runs unscaled on top of this sequence's own hit stop. Everything below
            // belongs to the sequence - a hold that never lifted would leave a black boss standing in a
            // fight that never starts.
            float holdRemaining = IntroHoldTimeout;
            while (_introHold && holdRemaining > 0f)
            {
                holdRemaining -= GameClock.UnscaledDeltaTime;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(this))
                {
                    return;
                }
            }

            _introHold = false;

            _isInIntro = false;
            OnIntroEnd?.Invoke();

            if (introTriggerZone != null)
            {
                introTriggerZone.Visible = false;
            }

            TransitionTo(EnemyState.Combat);
        }

        public void TriggerIntroManually()
        {
            if (_player != null && !_hasIntroTriggered)
            {
                _hasIntroTriggered = true;
            }
        }

        protected override void DetectPlayer()
        {
            if (_player != null)
            {
                return;
            }

            GodotObject hit = Phys2D.OverlapCircle(this, GlobalPosition, DetectionRange, World.Layer.Player);
            if (hit != null && Phys2D.FindActorInGroup(hit, World.Group.Player) is Node2D player)
            {
                _player = player;
            }
        }

        /// <summary>
        /// Arena-clamped variant of the base <c>MoveTowards</c>. Deliberately a separate name rather than
        /// a hiding method, so a caller cannot end up on the unclamped base version by accident.
        /// </summary>
        private void MoveTowardsWithinArena(Vector2 target, float speed)
        {
            float dir = Mathf.Sign(target.X - GlobalPosition.X);
            dir = ClampArenaDirection(dir);
            Velocity = new Vector2(dir * speed, Velocity.Y);
            if (Mathf.IsZeroApprox(dir))
            {
                return;
            }

            _facingDir = (int)dir;
            if (_sr != null)
            {
                _sr.FlipH = dir < 0f;
            }
        }

        private float GetCurrentSpeed()
        {
            if (tuningData == null)
            {
                return World.U(2.5f);
            }

            float speed = _isPhaseTwo ? tuningData.moveSpeedBoss * tuningData.phaseSpeedMultiplier : tuningData.moveSpeedBoss;
            if (_isInRageMode)
            {
                speed *= encounterData != null ? encounterData.rageSpeedMultiplier : DefaultRageSpeedMultiplier;
            }

            return speed;
        }

        private void SelectAndStartAttack()
        {
            // Phase one rotates, so the opening of the fight is learnable. Phase two rolls against the
            // authored weights, which is what stops the second half from being the same three beats in
            // the same order at a higher speed.
            _attackPatternIndex = (_attackPatternIndex + 1) % 3;

            if (_isPhaseTwo)
            {
                _attackPatternIndex = encounterData != null
                    ? encounterData.SelectPhaseTwoPattern(GD.Randf())
                    : DefaultPhaseTwoPattern(GD.Randf());
            }

            switch (_attackPatternIndex)
            {
                case 0:
                    StartSlash();
                    break;
                case 1:
                    StartSlam();
                    break;
                case 2:
                    StartRageRush();
                    break;
            }
        }

        private void StartSlash()
        {
            _isAttacking = true;
            _currentAttack = AttackType.Slash;
            _telegraphTimer = tuningData?.slashTelegraphTime ?? 0.5f;
            _telegraphPulse = 0f;
            _hasHitInAttack = false;
            Velocity = Vector2.Zero;
            OnAttackStart?.Invoke();
            OnSlash?.Invoke();
            ShowTelegraphVisuals();
            UpdateVisualColor();
        }

        private void StartSlam()
        {
            _isAttacking = true;
            _currentAttack = AttackType.Slam;
            _telegraphTimer = tuningData?.slamTelegraphTime ?? 0.7f;
            _telegraphPulse = 0f;
            _hasHitInAttack = false;
            Velocity = Vector2.Zero;
            OnAttackStart?.Invoke();
            OnSlam?.Invoke();
            ShowTelegraphVisuals();
            UpdateVisualColor();
        }

        private void StartRageRush()
        {
            if (_player == null)
            {
                return;
            }

            _isAttacking = true;
            _currentAttack = AttackType.RageRush;
            _telegraphTimer = tuningData?.rushTelegraphTime ?? 0.4f;
            _telegraphPulse = 0f;
            _hasHitInAttack = false;
            _rushElapsed = 0f;
            OnAttackStart?.Invoke();
            OnRageRush?.Invoke();
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
                Color telegraph = _currentAttack == AttackType.Slam ? Colors.Red : Colors.Yellow;
                _sr.Modulate = TelegraphBase.Lerp(telegraph, tuningData?.telegraphBlend ?? 0.7f);
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
            if (_isAttacking)
            {
                switch (_currentAttack)
                {
                    case AttackType.Slash:
                        UpdateSlash();
                        break;
                    case AttackType.Slam:
                        UpdateSlam();
                        break;
                    case AttackType.RageRush:
                        UpdateRageRush((float)delta);
                        break;
                }
            }

            base._PhysicsProcess(delta);

            // Unity's LateUpdate. Godot has no such pass, and after MoveAndSlide is the equivalent
            // moment: the body has finished moving for this step.
            if (!_isVictorious)
            {
                ClampArenaPosition();
            }
        }

        private void UpdateAttackTelegraph()
        {
            float dt = GameClock.DeltaTime;
            _telegraphTimer -= dt;
            _telegraphPulse += dt * (tuningData?.telegraphPulseSpeed ?? 8f);

            float pulse = 1f + (Mathf.Sin(_telegraphPulse) * (tuningData?.telegraphPulseAmplitude ?? 0.2f));
            Scale = new Vector2(pulse, pulse);

            if (_telegraphTimer <= 0f)
            {
                ExecuteAttack();
            }
        }

        private void ExecuteAttack()
        {
            switch (_currentAttack)
            {
                case AttackType.Slash:
                    PerformSlash();
                    break;
                case AttackType.Slam:
                    PerformSlam();
                    break;
                case AttackType.RageRush:
                    break;
            }
        }

        private void UpdateSlash()
        {
            if (_telegraphTimer > -(tuningData?.slashDuration ?? 0.3f))
            {
                return;
            }

            if (!_hasHitInAttack)
            {
                PerformSlash();
            }

            EndAttack();
        }

        private void PerformSlash()
        {
            if (_hasHitInAttack)
            {
                return;
            }

            _hasHitInAttack = true;

            this.FindComponent<CombatFeedback>()?.TriggerImpactScale();

            // Both already pixels: WrathMiniBossData scaled them at load. Wrath carries no AttackPoint
            // node, so unlike the grunt's this offset is what every slash actually uses.
            float range = tuningData?.slashRange ?? World.U(1.8f);
            float originOffset = tuningData?.attackPointOffset ?? World.U(0.5f);
            Vector2 origin = GlobalPosition + (Vector2.Right * _facingDir * originOffset);

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, origin, range, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                float dmg = tuningData != null ? tuningData.attackDamageSlash : 20f;
                float knb = tuningData != null ? tuningData.attackKnockbackSlash : World.U(6f);
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

                if (result.Applied && player.FindComponentInParent<CombatResultBroadcaster>() == null)
                {
                    player.FindComponent<CombatFeedback>()?.Play(result);
                }
            }
        }

        private void UpdateSlam()
        {
            if (_telegraphTimer > -(tuningData?.slamDuration ?? 0.4f))
            {
                return;
            }

            if (!_hasHitInAttack)
            {
                PerformSlam();
            }

            EndAttack();
        }

        private void PerformSlam()
        {
            if (_hasHitInAttack)
            {
                return;
            }

            _hasHitInAttack = true;

            CameraShake.Instance?.TriggerShake(CameraShakePreset.BossSlam);
            this.FindComponent<CombatFeedback>()?.TriggerImpactScale();

            float range = tuningData?.slamRange ?? World.U(2.5f);

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, GlobalPosition, range, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                float dmg = tuningData != null ? tuningData.attackDamageSlam : 25f;
                float knb = tuningData?.slamShockwaveForce ?? World.U(8f);

                // Radial, so the shove is out of the crater in whatever direction the player stands.
                // No sign flip: this is a difference of two Godot-space positions.
                Vector2 dir = (player.GlobalPosition - GlobalPosition).Normalized();
                var request = new DamageRequest(
                    this,
                    player,
                    player.GlobalPosition,
                    dir,
                    dmg,
                    knb,
                    DamageType.Heavy);

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

        private void UpdateRageRush(float delta)
        {
            float rushDuration = tuningData?.rushDuration ?? 0.6f;
            if (_telegraphTimer > -rushDuration * (tuningData?.rushWindupFraction ?? 0.5f))
            {
                return;
            }

            _rushElapsed += delta;

            if (_rushElapsed < rushDuration)
            {
                float rushSpeed = tuningData != null ? tuningData.rushSpeed : World.U(12f);
                float dir = ClampArenaDirection(_facingDir);
                Velocity = new Vector2(dir * rushSpeed, 0f);
                if (Mathf.IsZeroApprox(dir))
                {
                    EndAttack();
                    return;
                }

                if (!_hasHitInAttack)
                {
                    CheckRushHit();
                }
            }
            else
            {
                EndAttack();
            }
        }

        private void CheckRushHit()
        {
            // Already pixels - WrathMiniBossData scaled it at load.
            float range = tuningData?.rushRange ?? World.U(1f);

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, GlobalPosition, range, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                _hasHitInAttack = true;

                float dmg = tuningData != null ? tuningData.attackDamageRage : 15f;
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

                if (result.Applied && player.FindComponentInParent<CombatResultBroadcaster>() == null)
                {
                    player.FindComponent<CombatFeedback>()?.Play(result);
                }
            }
        }

        private void EndAttack()
        {
            _isAttacking = false;
            _attackCooldownTimer = GetAttackCooldown(_currentAttack);
            _currentAttack = AttackType.None;
            Velocity = Vector2.Zero;
            Scale = Vector2.One;
            StopTelegraphVisuals();

            _inRecoveryWindow = true;
            OnRecoveryWindowStart?.Invoke();
            StartRecoveryTimer();
            UpdateVisualColor();
        }

        private void StartRecoveryTimer()
        {
            _recoveryTimer = encounterData != null ? encounterData.postAttackRecoveryTime : _postAttackRecoveryTime;
            if (_isPhaseTwo)
            {
                _recoveryTimer *= encounterData != null
                    ? encounterData.phaseTwoRecoveryMultiplier
                    : DefaultPhaseTwoRecoveryMultiplier;
            }
        }

        private float GetAttackCooldown(AttackType completedAttack)
        {
            if (tuningData == null)
            {
                return 2f;
            }

            float baseCooldown = completedAttack switch
            {
                AttackType.Slash => tuningData.slashCooldown,
                AttackType.Slam => tuningData.slamCooldown,
                AttackType.RageRush => tuningData.rushCooldown,
                _ => 2f
            };

            if (_isPhaseTwo)
            {
                baseCooldown *= tuningData.phaseAttackCooldownMultiplier;
            }

            return baseCooldown;
        }

        private void ApplyParry(bool perfect)
        {
            Stun(perfect);
            OnParried?.Invoke();
        }

        // A broken poise gauge opens the same window a perfect parry does. The boss's poise pool is the
        // large one, so this is the reward for committing to heavy attacks instead of chipping at it.
        protected override void OnStaggered() => Stun(true);

        public void Stun(bool perfect = false)
        {
            _isStunned = true;
            _stunTimer = (tuningData?.stunDuration ?? 1f) * (perfect ? perfectParryStunMultiplier : 1f);
            _isAttacking = false;
            _currentAttack = AttackType.None;
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

            if (_isVictorious)
            {
                _sr.Modulate = DefeatedColor;
            }
            else if (_isStunned)
            {
                _sr.Modulate = new Color(0.3529412f, 0.3529412f, 0.34117648f); // ASH_STUN #5A5A57
            }
            else if (_isAttacking)
            {
                _sr.Modulate = _currentAttack == AttackType.Slam ? Colors.Red : Colors.Yellow;
            }
            else if (_isPhaseTwo)
            {
                if (_isInRageMode)
                {
                    _sr.Modulate = Colors.Red.Lerp(Colors.White, Mathf.PingPong(GameClock.Time * 3f, 1f));
                }
                else
                {
                    _sr.Modulate = new Color(1f, 0.3f, 0.3f);
                }
            }
            else
            {
                _sr.Modulate = _originalColor;
            }
        }

        public void TakeDamage(float damage, Vector2 direction, float knockback)
        {
            _health.ApplyDamage(damage, direction, knockback);
            Velocity = direction * knockback;
            ClampArenaPosition();
        }

        private float ClampArenaDirection(float dir)
        {
            if (dir > 0f && GlobalPosition.X >= MaxArenaX)
            {
                return 0f;
            }

            if (dir < 0f && GlobalPosition.X <= MinArenaX)
            {
                return 0f;
            }

            return dir;
        }

        private void ClampArenaPosition()
        {
            float clampedX = Mathf.Clamp(GlobalPosition.X, MinArenaX, MaxArenaX);
            if (!Mathf.IsEqualApprox(clampedX, GlobalPosition.X))
            {
                GlobalPosition = new Vector2(clampedX, GlobalPosition.Y);
                Velocity = new Vector2(0f, Velocity.Y);
            }
        }

        private float MinArenaX => _spawnPosition.X - ArenaLeftOffset;
        private float MaxArenaX => _spawnPosition.X + ArenaRightOffset;
    }
}
