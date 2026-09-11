using System;
using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Core;

namespace MyGame.Enemy
{
    /// <summary>
    /// A chapter boss that is authored rather than written. It owns no attacks of its own: it approaches,
    /// picks a <see cref="BossAttackProfile"/> off its <see cref="RainbowChapterBossData"/>, reads it out
    /// as telegraph -> active -> recovery, and resolves the hit through the same
    /// <see cref="CombatResolver"/> every other attacker uses. Chapter two is therefore a data asset and
    /// a spawner call, which is the whole point of the layer.
    ///
    /// It sits on <see cref="EnemyStateMachine"/> for sensing, engagement, staggering and death, so the
    /// only thing here is the attack loop and the arena.
    ///
    /// <see cref="WrathMiniBoss"/> is deliberately NOT rebuilt on this. It is the tuned chapter-one
    /// encounter with its own intro sequence, rage mode and three hand-written patterns, and the P0
    /// runner reaches into its private members by name; porting it would risk the one fight that works
    /// to remove a duplication nobody is paying for yet. When a second authored boss proves this loop,
    /// that is the moment to revisit it - not before.
    /// </summary>
    public sealed partial class RainbowChapterBossBehaviour : EnemyStateMachine, IBossEncounter
    {
        [Export] private RainbowChapterBossData bossData;
        [Export] private BossEncounterData encounterData;
        [Export] private Sprite2D visual;

        /// <summary>
        /// The hazard strip's scene, injected by the spawner the way <see cref="RangedCaster"/> is handed
        /// its projectile. Null is legal and means <see cref="BossHazardStrip.ScenePath"/>, so a boss made
        /// in a test burns the ground with the same strip the spawner's bosses drop - it used to get a
        /// bare, sprite-less one instead, which made the re-dressing below a no-op.
        /// </summary>
        [Export] private PackedScene hazardPrefab;

        /// <summary>
        /// The afterimage's scene. Null is legal for the same reason and falls back the same way, to
        /// <see cref="BossAfterimage.ScenePath"/>.
        /// </summary>
        [Export] private PackedScene afterimagePrefab;

        public event Action OnBossInitialized;
        public event Action OnPhaseTwoStarted;
        public event Action OnBossDefeated;
        public event Action OnAttackRequested;
        public event Action OnAttackCompleted;

        /// <summary>Raised the frame an attack's active window opens, after the telegraph has been read.</summary>
        public event Action OnAttackLanded;

        /// <summary>
        /// The arrival. Raised once, when a player is first seen - not on ready, because a boss the
        /// player has not reached yet has not arrived. A cutscene listener calls <see cref="HoldIntro"/>
        /// from inside this to stage a shot over it.
        /// </summary>
        public event Action OnIntroStart;

        public event Action OnIntroEnd;

        public RainbowChapterBossData BossData => bossData;
        public RainbowChapterColor ChapterColor => bossData != null ? bossData.ChapterColor : RainbowChapterColor.Red;
        public string BossName => bossData != null ? bossData.BossName : Name;
        public bool IsPhaseTwo { get; private set; }
        public bool IsDefeated => _bossHealth != null && _bossHealth.IsDead;
        public BossAttackProfile CurrentAttackProfile { get; private set; }
        public bool IsAttackRunning => CurrentAttackProfile != null;

        /// <summary>True while the current attack can still connect. Telegraph and recovery are both false.</summary>
        public bool IsAttackActive => IsAttackRunning && _phase == AttackPhase.Active;

        /// <summary>
        /// True during the pause an attack takes after its telegraph and before it snaps out. The boss
        /// looks finished here and nothing can connect, which is the point.
        /// </summary>
        public bool IsFeinting => IsAttackRunning && _phase == AttackPhase.Feint;

        /// <summary>True while a chained attack is waiting on its beat.</summary>
        public bool HasQueuedChain => _queuedChain != null;

        /// <summary>
        /// True while the boss is healing itself and can be stopped. This is chapter four's priority
        /// window: the one moment where hitting the boss is worth more than the punish it costs.
        /// </summary>
        public bool IsChanting => _chantTimer > 0f;

        /// <summary>How much health this boss has clawed back across the fight. Zero for a boss that never chants.</summary>
        public float HealthRestoredByChanting { get; private set; }

        /// <summary>The stance whose attacks are currently reachable, or empty when the boss has none.</summary>
        public string CurrentStanceId { get; private set; } = "";

        public bool IsStunned => _stunTimer > 0f;

        public bool IsInIntro => _isInIntro;

        event Action IBossEncounter.IntroStarted
        {
            add => OnIntroStart += value;
            remove => OnIntroStart -= value;
        }

        event Action IBossEncounter.Defeated
        {
            add => OnBossDefeated += value;
            remove => OnBossDefeated -= value;
        }

        Node2D IBossEncounter.BossObject => this;

        public void HoldIntro() => _introHold = true;

        public void ReleaseIntroHold() => _introHold = false;

        private enum AttackPhase { None, Telegraph, Feint, Active, Recovery }

