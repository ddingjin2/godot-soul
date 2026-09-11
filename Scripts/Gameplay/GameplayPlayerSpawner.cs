using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Player;

namespace MyGame.Gameplay
{
    /// <summary>
    /// The player, and everything anyone downstream needs off it. Unity's <c>GameObject</c> and
    /// <c>Transform</c> are both the actor root <see cref="Node2D"/> here - which is the
    /// <see cref="PlayerMotor2D"/> body, because in Godot the motor <i>is</i> the CharacterBody2D.
    /// </summary>
    public readonly struct GameplayPlayerContext
    {
        public GameplayPlayerContext(
            Node2D gameObject,
            PlayerController2D controller,
            Health health,
            StaminaSystem stamina,
            HumanityController humanity,
            SinResonanceController sin,
            DeathStateController deathController,
            DebugVisualization debugVisualization,
            // Optional so a synthetic test player, which has neither, can still build a context.
            SoulsWallet wallet = null,
            Poise poise = null)
        {
            GameObject = gameObject;
            Controller = controller;
            Health = health;
            Stamina = stamina;
            Humanity = humanity;
            Sin = sin;
            DeathController = deathController;
            DebugVisualization = debugVisualization;
            Wallet = wallet;
            Poise = poise;
        }

        /// <summary>The actor root. Kept under the Unity name so the callers in this folder did not all have to change.</summary>
        public Node2D GameObject { get; }

        /// <summary>Unity's <c>GameObject.transform</c>. Godot has no separate transform object, so this is the same node.</summary>
        public Node2D Transform => GameObject;
        public PlayerController2D Controller { get; }
        public Health Health { get; }
        public StaminaSystem Stamina { get; }
        public HumanityController Humanity { get; }
        public SinResonanceController Sin { get; }
        public DeathStateController DeathController { get; }
        public DebugVisualization DebugVisualization { get; }
        public SoulsWallet Wallet { get; }
        public Poise Poise { get; }
    }

    /// <summary>
    /// Builds the player actor and tunes it, in an order that matters more than it looks.
    ///
    /// UNITS: every size, offset and radius comes off <see cref="GameplayReadabilityDefaults"/> and
    /// every position off <see cref="GameplaySceneDefaults"/>, both already in Godot pixels with +Y
    /// down. Nothing in this file converts.
    ///
    /// SHAPE: Unity put a dozen components on one GameObject. Here the actor root is the
    /// <see cref="PlayerMotor2D"/> - the port made the motor the <see cref="CharacterBody2D"/> - and
    /// every other component is a child node of it, which is the arrangement
    /// <c>NodeExt.GetComponent</c> and <c>PlayerShim.Get</c> read back.
    /// </summary>
    public static class GameplayPlayerSpawner
    {
        /// <summary>
        /// The authored player. Every node the actor has is in there - body, collider, visual, the
        /// whole component rig, the hitbox anchor with its sword and swing arc, and the world health
        /// bar - so an instance arrives complete and detached, which is what <see cref="Spawn"/>
        /// needs and what the old <c>BuildPlayer</c> spent seventy lines assembling by hand.
        /// </summary>
        private const string PlayerScenePath = "res://Scenes/Actors/Player.tscn";

        private const float StartingHealth = 100f;
        private const float StartingHumanity = 100f;

