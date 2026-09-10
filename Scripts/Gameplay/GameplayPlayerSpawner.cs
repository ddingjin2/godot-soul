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
            // actions.Initialize with *its own* serialized damageHitbox, which is null on a player built
            // from code - so a hitbox handed to the action controller any earlier is clobbered the
            // moment the player enters the tree. This call puts it back afterwards. See the trap comment
            // in PlayerController2D._Ready before moving it.
            player.SetDamageHitbox(hitbox);

            var attackAnimator = hitbox.GetNode("ReadableSword").GetComponentInChildren<PlayerAttackAnimator2D>();
            attackAnimator.Initialize(player, attackAnimator.GetComponent<AnimationPlayer>());

            var humanity = go.GetComponent<HumanityController>();
            humanity.SetHumanity(startingHumanity);

            var sin = go.GetComponent<SinResonanceController>();
            sin.Initialize(humanity);
            sin.ApplyTuning(tuning?.SinTuning);

            EnsureLockOn(go, tuning?.WorldTuning);

            go.GetComponent<GameplayFallDeath>().Initialize(scene.FallDeathY);

            AddHealthBar(go, health, readability.PlayerHealthBarSize, readability.PlayerHealthBarOffset, readability.PlayerHealthBarColor);

            DeathStateController deathController = go.EnsureComponent<DeathStateController>();
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
            soulDrop.Initialize(wallet, deathController, player,
                resources != null ? resources.soulStainPickupDelay : GameplayTuningDefaults.SoulStainPickupDelay);

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

            lockOn.Configure(
                world != null ? world.lockOnRange : GameplayTuningDefaults.LockOnRange,
                world != null ? world.lockOnBreakRange : GameplayTuningDefaults.LockOnBreakRange);

            target.EnsureComponent<GameplayLockOnMarker>();
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
        /// The whole player hierarchy, detached from the tree. Unity built this only when no player
        /// prefab existed; there are no prefabs in this port, so it is the only path and
        /// <c>BypassPrefabs</c> - which existed for the editor prefab extractor - is gone with them.
        /// </summary>
        private static PlayerMotor2D BuildPlayer(GameplayReadabilityDefaults readability)
        {
            // The Unity player was a Rigidbody2D with freezeRotation, interpolation and Continuous
            // collision. A CharacterBody2D never rotates, is interpolated by the engine and resolves its
            // own sweep in MoveAndSlide, so all three settings have no counterpart to write.
            var go = new PlayerMotor2D
            {
                Name = GameplayBootstrap.PlayerObjectName,
                CollisionLayer = World.Layer.Player,
                CollisionMask = World.Layer.GroundProbe | World.Layer.Enemy,
            };

            CollisionShape2D col = go.AddComponent<CollisionShape2D>("Collider");
            col.Shape = new CapsuleShape2D
            {
                Radius = readability.PlayerColliderSize.X * 0.5f,
                Height = readability.PlayerColliderSize.Y,
            };

            Sprite2D sr = go.AddComponent<Sprite2D>("Visual");
            GameplayVisualFactory.Dress(
                sr,
                GameplayVisualFactory.CreateActorSprite(GameplayVisualFactory.ActorSpriteKind.Player),
                readability.PlayerVisualSize,
                GameplayVisualFactory.Pivot(GameplayVisualFactory.ActorSpriteKind.Player));
            sr.Modulate = readability.PlayerColor;
            sr.ZIndex = readability.PlayerSortingOrder;

            go.AddComponent<Health>();
            go.AddComponent<StaminaSystem>();
            go.AddComponent<Poise>();
            go.AddComponent<SoulsWallet>();
            go.AddComponent<CombatFeedback>();
            go.AddComponent<AudioFeedback>();

            // One node, not two: the anchor and the damage volume were the same GameObject in Unity, and
            // DamageHitbox2D is an Area2D. It runs its own overlap query every frame rather than reading
            // signals, so it needs no CollisionShape2D of its own - the radius passed to Configure is
            // the query.
            var hitbox = new DamageHitbox2D
            {
                Name = "HitboxAnchor",
                Position = readability.PlayerHitboxAnchorLocalPosition,
                CollisionLayer = World.Layer.PlayerHitbox,
                CollisionMask = World.Layer.Enemy,
            };
            go.AddChild(hitbox);

            go.AddComponent<PlayerActionController>();
            // PlayerStateMachine is plain logic in this port, not a node - PlayerController2D owns one
            // directly, so there is nothing to add for it here.
            go.AddComponent<PlayerController2D>();

            CreatePlayerSword(hitbox, readability);
            CreateAttackReadout(
                hitbox,
                "AttackArc",
                readability.AttackArcLocalPosition,
                readability.AttackArcSize,
                readability.PlayerAttackReadoutColor,
                readability.PlayerReadoutSortingOrder);

            go.AddComponent<HumanityController>();
            go.AddComponent<SinResonanceController>();
            go.AddComponent<PlayerInputReceiver>();
            go.AddComponent<GameplayFallDeath>();
            go.AddComponent<GameplayWorldHealthBar>();
            go.AddComponent<DeathStateController>();
            go.AddComponent<DebugVisualization>();
            go.AddComponent<GameplaySoulDrop>();

            return go;
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

        /// <summary>
        /// The readable sword under the hitbox anchor. Unity had one GameObject carrying the sprite, the
        /// Animator and PlayerAttackAnimator2D; here the sprite is the node and the animator pair hangs
        /// under it, because PlayerAttackAnimator2D finds its AnimationPlayer among its own children.
        /// </summary>
        private static void CreatePlayerSword(Node2D parent, GameplayReadabilityDefaults readability)
        {
            var sword = new Sprite2D
            {
                Name = "ReadableSword",
                Position = readability.SwordLocalPosition,
                Rotation = readability.SwordLocalRotation,
            };
            parent.AddChild(sword);

            GameplayVisualFactory.Dress(
                sword,
                GameplayVisualFactory.CreateSwordSprite(),
                readability.SwordSize,
                GameplayVisualFactory.Pivot(GameplayVisualFactory.SpriteKind.Sword));
            sword.Modulate = readability.SwordColor;
            sword.ZIndex = readability.SwordSortingOrder;

            PlayerAttackAnimator2D animator = sword.AddComponent<PlayerAttackAnimator2D>();

            // Unity loaded a RuntimeAnimatorController asset here. There is no swing clip in this port
            // yet, so the AnimationPlayer is built empty: PlayerAttackAnimator2D checks HasAnimation
            // before it plays, so an empty player is silent rather than broken, and dropping a clip
            // named "Attack" into it is all this needs later.
            animator.AddComponent<AnimationPlayer>();
        }

        private static void CreateAttackReadout(Node2D parent, string name, Vector2 localPosition, Vector2 size, Color color, int sortingOrder)
        {
            var go = new Sprite2D { Name = name, Position = localPosition };
            parent.AddChild(go);

            GameplayVisualFactory.Dress(go, GameplayVisualFactory.CreateDiscSprite(), size, GameplayVisualFactory.Pivot(GameplayVisualFactory.SpriteKind.Disc));
            go.Modulate = color;
            go.ZIndex = sortingOrder;

            go.AddComponent<GameplayTelegraphPulse>();
        }
    }
}