        /// <summary>
        /// How many links a chain may run before the boss has to stop and let the player back in. A row
        /// that chains to itself, or a pair that chain to each other, is a fight with no punish window
        /// at all - and it is a one-word authoring mistake, so it is capped rather than trusted.
        /// Authored per chapter as <c>&lt;chapter&gt;_Encounter.json.maxChainSteps</c>; this is the
        /// fallback a boss with no encounter file gets.
        /// </summary>
        private const int DefaultMaxChainSteps = 4;

        private Health _bossHealth;
        private AttackPhase _phase;
        private float _phaseTimer;
        private float _attackCooldownTimer;
        private float _stunTimer;
        private bool _hasHitThisAttack;

        /// <summary>Latched when an attack's active window opens, so a lunge cannot be re-aimed mid-dash.</summary>
        private float _lungeDir = 1f;
        private int _rotationIndex = -1;
        private Vector2 _spawnPosition;
        private Color _restingColor = Colors.White;

        /// <summary>Set once someone outside has claimed the resting colour; see <see cref="SetRestingColor"/>.</summary>
        private bool _restingColorOverridden;
        private bool _defeatHandled;
        private bool _initializing;
        private bool _isInIntro = true;
        private bool _hasIntroTriggered;
        private bool _introHold;
        private float _hopTimer;
        /// <summary>The strip instance parented at the scene root right now - see <see cref="ClearHazard"/>.</summary>
        private BossHazardStrip _lastHazard;

        private BossAttackProfile _queuedChain;
        private float _chainTimer;
        private int _chainStepsTaken;
        private float _chantCooldownTimer;
        private float _chantTimer;
        private float _lastKnownHealth = -1f;
        private float _stanceTimer;
        private int _stanceIndex;

        private const float DefaultIntroHoldTimeout = 2.7f;
        private static readonly float DefaultArenaLeftOffset = World.U(5f);
        private static readonly float DefaultArenaRightOffset = World.U(1.1f);
        private const float DefaultVictoryPresentationDelay = 1.5f;
        /// <summary>What a boss with no design file is stunned for. <c>RainbowChapterBossData.stunDuration</c> is the authored number.</summary>
        private const float DefaultStunDuration = 1f;

        public void SetBossData(RainbowChapterBossData data)
        {
            bossData = data;
            ApplyData();
        }

        public void SetEncounterData(BossEncounterData data) => encounterData = data;

        /// <summary>
        /// Replaces the colour the body settles back to between attacks, in both phases. The telegraph
        /// and phase-two attack tints are untouched - a warning nobody can see is not a warning.
        /// </summary>
        /// <remarks>
        /// Additive. It also has to switch off the phase-two resting colour, because
        /// <c>ApplyRestingColour</c> settles on <c>bossData.SecondaryColor</c> there rather than on
        /// <c>_restingColor</c>, and half a fix would let the tint back in at the phase change.
        /// </remarks>
        public void SetRestingColor(Color color)
        {
            _restingColor = color;
            _restingColorOverridden = true;
        }

        /// <summary>
        /// Hands the boss the scene its hazard strips are instanced from. Injected rather than loaded
        /// here because the visual belongs to the spawner - the same split <see cref="RangedCaster"/>
        /// makes with its projectile, and the reason this class never learns what a sprite looks like.
        /// </summary>
        public void SetHazardPrefab(PackedScene prefab) => hazardPrefab = prefab;

        /// <summary>Hands the boss the scene its afterimages are instanced from. Same injection as the hazard strip.</summary>
        public void SetAfterimagePrefab(PackedScene prefab) => afterimagePrefab = prefab;

        /// <summary>
        /// The strip this boss is currently burning, or null - genuinely null, not a freed Godot object,
        /// so <c>?.</c>, <c>is null</c> and a test's IsNull all agree with <c>== null</c>.
        /// </summary>
        public BossHazardStrip LastHazard => GodotObject.IsInstanceValid(_lastHazard) ? _lastHazard : null;

        /// <summary>
        /// Starts <paramref name="profile"/> now, refusing if the boss is dead, stunned or already
        /// swinging. Public because a scripted encounter - a cutscene, a scripted opener - has to be able
        /// to call a specific attack instead of waiting for the picker to choose it.
        /// </summary>
        public bool RequestAttack(BossAttackProfile profile)
        {
            if (profile == null || IsDefeated || IsAttackRunning || IsStunned)
            {
                return false;
            }

            CurrentAttackProfile = profile;
            _phase = AttackPhase.Telegraph;
            _phaseTimer = profile.TelegraphTime;
            _hasHitThisAttack = false;

            StopMovement();
            ApplyTelegraphColour(profile);

            // On the telegraph, not on the active window: the copies are the fake tell, so they have to
            // be up while the player is still deciding which body to answer.
            CastAfterimages(profile);

            OnAttackRequested?.Invoke();
            return true;
        }