        public static GameplayPlayerContext Spawn(
            GameplaySceneDefaults scene,
            GameplayReadabilityDefaults readability,
            Node2D checkpoint,
            Node2D spiritPlatform)
        {
            GameplayTuningCatalog tuning = GameplayTuningCatalog.Load();
            PlayerResourceData resources = tuning?.PlayerResources;
            float maxHealth = resources != null ? resources.maxHealth : StartingHealth;
            float startingHealth = resources != null ? resources.startingHealth : StartingHealth;
            float startingHumanity = resources != null ? resources.startingHumanity : StartingHumanity;

            // Built detached and parented last. Every component's _Ready looks for its siblings, and a
            // node that entered the tree before they existed would find none of them - so the whole
            // hierarchy goes in as one piece, already standing at its spawn point.
            PlayerMotor2D go = BuildPlayer(readability);
            go.Position = scene.PlayerSpawnPosition;
            GameplayBuildShim.SceneRoot?.AddChild(go);

            var sr = go.GetComponent<Sprite2D>();

            var health = go.GetComponent<Health>();
            health.SetMaxHealth(maxHealth);
            health.SetHealth(startingHealth);
            EnsureDamageReceiver(go, health);

            var stamina = go.GetComponent<StaminaSystem>();
            stamina.ApplyTuning(resources);
            stamina.SetStamina(stamina.MaxStamina);

            go.GetComponent<CombatFeedback>().SetOriginalColor(readability.PlayerColor);
            EnsureCombatResultBridge(go);

            // The anchor and the hitbox are one node: Unity's hitbox GameObject carried both the
            // DamageHitbox2D and the trigger collider, and a DamageHitbox2D is an Area2D here.
            var hitbox = go.GetNode<DamageHitbox2D>("HitboxAnchor");
            hitbox.Configure(readability.PlayerHitboxRadius, readability.PlayerHitboxOffset, World.Layer.Enemy);

            go.ApplyTuning(tuning?.PlayerMovement);
            var actions = go.GetComponent<PlayerActionController>();
            actions.ApplyTuning(tuning?.PlayerCombat);
            actions.ApplyResourceTuning(resources);

            var player = go.GetComponent<PlayerController2D>();
            player.SetVisual(sr);
            player.SetHitboxAnchor(hitbox);

            // Last of the three, and it has to be. PlayerController2D._Ready re-runs
            // actions.Initialize with *its own* serialized damageHitbox, which Scenes/Actors/Player.tscn
            // leaves unset on purpose - so a hitbox handed to the action controller any earlier is
            // clobbered the moment the player enters the tree. This call puts it back afterwards. See
            // the trap comment in PlayerController2D._Ready, and the header of Player.tscn, before
            // moving it.
            player.SetDamageHitbox(hitbox);

            var attackAnimator = hitbox.GetNode("ReadableSword").GetComponentInChildren<PlayerAttackAnimator2D>();
            attackAnimator.Initialize(player, attackAnimator.GetComponent<AnimationPlayer>());

            var humanity = go.GetComponent<HumanityController>();
            if (resources != null)
            {
                humanity.Configure(resources.maxHumanity, resources.lowHumanityThreshold,
                    resources.humanityLossOnHit, resources.humanityRegenRate, resources.humanityRegenDelay);
            }
            humanity.SetHumanity(startingHumanity);

            var sin = go.GetComponent<SinResonanceController>();
            sin.Initialize(humanity);
            sin.ApplyTuning(tuning?.SinTuning);

            EnsureLockOn(go, tuning?.WorldTuning);

            go.GetComponent<GameplayFallDeath>().Initialize(scene.FallDeathY);

            AddHealthBar(go, health, readability.PlayerHealthBarSize, readability.PlayerHealthBarOffset, readability.PlayerHealthBarColor);

            DeathStateController deathController = go.EnsureComponent<DeathStateController>();
            deathController.ApplyTuning(resources);
            deathController.Initialize(health, humanity, checkpoint, spiritPlatform);
            player.SetDeathStateController(deathController);

            DebugVisualization debugVisualization = go.EnsureComponent<DebugVisualization>();
            debugVisualization.Initialize(player, checkpoint, spiritPlatform);

            Poise poise = EnsurePoise(go, resources);
            SoulsWallet wallet = EnsureSoulsWallet(go);

            // Last, and that is the whole requirement. PlayerProgression captures its four bases off the
            // live components on its first use and then writes every stat as base + perLevel * level, so
            // binding it any earlier would capture the untuned numbers instead of the authored ones and
            // every level would be measured from the wrong floor. It has to come after
            // health.SetMaxHealth, stamina.ApplyTuning, actions.ApplyResourceTuning and poise.Configure -
            // and after the wallet, which is what it spends. Read PlayerProgression.EnsureBound before
            // moving this line.
            PlayerProgression.EnsureOn(go);

            GameplaySoulDrop soulDrop = go.EnsureComponent<GameplaySoulDrop>();
            if (resources == null)
                GD.PushError("GameplayPlayerSpawner: Design/PlayerResources.json is missing; the soul stain is not wired and nothing is dropped on death.");
            else
                soulDrop.Initialize(wallet, deathController, player, resources.soulStainPickupDelay);

            // Cosmetic and last: pixel frames when the player has them, the greybox breathing when it
            // does not. Both only read what every line above has finished writing.
            ActorAnimationDriver.AttachTo(go, "Player");

            return new GameplayPlayerContext(go, player, health, stamina, humanity, sin, deathController, debugVisualization, wallet, poise);
        }

        /// <summary>
        /// Added here rather than in the seed hierarchy so lock-on arrives on any player the spawner is
        /// handed, the same way Poise and the souls wallet did.
        /// </summary>
        private static void EnsureLockOn(Node target, WorldTuningData world)
        {
            PlayerLockOn lockOn = target.EnsureComponent<PlayerLockOn>();
            target.EnsureComponent<GameplayLockOnMarker>();

            // Both reaches are WorldTuning.json's, already in pixels. No constant behind them: a build
            // with no design file gets a lock-on that grabs nothing and an error saying why, rather than
            // a reach nobody authored (PLAN_CLOSEOUT A4, decision D1).
            if (world == null)
            {
                GD.PushError("GameplayPlayerSpawner: Design/WorldTuning.json is missing; lock-on keeps no reach.");
                return;
            }

            lockOn.Configure(world.lockOnRange, world.lockOnBreakRange);
        }