        public void CompleteCurrentAttack()
        {
            if (!IsAttackRunning)
            {
                return;
            }

            float recovery = CurrentAttackProfile.RecoveryTime;
            if (IsPhaseTwo && bossData != null)
            {
                recovery *= bossData.PhaseTwoCooldownMultiplier;
            }

            // The floor is the encounter's punish window: an attack authored with almost no recovery
            // would otherwise leave the player nothing to answer with, which is a fight with no rhythm.
            if (encounterData != null)
            {
                recovery = Mathf.Max(
                    recovery,
                    encounterData.postAttackRecoveryTime * (IsPhaseTwo ? encounterData.phaseTwoRecoveryMultiplier : 1f));
            }

            _attackCooldownTimer = recovery;

            // Before the profile is cleared, and only on an attack that ran to its end. Stun() drops the
            // swing without coming through here, which is what makes an interrupted lunge cost the boss
            // its ground denial as well as its damage.
            if (CurrentAttackProfile.ShouldLeaveHazard(IsPhaseTwo))
            {
                DropHazard(CurrentAttackProfile);
            }

            QueueChain(CurrentAttackProfile);

            CurrentAttackProfile = null;
            _phase = AttackPhase.None;
            Scale = Vector2.One;
            ApplyRestingColour();
            OnAttackCompleted?.Invoke();
        }

        /// <summary>Ends the swing without its recovery, the way a parry or a poise break does.</summary>
        public void Stun(bool perfect = false)
        {
            if (IsDefeated)
            {
                return;
            }

            _stunTimer = (bossData != null ? bossData.stunDuration : DefaultStunDuration)
                * (perfect ? perfectParryStunMultiplier : 1f);

            // The rest of the chain dies with the swing that was carrying it. Interrupting one link and
            // then eating the next three is not a punish window. A poise break is also the loudest way
            // to stop a chant, so it stops one.
            ClearChain();
            EndChant();

            CurrentAttackProfile = null;
            _phase = AttackPhase.None;
            _attackCooldownTimer = 0f;
            Scale = Vector2.One;
            StopMovement();
            ApplyRestingColour();
        }

        public override void _Ready()
        {
            base._Ready();

            _bossHealth = this.FindComponent<Health>();
            _spawnPosition = GlobalPosition;

            visual ??= _sr ?? this.FindComponent<Sprite2D>();

            _bossHealth.OnHealthChanged += OnHealthChanged;
            _bossHealth.OnHealthDepleted += OnHealthDepleted;

            ApplyData();
        }

        public override void _ExitTree()
        {
            base._ExitTree();

            // A strip is a root object, so it is not in the enemies list the respawner sweeps: without
            // this, resting at a checkpoint frees the boss and leaves its fire burning in the arena.
            ClearHazard();

            if (_bossHealth == null)
            {
                return;
            }

            _bossHealth.OnHealthChanged -= OnHealthChanged;
            _bossHealth.OnHealthDepleted -= OnHealthDepleted;
        }

        /// <summary>
        /// Writes the authored stats onto the components. The phase check is suppressed for the length
        /// of it: <see cref="Health.SetMaxHealth"/> and <see cref="Health.SetHealth"/> both raise
        /// OnHealthChanged, and a boss whose max is being raised from the component default reads as
        /// half dead for the instant between the two calls - which started phase two before the fight
        /// did, with the boss at full health.
        /// </summary>
        private void ApplyData()
        {
            if (bossData == null)
            {
                return;
            }

            _initializing = true;

            try
            {
                ApplyDataCore();
            }
            finally
            {
                _initializing = false;
            }

            OnBossInitialized?.Invoke();
        }

        private void ApplyDataCore()
        {
            _bossHealth ??= this.FindComponent<Health>();
            visual ??= this.FindComponent<Sprite2D>();

            // The only writer of this boss's max health, which is why the difficulty and the New Game+
            // lap are folded in here rather than by the spawner. They used to be applied there and then
            // overwritten by this line with the authored number, so seven of the eight bosses fought at
            // their authored health on every difficulty - with a full health bar either way.
            float maxHealth = bossData.MaxHealth * DifficultySettings.EnemyHealthMultiplier;

            _bossHealth.SetMaxHealth(maxHealth);
            _bossHealth.SetHealth(maxHealth);
            _lastKnownHealth = maxHealth;

            // The first chant waits out a full interval, so the fight opens with attacks rather than
            // with the boss healing damage nobody has dealt yet.
            _chantCooldownTimer = bossData.ChantInterval;

            if (visual != null)
            {
                _restingColor = bossData.PrimaryColor;
                visual.Modulate = _restingColor;
                visual.SetSpriteSize(bossData.BodySize);
            }

            if (this.FindComponent<CollisionShape2D>()?.Shape is CapsuleShape2D capsule)
            {
                capsule.Radius = bossData.BodySize.X * 0.5f;
                capsule.Height = bossData.BodySize.Y;
            }

            Name = bossData.BossName;
        }

        public override void _Process(double delta)
        {
            if (IsDefeated)
            {
                return;
            }

            var dt = (float)delta;

            UpdateSensingAndEngagement();

            if (_stunTimer > 0f)
            {
                _stunTimer -= dt;
                return;
            }

            // Ahead of the attack branch, because that branch returns: a stance clock that only ran
            // between swings measured idle time rather than fight time, so an authored six seconds was
            // whole multiples of that in the hand. TickStance still refuses to change shape mid-swing;
            // it just counts while one is in the air.
            TickStance();

            // Before the intro gate on purpose. The boss never picks an attack during its arrival, but
            // an attack handed to it explicitly - a scripted opener staged by a cutscene - has to run,
            // and a swing that starts and then never ticks is a boss frozen mid-telegraph.
            if (IsAttackRunning)
            {
                TickAttack();
                return;
            }

            if (_isInIntro)
            {
                // The arrival does not begin until there is someone to arrive in front of, and nothing
                // is chosen to swing until it ends.
                if (!_hasIntroTriggered && _player != null)
                {
                    _hasIntroTriggered = true;
                    IntroSequence();
                }

                return;
            }

            // Before the cooldown: the gap between links is the chain's own beat, and making it wait out
            // the recovery as well would turn every chain into two unrelated attacks.
            if (TickChain())
            {
                return;
            }

            // After the chain and before the ordinary cooldown: a boss does not stop mid-combo to heal,
            // but it does heal instead of standing in its recovery.
            if (TickChant())
            {
                return;
            }

            if (_attackCooldownTimer > 0f)
            {
                _attackCooldownTimer -= dt;
                return;
            }

            if (_player == null || bossData == null)
            {
                return;
            }

            float distance = Mathf.Abs(_player.GlobalPosition.X - GlobalPosition.X);
            if (distance > bossData.AttackRange)
            {
                ApproachWithinArena();
            }
            else
            {
                RequestAttack(SelectAttack());
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);

            // Unity's LateUpdate. After MoveAndSlide is the same moment: the body has finished moving.
            if (!IsDefeated)
            {
                ClampToArena();
            }
        }

        /// <summary>
        /// The arrival, and the wait for whatever is being staged over it. Capped rather than trusting
        /// the listener: a hold that is never released would leave the boss standing still in a fight
        /// that never starts, and the default with nobody listening is no wait at all.
        /// </summary>
        /// <remarks>
        /// Unscaled, because a cutscene runs at a zero timescale - a scaled wait here would never
        /// advance and the cap would never fire, which is the failure the cap exists to prevent.
        /// </remarks>
        private async void IntroSequence()
        {
            StopMovement();
            OnIntroStart?.Invoke();

            float remaining = encounterData != null ? encounterData.introHoldTimeout : DefaultIntroHoldTimeout;
            while (_introHold && remaining > 0f)
            {
                remaining -= GameClock.UnscaledDeltaTime;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                if (!GodotObject.IsInstanceValid(this))
                {
                    return;
                }
            }

            _introHold = false;
            _isInIntro = false;
            OnIntroEnd?.Invoke();

            TransitionTo(EnemyState.Combat);
        }

        /// <summary>
        /// Skips the arrival. For a boss that is not the chapter's opening encounter - a rematch, a
        /// second phase spawned mid-fight - where an intro would replay a beat the player already had.
        /// </summary>
        public void SkipIntro()
        {
            _hasIntroTriggered = true;
            _introHold = false;

            if (!_isInIntro)
            {
                return;
            }

            _isInIntro = false;
            OnIntroEnd?.Invoke();
        }

        /// <summary>
        /// Telegraph reads, active window can hit once, recovery is the punish. Driven off the frame
        /// delta rather than the physics step so the read matches the frame the player sees, which is the
        /// same bet <see cref="WrathMiniBoss"/> makes and the P0 runner pins there.
        /// </summary>
        private void TickAttack()
        {
            _phaseTimer -= GameClock.DeltaTime;

            if (_phase == AttackPhase.Telegraph)
            {
                PulseTelegraph();

                if (_phaseTimer > 0f)
                {
                    return;
                }

                // The feint is a pause dressed as the end of the attack: resting colour, no pulse. A
                // player reading the flash rather than the timing lets go here, which is the whole
                // lesson chapter three is built to teach.
                if (CurrentAttackProfile.HasFeint)
                {
                    _phase = AttackPhase.Feint;
                    _phaseTimer = CurrentAttackProfile.FeintPauseTime;
                    Scale = Vector2.One;
                    ApplyRestingColour();
                    return;
                }

                OpenActiveWindow();
                return;
            }

            if (_phase == AttackPhase.Feint)
            {
                if (_phaseTimer > 0f)
                {
                    return;
                }

                OpenActiveWindow();
                return;
            }

            if (_phase == AttackPhase.Active)
            {
                DriveLunge();

                // Re-tested every frame of the window, so stepping into a swing that has already begun
                // still connects. The once-per-attack flag is what stops it hitting on every one.
                if (!_hasHitThisAttack)
                {
                    ResolveHit(CurrentAttackProfile);
                }

                if (_phaseTimer <= 0f)
                {
                    CompleteCurrentAttack();
                }
            }
        }

        /// <summary>
        /// Carries the boss forward through the opening of its active window, for an attack that authors
        /// a lunge. The swing keeps its own <see cref="BossAttackProfile.ActiveTime"/>: the lunge is how
        /// far it travels, not how long it stays dangerous, so a short dash on a long window still ends
        /// with the boss standing in its recovery where the player can answer it.
        /// </summary>
        /// <remarks>
        /// Facing is read once, when the window opens, and not re-aimed: a lunge a player can walk out of
        /// by circling is a lunge with no commitment in it, and commitment is the whole trade.
        /// The arena clamp still applies - <see cref="ClampToArena"/> runs after the move step - so a
        /// dash into the wall stops at the wall rather than leaving the fight.
        /// </remarks>
        private void DriveLunge()
        {
            if (!CurrentAttackProfile.Lunges)
            {
                return;
            }

            float elapsed = CurrentAttackProfile.ActiveTime - _phaseTimer;
            if (elapsed > CurrentAttackProfile.LungeDuration)
            {
                Velocity = new Vector2(0f, Velocity.Y);
                return;
            }

            Velocity = new Vector2(_lungeDir * CurrentAttackProfile.LungeSpeed, Velocity.Y);
        }