        private static Poise EnsurePoise(Node target, PlayerResourceData resources)
        {
            Poise poise = target.EnsureComponent<Poise>();

            if (resources != null)
                poise.Configure(resources.maxPoise, resources.poiseHeavyMultiplier, resources.poiseRegenDelay, resources.poiseRegenRate);

            return poise;
        }

        /// <summary>The player's wallet starts empty; it fills from kills and empties on death.</summary>
        private static SoulsWallet EnsureSoulsWallet(Node target)
        {
            SoulsWallet wallet = target.EnsureComponent<SoulsWallet>();
            wallet.SetSouls(0);
            return wallet;
        }

        /// <summary>
        /// The whole player hierarchy, detached from the tree - one instance of
        /// <c>Scenes/Actors/Player.tscn</c> with the designer-owned half bound over what the scene
        /// ships. The Unity player was a Rigidbody2D with freezeRotation, interpolation and Continuous
        /// collision; a CharacterBody2D never rotates, is interpolated by the engine and resolves its
        /// own sweep in MoveAndSlide, so none of the three has a property to author or to write.
        ///
        /// Every [Export] on the instance is left as the scene ships it, which is null - and that is
        /// deliberate for <c>PlayerController2D.damageHitbox</c>. Wiring it in the scene would defeat
        /// the trap that <see cref="Spawn"/>'s <c>SetDamageHitbox</c> call exists to work around; see
        /// the comment there and in <c>PlayerController2D._Ready</c>.
        /// </summary>
        private static PlayerMotor2D BuildPlayer(GameplayReadabilityDefaults readability)
        {
            var go = GD.Load<PackedScene>(PlayerScenePath).Instantiate<PlayerMotor2D>();

            // The scene root already ships this name; keeping the assignment is what stops the const
            // and the scene from drifting apart in silence.
            go.Name = GameplayBootstrap.PlayerObjectName;

            ResizeCapsuleCollider(go, readability.PlayerColliderSize);

            DressSprite(
                go.GetNode<Sprite2D>("Visual"),
                readability.PlayerColor,
                readability.PlayerVisualSize,
                readability.PlayerSortingOrder);

            // The readable sword under the hitbox anchor, and the swing arc beside it. Their local
            // positions, the sword's rotation and both pivots are authored; the colours are the
            // artist's (Resources/Art/Readability.json), so those - and the two sizes with them - stay
            // bound per spawn.
            var hitbox = go.GetNode<DamageHitbox2D>("HitboxAnchor");

            DressSprite(
                hitbox.GetNode<Sprite2D>("ReadableSword"),
                readability.SwordColor,
                readability.SwordSize,
                readability.SwordSortingOrder);

            DressSprite(
                hitbox.GetNode<Sprite2D>("AttackArc"),
                readability.PlayerAttackReadoutColor,
                readability.AttackArcSize,
                readability.PlayerReadoutSortingOrder);

            return go;
        }

        /// <summary>
        /// Resizes the capsule the player scene already carries. The shape is marked
        /// <c>resource_local_to_scene</c> in <c>Player.tscn</c>, so this writes one player's body and
        /// not every instance's.
        /// </summary>
        private static void ResizeCapsuleCollider(Node2D go, Vector2 size)
        {
            if (go.GetNode<CollisionShape2D>("Collider").Shape is not CapsuleShape2D capsule)
                return;

            // Unity's CapsuleCollider2D.size is the full width and height; Godot wants a radius.
            capsule.Radius = size.X * 0.5f;
            capsule.Height = size.Y;
        }

        /// <summary>
        /// The designer-owned half of an authored sprite. The texture and the Unity pivot (baked into
        /// the scene as an <c>Offset</c>) live in the <c>.tscn</c>; the colour comes from
        /// <c>Resources/Art/Readability.json</c> and the size and sorting order from
        /// <see cref="GameplayReadabilityDefaults"/>, so all three stay bound per spawn.
        /// </summary>
        private static void DressSprite(Sprite2D sr, Color color, Vector2 size, int sortingOrder)
        {
            sr.SetSpriteSize(size);
            sr.Modulate = color;
            sr.ZIndex = sortingOrder;
        }

        private static void AddHealthBar(Node target, Health health, Vector2 size, Vector2 offset, Color color)
        {
            GameplayWorldHealthBar bar = target.EnsureComponent<GameplayWorldHealthBar>();
            bar.Initialize(health, size, offset, color);
        }

        private static DamageReceiver EnsureDamageReceiver(Node target, Health health)
        {
            DamageReceiver receiver = target.EnsureComponent<DamageReceiver>();
            receiver.Initialize(health);
            return receiver;
        }

        private static void EnsureCombatResultBridge(Node target)
        {
            target.EnsureComponent<CombatResultBroadcaster>();
        }
    }
}