        /// <summary>Snaps the attack into its active window. Reached from a plain telegraph or from a feint.</summary>
        private void OpenActiveWindow()
        {
            _phase = AttackPhase.Active;
            _phaseTimer = CurrentAttackProfile.ActiveTime;

            // Latched here rather than read each frame, so the dash commits to the direction the
            // telegraph pointed in.
            _lungeDir = _player != null && _player.GlobalPosition.X < GlobalPosition.X ? -1f : 1f;

            Scale = Vector2.One;
            ApplyTelegraphColour(CurrentAttackProfile);
            OnAttackLanded?.Invoke();
            ResolveHit(CurrentAttackProfile);
        }

        private void ResolveHit(BossAttackProfile profile)
        {
            if (profile == null || _hasHitThisAttack)
            {
                return;
            }

            int facing = GetFacingDir();
            Vector2 origin = GlobalPosition + (Vector2.Right * facing * profile.ForwardOffset);

            // A pull is a direction, not a negative force: PlayerController2D drops any knockback whose
            // magnitude is not above zero, so a pull authored as a negative number would do nothing at all.
            int shove = profile.PullsTarget ? -facing : facing;

            foreach (GodotObject hit in Phys2D.OverlapCircleAll(this, origin, profile.Range, World.Layer.Player))
            {
                if (Phys2D.FindActorInGroup(hit, World.Group.Player) is not Node2D player)
                {
                    continue;
                }

                _hasHitThisAttack = true;

                var request = new DamageRequest(
                    this,
                    player,
                    player.GlobalPosition,
                    Vector2.Right * shove,
                    profile.Damage,
                    profile.Knockback,
                    profile.DamageType);

                DamageResult result = CombatResolver.Resolve(request);

                if (result.WasParried)
                {
                    Stun(result.WasPerfectParried);
                    return;
                }

                if (result.Applied && player.FindComponentInParent<CombatResultBroadcaster>() == null)
                {
                    player.FindComponent<CombatFeedback>()?.Play(result);
                }
            }
        }

        /// <summary>
        /// Phase one rotates through the authored order, so the opening of the fight can be learned.
        /// Phase two rolls the weights, so the second half stops being the same loop faster.
        /// </summary>
        private BossAttackProfile SelectAttack()
        {
            BossAttackProfile[] attacks = AttacksInCurrentStance();
            if (attacks == null || attacks.Length == 0)
            {
                return null;
            }

            if (!IsPhaseTwo)
            {
                _rotationIndex = (_rotationIndex + 1) % attacks.Length;
                return attacks[_rotationIndex];
            }

            float total = 0f;
            foreach (BossAttackProfile profile in attacks)
            {
                if (profile != null)
                {
                    total += profile.PhaseTwoWeight;
                }
            }

            // Every weight authored at zero means nobody chose; rotating beats standing still.
            if (total <= 0f)
            {
                _rotationIndex = (_rotationIndex + 1) % attacks.Length;
                return attacks[_rotationIndex];
            }

            float point = GD.Randf() * total;
            foreach (BossAttackProfile profile in attacks)
            {
                if (profile == null)
                {
                    continue;
                }

                point -= profile.PhaseTwoWeight;
                if (point <= 0f)
                {
                    return profile;
                }
            }

            return attacks[attacks.Length - 1];
        }

        /// <summary>
        /// Drops a strip on the ground the attack committed to. Built from the injected body when there
        /// is one and bare when there is not, so the damage half of the mechanic never depends on the
        /// presentation half being wired.
        /// </summary>
        private void DropHazard(BossAttackProfile profile)
        {
            // One strip at a time. A phase-two lunge cycle is shorter than the strip it leaves, so two in
            // a row put two almost fully overlapping circles on ground the boss has barely crossed, and
            // three stack to more damage per second than the arena gives a player room to escape. The
            // new lunge relights the ground rather than adding a second fire to it.
            ClearHazard();

            Vector2 position = GlobalPosition + (Vector2.Right * GetFacingDir() * profile.HazardForwardOffset);

            // Ground denial has to be on the ground. Nothing gates an attack on being grounded and
            // StopMovement only zeroes X, so a swing begun at the top of an approach hop finishes in the
            // air and would leave its fire hanging where no player can be made to walk through it. The
            // boss body's own standing height is the answer: at spawn it stands on the floor, so pinning
            // Y there is exactly where the strip already lands whenever the boss is grounded.
            //
            // ponytail: assumes the arena floor is one flat line, which every SceneLayout ships. A chapter
            // that fights on a platform wants a downward raycast instead - Phys2D.Raycast against
            // Vector2.Down and World.Layer.GroundProbe, the probe LeapingAttacker already makes.
            position.Y = _spawnPosition.Y;

            // Instanced, not duplicated off a template the spawner had deactivated: a PackedScene
            // instance is visible and processing from its first frame, so there is nothing to re-arm.
            var strip = InstantiateEffect<BossHazardStrip>(hazardPrefab, BossHazardStrip.ScenePath);
            if (strip == null)
            {
                return;
            }

            // Parented at the scene root and placed before it enters, never moved afterwards: a strip
            // that slid in from the scene's origin would burn a line across the floor on its frame.
            SpawnRoot().AddChild(strip);
            strip.GlobalPosition = position;

            // The scene carries only the shape; the attack that dropped it owns the colour and the size.
            Sprite2D sprite = strip.FindComponent<Sprite2D>();
            if (sprite != null)
            {
                sprite.Modulate = profile.HazardColor;
                sprite.SetSpriteSize(new Vector2(profile.HazardRadius * 2f, profile.HazardRadius * 2f));
            }

            strip.Configure(this, profile.HazardDamage, profile.HazardRadius, profile.HazardTickInterval, profile.HazardDuration);
            _lastHazard = strip;
        }

        /// <summary>
        /// Lines up the next link of a chain, if the finished attack names one and the chain has not
        /// already run longer than a fight leaves room for.
        /// </summary>
        private void QueueChain(BossAttackProfile finished)
        {
            int maxChainSteps = encounterData != null ? encounterData.maxChainSteps : DefaultMaxChainSteps;
            if (!finished.HasChain || bossData == null || _chainStepsTaken >= maxChainSteps)
            {
                ClearChain();
                return;
            }

            BossAttackProfile next = bossData.FindAttack(finished.ChainNextAttackId);
            if (next == null)
            {
                // Named a row that does not exist. Said out loud because the chain simply ending is what
                // a correctly authored last link looks like, so a typo is otherwise invisible.
                GD.PushWarning(
                    $"{BossName}: attack '{finished.AttackId}' chains to '{finished.ChainNextAttackId}', which is not " +
                    "an attackId on this boss. The chain ends there.");
                ClearChain();
                return;
            }

            _queuedChain = next;
            _chainTimer = finished.ChainDelayFor(IsPhaseTwo);
            _chainStepsTaken++;
        }

        private void ClearChain()
        {
            _queuedChain = null;
            _chainTimer = 0f;
            _chainStepsTaken = 0;
        }

        /// <summary>
        /// Runs the queued link when its beat arrives. Ahead of the ordinary cooldown in
        /// <see cref="_Process"/>, because the delay between links is the chain's own rhythm - waiting
        /// out the recovery first would flatten every chain into two separate attacks.
        /// </summary>
        private bool TickChain()
        {
            if (_queuedChain == null)
            {
                return false;
            }

            _chainTimer -= GameClock.DeltaTime;
            if (_chainTimer > 0f)
            {
                return true;
            }

            BossAttackProfile next = _queuedChain;
            _queuedChain = null;

            // Steps are not reset here: the counter is what stops a loop, and it is cleared when a chain
            // ends or is interrupted.
            RequestAttack(next);
            return true;
        }

        /// <summary>
        /// Runs the chant clock: counts down to one, heals through it, and reports whether the boss is
        /// busy chanting rather than free to attack. Returns false for every boss that never chants.
        /// </summary>
        private bool TickChant()
        {
            if (bossData == null || !bossData.ChantsWhileFighting)
            {
                return false;
            }

            float dt = GameClock.DeltaTime;

            if (IsChanting)
            {
                float heal = bossData.ChantHealPerSecond * dt;
                _bossHealth.Heal(heal);
                HealthRestoredByChanting += heal;

                _chantTimer -= dt;
                if (_chantTimer <= 0f)
                {
                    EndChant();
                }

                StopMovement();
                return true;
            }

            _chantCooldownTimer -= dt;
            if (_chantCooldownTimer > 0f)
            {
                return false;
            }

            _chantTimer = bossData.ChantDuration;
            StopMovement();
            return true;
        }

        /// <summary>
        /// Ends the chant and restarts the wait for the next one. Called both when it runs out and when
        /// the player cuts it short, so an interrupted chant costs the boss the whole interval rather
        /// than resuming where it stopped.
        /// </summary>
        private void EndChant()
        {
            _chantTimer = 0f;
            _chantCooldownTimer = bossData != null ? bossData.ChantInterval : 0f;
        }

        /// <summary>
        /// Walks the stance list on its own clock. Stances rotate in the order they appear in the attack
        /// rows rather than at random: the final chapters are a test of reading, and a boss whose next
        /// shape cannot be anticipated is a test of luck.
        /// </summary>
        private void TickStance()
        {
            if (bossData == null || !bossData.RotatesStances)
            {
                return;
            }

            string[] stances = bossData.StanceIds();
            if (stances.Length == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(CurrentStanceId))
            {
                _stanceIndex = 0;
                CurrentStanceId = stances[0];
                _stanceTimer = bossData.StanceRotationInterval;
                return;
            }

            // Not while a swing is in the air: changing shape mid-attack would move the tell the player
            // is already reading.
            if (IsAttackRunning)
            {
                return;
            }

            _stanceTimer -= GameClock.DeltaTime;
            if (_stanceTimer > 0f)
            {
                return;
            }

            _stanceIndex = (_stanceIndex + 1) % stances.Length;
            CurrentStanceId = stances[_stanceIndex];

            // Phase two compresses the rotation, which is what makes the last stretch a different exam
            // from the first.
            float interval = bossData.StanceRotationInterval;
            if (IsPhaseTwo)
            {
                interval *= bossData.PhaseTwoCooldownMultiplier;
            }

            _stanceTimer = interval;
        }

        /// <summary>
        /// Throws off the harmless copies. Spread evenly to either side, alternating, so a boss with two
        /// stands between them and one with three stands in the middle of them.
        /// </summary>
        private void CastAfterimages(BossAttackProfile profile)
        {
            if (bossData == null || bossData.AfterimageLifetime <= 0f)
            {
                return;
            }

            int count = profile != null ? profile.AfterimageCountOr(bossData.AfterimageCount) : bossData.AfterimageCount;
            if (count <= 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                float step = bossData.AfterimageSpread * ((i / 2) + 1);
                float offset = i % 2 == 0 ? -step : step;

                var image = InstantiateEffect<BossAfterimage>(afterimagePrefab, BossAfterimage.ScenePath);
                if (image == null)
                {
                    continue;
                }

                SpawnRoot().AddChild(image);
                image.GlobalPosition = GlobalPosition + (Vector2.Right * offset);

                image.Configure(
                    visual?.Texture,
                    bossData.PrimaryColor,
                    bossData.BodySize,
                    visual != null ? visual.ZIndex - 1 : 0,
                    bossData.AfterimageLifetime);
            }
        }

        /// <summary>Puts the fire out now, wherever the boss is in its life.</summary>
        private void ClearHazard()
        {
            // The strip is the scene's root, so freeing it takes its sprite with it. A template body the
            // strip merely hung off used to be possible, which is why this needed a second field.
            if (GodotObject.IsInstanceValid(_lastHazard))
            {
                _lastHazard.QueueFree();
            }

            _lastHazard = null;
        }

        /// <summary>
        /// One instance of an effect scene, or null if neither the injected scene nor the shipped one
        /// yields the expected root. Nothing here re-arms visibility or process mode: unlike a
        /// <c>Duplicate()</c> of a deactivated template, a scene instance has never been deactivated.
        /// </summary>
        private static T InstantiateEffect<T>(PackedScene scene, string fallbackPath) where T : Node
        {
            PackedScene source = GodotObject.IsInstanceValid(scene) ? scene : GD.Load<PackedScene>(fallbackPath);
            Node body = source?.Instantiate();

            if (body is T typed)
            {
                return typed;
            }

            GD.PushWarning($"{fallbackPath} did not instance a {typeof(T).Name}; the effect is skipped.");
            body?.QueueFree();
            return null;
        }

        /// <summary>
        /// The rows the boss can reach right now: its current stance, plus every row that belongs to no
        /// stance. A boss with no stance rotation reaches all of them, which is every fight before the
        /// final chapters.
        /// </summary>
        /// <remarks>
        /// Falls back to the whole list if the current stance turns out to hold nothing. A stance with no
        /// rows is an authoring mistake, and the failure it would otherwise cause - a boss that stands
        /// still until the stance rotates - looks like a hang rather than like a typo.
        /// </remarks>
        private BossAttackProfile[] AttacksInCurrentStance()
        {
            BossAttackProfile[] attacks = bossData != null ? bossData.Attacks : null;
            if (attacks == null || attacks.Length == 0)
            {
                return attacks;
            }

            if (string.IsNullOrEmpty(CurrentStanceId))
            {
                return attacks;
            }

            var reachable = new List<BossAttackProfile>();
            foreach (BossAttackProfile profile in attacks)
            {
                if (profile == null || profile.ChainOnly)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(profile.StanceId) || profile.StanceId == CurrentStanceId)
                {
                    reachable.Add(profile);
                }
            }

            return reachable.Count > 0 ? reachable.ToArray() : attacks;
        }

        private void ApproachWithinArena()
        {
            float speed = bossData.MoveSpeed;
            if (IsPhaseTwo)
            {
                speed *= bossData.PhaseTwoSpeedMultiplier;
            }

            float dir = Mathf.Sign(_player.GlobalPosition.X - GlobalPosition.X);
            if (dir > 0f && GlobalPosition.X >= MaxArenaX)
            {
                dir = 0f;
            }
            else if (dir < 0f && GlobalPosition.X <= MinArenaX)
            {
                dir = 0f;
            }

            if (Mathf.IsZeroApprox(dir))
            {
                StopMovement();
                return;
            }

            Velocity = new Vector2(dir * speed, Velocity.Y);
            TickApproachHop();

            if (visual != null)
            {
                visual.FlipH = dir < 0f;
            }
        }

        /// <summary>
        /// The pursuit chapter two is built on: the boss closes in bounds rather than at a constant
        /// crawl, which gives the player something with a rhythm to retreat against.
        /// </summary>
        /// <remarks>
        /// Grounded is inferred from the vertical velocity rather than probed for, because a ground check
        /// would need a mask and a layer this class does not own. The inference is not airtight: vertical
        /// velocity passes through zero at the apex of every hop too. What actually stops a second
        /// impulse up there is <c>_hopTimer</c> - the authored interval has to be longer than the time to
        /// apex, <c>approachHopImpulse / g</c>. Nothing here enforces that relation, so it is guarded as
        /// a test against the shipped numbers instead
        /// (<c>GameplayHazardStripRegressionTests.ChapterTwoHopNumbers_CannotStackAHopAtItsOwnApex</c>);
        /// an interval authored below the apex time gives a boss that climbs, and the arena clamps X but
        /// not Y.
        ///
        /// Y FLIP: the impulse is authored as an upward speed, so it is written as a negative Y here.
        /// </remarks>
        private void TickApproachHop()
        {
            if (bossData == null || !bossData.HopsWhileApproaching)
            {
                return;
            }

            _hopTimer -= GameClock.DeltaTime;
            if (_hopTimer > 0f)
            {
                return;
            }

            if (Mathf.Abs(Velocity.Y) > World.U(0.01f))
            {
                return;
            }

            _hopTimer = bossData.ApproachHopInterval;
            Velocity = new Vector2(Velocity.X, -bossData.ApproachHopImpulse);
        }

        private void ClampToArena()
        {
            float clampedX = Mathf.Clamp(GlobalPosition.X, MinArenaX, MaxArenaX);
            if (Mathf.IsEqualApprox(clampedX, GlobalPosition.X))
            {
                return;
            }

            GlobalPosition = new Vector2(clampedX, GlobalPosition.Y);
            Velocity = new Vector2(0f, Velocity.Y);
        }

        private float MinArenaX =>
            _spawnPosition.X - (encounterData != null ? encounterData.arenaLeftOffset : DefaultArenaLeftOffset);

        private float MaxArenaX =>
            _spawnPosition.X + (encounterData != null ? encounterData.arenaRightOffset : DefaultArenaRightOffset);

        protected override float GetDetectionRange()
        {
            if (encounterData != null)
            {
                return encounterData.detectionRange;
            }

            return bossData != null ? bossData.DetectionRange : base.GetDetectionRange();
        }

        // A poise break opens the same window a perfect parry does, the way every other archetype
        // treats it.
        protected override void OnStaggered() => Stun(true);

        private void PulseTelegraph()
        {
            float pulseSpeed = bossData.telegraphPulseSpeed;
            float pulseAmplitude = bossData.telegraphPulseAmplitude;
            float pulse = 1f + (Mathf.Sin(GameClock.Time * pulseSpeed) * pulseAmplitude);
            Scale = new Vector2(pulse, pulse);
        }

        private void ApplyTelegraphColour(BossAttackProfile profile)
        {
            if (visual != null)
            {
                visual.Modulate = profile.TelegraphColor;
            }
        }

        private void ApplyRestingColour()
        {
            if (visual == null)
            {
                return;
            }

            visual.Modulate = !_restingColorOverridden && IsPhaseTwo && bossData != null
                ? bossData.SecondaryColor
                : _restingColor;
        }

        private void OnHealthChanged(float currentHealth)
        {
            // Damage while chanting ends it, and the boss pays the whole interval before it may try
            // again. Compared against the previous value rather than trusting the caller, because the
            // chant's own healing raises health through this same event - a chant that read its own
            // heal as a hit would cancel itself on its first frame.
            if (!_initializing && IsChanting && currentHealth < _lastKnownHealth)
            {
                EndChant();
            }

            _lastKnownHealth = currentHealth;

            if (_initializing || bossData == null || IsPhaseTwo || IsDefeated)
            {
                return;
            }

            if (_bossHealth.NormalizedHealth > bossData.PhaseTwoHealthThreshold)
            {
                return;
            }

            IsPhaseTwo = true;
            ApplyRestingColour();
            OnPhaseTwoStarted?.Invoke();
        }

        private void OnHealthDepleted()
        {
            if (_defeatHandled)
            {
                return;
            }

            _defeatHandled = true;

            Velocity = Vector2.Zero;

            // Unity switched the Rigidbody2D to Static here; the CharacterBody2D equivalent is the base
            // class's freeze flag, which stops both gravity and the move step.
            _bodyFrozen = true;

            CurrentAttackProfile = null;
            _phase = AttackPhase.None;
            Scale = Vector2.One;
            ClearChain();

            // A boss killed mid-chant would otherwise report IsChanting for the rest of its existence,
            // and anything reading that flag off a corpse reads a ritual that is not happening.
            EndChant();

            // The fight is over, so its ground denial is too. DefeatSequence waits out the victory
            // presentation at a normal timescale, and a strip left burning through it goes on damaging -
            // and can kill - a player the game has already decided won.
            ClearHazard();

            DefeatSequence();
        }

        private async void DefeatSequence()
        {
            await ToSignal(
                GetTree().CreateTimer(encounterData != null ? encounterData.victoryPresentationDelay : DefaultVictoryPresentationDelay),
                SceneTreeTimer.SignalName.Timeout);

            if (!GodotObject.IsInstanceValid(this))
            {
                return;
            }

            OnBossDefeated?.Invoke();
        }
    }
}
