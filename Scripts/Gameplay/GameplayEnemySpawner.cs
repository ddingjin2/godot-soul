using System.Collections.Generic;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;

namespace MyGame.Gameplay
{
    public readonly struct GameplayEnemyContext
    {
        public GameplayEnemyContext(IReadOnlyList<Node2D> enemies, IBossEncounter boss)
        {
            Enemies = enemies;
            Boss = boss;
        }

        /// <summary>Unity's <c>IReadOnlyList&lt;GameObject&gt;</c>: the actor roots, which are Node2D bodies here.</summary>
        public IReadOnlyList<Node2D> Enemies { get; }

        /// <summary>
        /// The arena's boss as an encounter rather than as a type. Everything downstream - the victory
        /// hook, the cutscene trigger, the camera binding - only ever asked it whether it had arrived
        /// and whether it was over, which is exactly <see cref="IBossEncounter"/>.
        /// </summary>
        public IBossEncounter Boss { get; }
    }

    /// <summary>
    /// Every enemy in the arena: one authored scene per archetype, instanced and then tuned.
    ///
    /// UNITS: sizes, offsets and positions arrive already in Godot pixels with +Y down, from
    /// <see cref="GameplayReadabilityDefaults"/> and <see cref="GameplaySceneDefaults"/>; enemy tuning
    /// arrives in pixels too, because <c>EnemyTuningData.ScaleToPixels</c> runs at load. Nothing here
    /// converts anything.
    ///
    /// SHAPE: the Unity actor was a Rigidbody2D GameObject carrying a behaviour component. The port
    /// made <c>EnemyStateMachine</c> the <see cref="CharacterBody2D"/>, so the behaviour <i>is</i> the
    /// actor root and everything else - health, poise, wallet, readouts - is a child of it. That tree
    /// lives in <c>Scenes/Actors/</c> now: <c>EnemyBase.tscn</c> plus one inheriting scene per
    /// archetype. What is left in this file is the <i>tuning</i>, and the order it happens in - which
    /// a scene cannot express and which is load-bearing in two places (see
    /// <see cref="EnsureEnemyHealthRig"/> and <see cref="PlaceInWorld"/>).
    /// </summary>
    public static class GameplayEnemySpawner
    {
        // The authored actors. Each archetype scene inherits Scenes/Actors/EnemyBase.tscn, so the
        // shared rig - collider, visual, health, poise, wallet, role marker, health bar - is declared
        // once and every scene below is only what that archetype adds on top.
        private const string MeleeGruntScenePath = "res://Scenes/Actors/MeleeGrunt.tscn";
        private const string LeapingAttackerScenePath = "res://Scenes/Actors/LeapingAttacker.tscn";
        private const string RangedCasterScenePath = "res://Scenes/Actors/RangedCaster.tscn";
        private const string WrathMiniBossScenePath = "res://Scenes/Actors/WrathMiniBoss.tscn";
        private const string ChapterBossScenePath = "res://Scenes/Actors/ChapterBoss.tscn";

        /// <summary>Who the <see cref="GameplayBuildShim.RequireComponent{T}"/> error lines name.</summary>
        private const string Owner = nameof(GameplayEnemySpawner);

        public static GameplayEnemyContext Spawn(
            GameplaySceneDefaults scene,
            GameplayReadabilityDefaults readability,
            GameplayPlayerContext player)
        {
            _ = player;

            GameplayTuningCatalog tuning = GameplayTuningCatalog.Load();
            var enemies = new List<Node2D>();

            // An arena can hold nothing but its boss. Every archetype is a list now, but a layout written
            // before they were still falls back to one leaper and one caster - so this stays the way a
            // chapter says "just the fight".
            if (scene.SpawnApproachEnemies)
            {
                foreach (GameplaySceneDefaults.EnemySpawn spawn in Placements(scene.MeleeGruntSpawns))
                    enemies.Add(CreateMeleeGruntAt(spawn, readability, tuning));

                foreach (GameplaySceneDefaults.EnemySpawn spawn in Placements(scene.LeapingAttackerSpawns))
                    enemies.Add(CreateLeapingAttackerAt(spawn, readability, tuning));

                foreach (GameplaySceneDefaults.EnemySpawn spawn in Placements(scene.RangedCasterSpawns))
                    enemies.Add(CreateRangedCasterAt(spawn, readability, tuning));
            }

            IBossEncounter boss = CreateBoss(scene, readability, tuning);
            enemies.Add(boss.BossObject);

            // One place rather than six: every archetype and both boss paths land in this list, and
            // GameplayEnemyRespawner rebuilds through this same method, so a respawned enemy is dressed
            // too. A boss built straight through CreateChapterBoss - which is only the tests - is not,
            // and does not need to be.
            foreach (Node2D enemy in enemies)
                ActorAnimationDriver.AttachTo(enemy, ActorKeyFor(enemy));

            return new GameplayEnemyContext(enemies, boss);
        }

        private static GameplaySceneDefaults.EnemySpawn[] Placements(GameplaySceneDefaults.EnemySpawn[] spawns)
        {
            return spawns ?? System.Array.Empty<GameplaySceneDefaults.EnemySpawn>();
        }

        /// <summary>
        /// Which folder under <c>Resources/PixelActors</c> dresses this enemy. Keyed off the behaviour
        /// rather than the node's name, because a chapter boss is named after the boss it is -
        /// "Crimson Warden", not "Boss" - and every boss shares one wardrobe in this first pass.
        /// </summary>
        private static string ActorKeyFor(Node2D enemy)
        {
            if (enemy == null)
                return null;
            if (enemy.GetComponent<MeleeGrunt>() != null)
                return "MeleeGrunt";
            if (enemy.GetComponent<LeapingAttacker>() != null)
                return "LeapingAttacker";
            if (enemy.GetComponent<RangedCaster>() != null)
                return "RangedCaster";

            return "Boss";
        }

        // The three bare-position entry points below are kept: they were the shape the editor prefab
        // extractor called by name, and they still say what a placement-free spawn means. The
        // placement-aware bodies are the *At methods; these are one line each on purpose.
        private static MeleeGrunt CreateMeleeGrunt(Vector2 position, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            return CreateMeleeGruntAt(new GameplaySceneDefaults.EnemySpawn(position, null), readability, catalog);
        }

        private static LeapingAttacker CreateLeapingAttacker(Vector2 position, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            return CreateLeapingAttackerAt(new GameplaySceneDefaults.EnemySpawn(position, null), readability, catalog);
        }

        private static RangedCaster CreateRangedCaster(Vector2 position, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            return CreateRangedCasterAt(new GameplaySceneDefaults.EnemySpawn(position, null), readability, catalog);
        }

        private static MeleeGrunt CreateMeleeGruntAt(GameplaySceneDefaults.EnemySpawn spawn, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            MeleeGrunt go = InstantiateEnemy<MeleeGrunt>(MeleeGruntScenePath, GameplayPrefabNames.MeleeGrunt);
            ResizeCapsuleCollider(go, readability.MeleeColliderSize);
            DressActorVisual(go, readability.EnemyColor, readability.MeleeVisualSize, readability.ActorSortingOrder);

            Node2D attackPoint = go.GetNode<Node2D>("AttackPoint");

            DressAttackReadout(go, "SlashDanger", readability.MeleeDangerLocalPosition, readability.MeleeDangerSize, readability.MeleeDangerColor, readability.EnemyReadoutSortingOrder);
            DressRoleMarker(go, "Melee", readability.MeleeRoleLocalPosition, readability.MeleeRoleColor, readability);

            MeleeGruntData tuning = SpawnTuning<MeleeGruntData>(spawn) ?? catalog.MeleeGrunt;

            go.SetTuningData(tuning);
            go.SetAttackPoint(attackPoint);

            ApplyEnemyHealth(go, tuning.maxHealth, readability.EnemyHealthBarSize, readability.MeleeHealthBarOffset, readability.EnemyHealthBarColor, true);
            ApplyEnemyPoiseAndReward(go, tuning);

            // Parented last: every component has to exist before _Ready runs on the actor.
            PlaceInWorld(go, spawn.Position);

            return go;
        }

        private static LeapingAttacker CreateLeapingAttackerAt(GameplaySceneDefaults.EnemySpawn spawn, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            LeapingAttacker go = InstantiateEnemy<LeapingAttacker>(LeapingAttackerScenePath, GameplayPrefabNames.LeapingAttacker);
            ResizeCapsuleCollider(go, readability.LeaperColliderSize);
            DressActorVisual(go, readability.LeaperColor, readability.LeaperVisualSize, readability.ActorSortingOrder);

            DressAttackReadout(go, "LeapLandingDanger", readability.LeapDangerLocalPosition, readability.LeapDangerSize, readability.LeapDangerColor, readability.EnemyReadoutSortingOrder);
            DressRoleMarker(go, "Leap", readability.LeapRoleLocalPosition, readability.LeapRoleColor, readability);

            LeapingAttackerData tuning = SpawnTuning<LeapingAttackerData>(spawn) ?? catalog.LeapingAttacker;

            go.SetTuningData(tuning);

            ApplyEnemyHealth(go, tuning.maxHealth, readability.EnemyHealthBarSize, readability.LeaperHealthBarOffset, readability.EnemyHealthBarColor, true);
            ApplyEnemyPoiseAndReward(go, tuning);

            // Parented last: every component has to exist before _Ready runs on the actor.
            PlaceInWorld(go, spawn.Position);

            return go;
        }

        private static RangedCaster CreateRangedCasterAt(GameplaySceneDefaults.EnemySpawn spawn, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            RangedCaster go = InstantiateEnemy<RangedCaster>(RangedCasterScenePath, GameplayPrefabNames.RangedCaster);
            ResizeCapsuleCollider(go, readability.CasterColliderSize);
            DressActorVisual(go, readability.CasterColor, readability.CasterVisualSize, readability.ActorSortingOrder);

            DressAttackReadout(go, "CastRange", readability.CastDangerLocalPosition, readability.CastDangerSize, readability.CastDangerColor, readability.EnemyReadoutSortingOrder);
            DressRoleMarker(go, "Cast", readability.CastRoleLocalPosition, readability.CastRoleColor, readability);

            // The shot is a scene now, not a template built here and duplicated per shot. Colour and
            // sorting order still travel: they are readability's, so Resources/Art/Readability.json
            // keeps overriding what the scene ships with.
            go.SetProjectilePrefab(
                GD.Load<PackedScene>(EnemyProjectile.ScenePath),
                readability.ProjectileColor,
                readability.ProjectileSortingOrder);

            RangedCasterData tuning = SpawnTuning<RangedCasterData>(spawn) ?? catalog.RangedCaster;

            go.SetTuningData(tuning);

            ApplyEnemyHealth(go, tuning.maxHealth, readability.EnemyHealthBarSize, readability.CasterHealthBarOffset, readability.EnemyHealthBarColor, true);
            ApplyEnemyPoiseAndReward(go, tuning);

            // Parented last: every component has to exist before _Ready runs on the actor.
            PlaceInWorld(go, spawn.Position);

            return go;
        }

        /// <summary>Where <c>Resources/Design</c> lives from a resource load's point of view.</summary>
        private const string DesignResourceFolder = "Design/";

        private static readonly Dictionary<string, EnemyTuningData> SpawnTuningByFile = new();
        private static readonly HashSet<string> MissingSpawnTuningFiles = new();

        /// <summary>
        /// The tuning a single placement named, or null for "use the archetype's shared numbers", which
        /// is every placement shipped today. This is the whole of the variant hook: an enemy that hits
        /// harder in the second half of a level is a JSON file and a line in the layout, not a class.
        /// </summary>
        /// <remarks>
        /// Loaded here rather than through <see cref="GameplayTuningCatalog"/> because the catalog names
        /// the files it knows about one property at a time, and a per-spawn file is named by the layout
        /// at runtime. Cached because <see cref="GameplayEnemyRespawner"/> rebuilds every enemy on every
        /// death and every rest, and a fresh tuning resource per enemy per respawn is an unbounded leak.
        /// </remarks>
        private static T SpawnTuning<T>(GameplaySceneDefaults.EnemySpawn spawn) where T : EnemyTuningData, new()
        {
            string dataFile = spawn.DataFile;
            if (string.IsNullOrWhiteSpace(dataFile) || MissingSpawnTuningFiles.Contains(dataFile))
                return null;

            // Unity needed a null check ahead of the type test because a destroyed Unity object is still
            // a live C# reference. A Godot Resource is an ordinary managed object, so the type test alone
            // is enough here.
            if (SpawnTuningByFile.TryGetValue(dataFile, out EnemyTuningData cached) && cached is T reused)
                return reused;

            // Deserialized into the archetype's own data type rather than the shared base, so a variant
            // file can carry the fields that archetype actually reads - a grunt's telegraph pulse, a
            // leaper's arc - instead of only the four everything shares.
            var data = Res.LoadJson<T>(DesignResourceFolder + dataFile, required: false);
            if (data == null)
            {
                // Said once rather than on every respawn, and said at all: a placement whose data file was
                // renamed would otherwise fight with the archetype's numbers and look like bad tuning.
                GD.PushError($"GameplayEnemySpawner: a spawn names enemy data '{dataFile}', which is not in Resources/Design. Falling back to the shared archetype tuning.");
                MissingSpawnTuningFiles.Add(dataFile);
                return null;
            }

            // Same metres-to-pixels pass every archetype file gets through its own Load. Without it this
            // one variant would be the only enemy in the game measured in metres.
            data.ScaleToPixels();

            SpawnTuningByFile[dataFile] = data;
            return data;
        }

        /// <summary>
        /// The arena decides which boss stands in it. With no boss file named this is the shipped
        /// chapter-one fight, unchanged. Naming one builds an authored chapter boss from that data
        /// instead, with no code path of its own to keep in step.
        /// </summary>
        private static IBossEncounter CreateBoss(
            GameplaySceneDefaults scene, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            RainbowChapterBossData chapterData = catalog?.ChapterBoss(scene.BossDataFile);

            if (chapterData == null)
            {
                // Named a file that does not load: say so rather than quietly serving chapter one, which
                // would read as "my boss data is being ignored" with no way to tell why.
                if (!string.IsNullOrEmpty(scene.BossDataFile))
                    GD.PushError($"GameplayEnemySpawner: scene layout names boss data '{scene.BossDataFile}', which is not in Resources/Design. Falling back to WrathMiniBoss.");

                return CreateWrathMiniBoss(scene.WrathMiniBossSpawnPosition, readability, catalog);
            }

            return CreateChapterBoss(
                scene.WrathMiniBossSpawnPosition,
                readability,
                chapterData,
                ResolveEncounter(scene, catalog));
        }

        /// <summary>
        /// The room the fight happens in. An arena that names its own encounter file gets it; one that
        /// does not falls back to chapter one's, which is what every arena did before a second chapter
        /// existed. Without this a chapter-two boss would silently inherit chapter one's arena reach and
        /// punish window - the two numbers that decide what a fight feels like.
        /// </summary>
        private static BossEncounterData ResolveEncounter(GameplaySceneDefaults scene, GameplayTuningCatalog catalog)
        {
            if (catalog == null)
                return null;

            if (string.IsNullOrEmpty(scene.BossEncounterFile))
                return catalog.WrathEncounter;

            BossEncounterData encounter = catalog.ChapterEncounter(scene.BossEncounterFile);
            if (encounter != null)
                return encounter;

            GD.PushError($"GameplayEnemySpawner: scene layout names boss encounter '{scene.BossEncounterFile}', which is not in Resources/Design. Falling back to the chapter-one encounter.");
            return catalog.WrathEncounter;
        }

        /// <summary>
        /// Builds an authored chapter boss: greybox body from the readability defaults, stats, attacks
        /// and colour from its own data, and the shared encounter for the arena around it.
        /// </summary>
        /// <remarks>
        /// Public because it is the whole proof that a chapter boss needs no new code - a test builds
        /// one through this and fights it. It takes its data rather than a file name so that proof does
        /// not depend on shipping an arena nobody has designed yet.
        /// The Unity <c>layer</c> parameter is gone: the collision layer is set by
        /// <see cref="CreateEnemyRoot{T}"/> from <see cref="World.Layer"/> rather than looked up by name.
        /// </remarks>
        public static RainbowChapterBossBehaviour CreateChapterBoss(
            Vector2 position,
            GameplayReadabilityDefaults readability,
            RainbowChapterBossData data,
            BossEncounterData encounter)
        {
            RainbowChapterBossBehaviour go = InstantiateEnemy<RainbowChapterBossBehaviour>(ChapterBossScenePath, data.BossName);
            ResizeCapsuleCollider(go, data.BodySize);
            DressActorVisual(go, data.PrimaryColor, readability.BossVisualSize, readability.ActorSortingOrder);
            DressRoleMarker(go, data.ChapterName, readability.BossRoleLocalPosition, readability.BossRoleColor, readability);

            // Added before the boss enters the tree: its _Ready reads the health it is about to resize,
            // and the sprite it paints, and a child is always ready before its parent. The rig only - no
            // number. The behaviour writes the boss's max health from its authored data, difficulty
            // included, and it is the only thing that does.
            Health health = EnsureEnemyHealthRig(go, false);

            // Without these a chapter boss cannot be poise-broken and pays nothing for dying - it looks
            // like a boss and behaves like scenery with health.
            // Found, never added: EnemyBase.tscn carries both, and a second SoulsWallet would be the one
            // written while GetComponent<SoulsWallet>() kept answering with the empty first - the boss's
            // kill silently worth nothing. A scene that has lost either gets an error (PLAN_CLOSEOUT K7).
            Poise poise = go.RequireComponent<Poise>(Owner);
            poise.Configure(data.MaxPoise, data.PoiseHeavyMultiplier, data.PoiseRegenDelay, data.PoiseRegenRate);
            go.RequireComponent<SoulsWallet>(Owner).SetSouls(data.SoulReward);

            PlaceInWorld(go, position);

            go.SetEncounterData(encounter);
            // Scenes rather than templates parented to the boss and deactivated. Nothing leaks out of a
            // PackedScene instance, so neither of these needs re-arming where it is spawned; the hazard's
            // colour and size come off the attack profile, and the afterimage's off the boss.
            go.SetHazardPrefab(GD.Load<PackedScene>(BossHazardStrip.ScenePath));
            go.SetAfterimagePrefab(GD.Load<PackedScene>(BossAfterimage.ScenePath));
            go.SetBossData(data);

            // After SetBossData, which is where this boss's max health is decided.
            AddHealthBar(go, health, readability.BossHealthBarSize, readability.BossHealthBarOffset, readability.BossHealthBarColor);

            return go;
        }

        private static WrathMiniBoss CreateWrathMiniBoss(Vector2 position, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            WrathMiniBoss go = InstantiateEnemy<WrathMiniBoss>(WrathMiniBossScenePath, GameplayPrefabNames.WrathMiniBoss);
            ResizeCapsuleCollider(go, readability.BossColliderSize);
            DressActorVisual(go, readability.BossColor, readability.BossVisualSize, readability.ActorSortingOrder);

            DressAttackReadout(go, "BossSlashDanger", readability.BossSlashDangerLocalPosition, readability.BossSlashDangerSize, readability.BossSlashDangerColor, readability.BossSlashReadoutSortingOrder);
            DressAttackReadout(go, "BossSlamDanger", readability.BossSlamDangerLocalPosition, readability.BossSlamDangerSize, readability.BossSlamDangerColor, readability.BossSlamReadoutSortingOrder);
            DressRoleMarker(go, "Mini Boss", readability.BossRoleLocalPosition, readability.BossRoleColor, readability);

            WrathMiniBossData tuning = catalog.WrathMiniBoss;

            go.SetTuningData(tuning);

            // Bound after the boss is in the tree, so the file wins over anything the node exported for
            // itself - which is nothing on a boss built here, and would have been the prefab's in Unity.
            if (catalog?.WrathEncounter != null)
                go.SetEncounterData(catalog.WrathEncounter);

            ApplyEnemyHealth(go, tuning.maxHealthBoss, readability.BossHealthBarSize, readability.BossHealthBarOffset, readability.BossHealthBarColor, false);
            ApplyEnemyPoiseAndReward(go, tuning);

            // Parented last: every component has to exist before _Ready runs on the actor.
            PlaceInWorld(go, position);

            return go;
        }

        /// <summary>
        /// The actor, detached and already complete. Every archetype scene inherits
        /// <c>Scenes/Actors/EnemyBase.tscn</c>, so the collision layer and mask, the capsule, the body
        /// sprite and the whole component rig arrive with the instance rather than being written here.
        ///
        /// The name is still bound: three of the five scenes ship the name their spawner wants, but a
        /// chapter boss is named after the boss it *is* - "Crimson Warden", not "ChapterBoss" - and the
        /// save data, the debug jump menu and the tests all read that name back.
        ///
        /// The Enemy group is joined by <c>EnemyStateMachine._Ready</c>, which is Unity's "Enemy" tag.
        ///
        /// The shared behaviour numbers are pushed here rather than read there: <c>MyGame.Enemy</c> sits
        /// below this namespace and below <c>MyGame.Player</c>, so it cannot open <c>WorldTuning.json</c>
        /// or <c>PlayerCombat.json</c> itself. Same shape as <c>Poise.Configure</c>. One funnel, so a new
        /// archetype cannot forget it.
        /// </summary>
        private static T InstantiateEnemy<T>(string scenePath, string name) where T : CharacterBody2D
        {
            var go = GD.Load<PackedScene>(scenePath).Instantiate<T>();
            go.Name = name;
            ConfigureSharedBehaviour(go);
            return go;
        }

        /// <summary>
        /// <c>WorldTuning.json</c>'s enemy block and <c>PlayerCombat.json</c>'s perfect-parry reward, into
        /// the state machine every archetype shares. A missing design file leaves the machine unconfigured
        /// and says so - there is no copy of either file's numbers here (PLAN_CLOSEOUT D1).
        /// </summary>
        /// <remarks>
        /// UNITS: every distance handed over is already <b>pixels</b>. <c>WorldTuningData.Load</c> ran
        /// <c>ScaleToPixels</c> over the metre values on the way in, and nothing re-scales below.
        /// </remarks>
        private static void ConfigureSharedBehaviour(Node actor)
        {
            if (actor is not EnemyStateMachine machine)
            {
                return;
            }

            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            WorldTuningData world = catalog?.WorldTuning;
            MyGame.Player.PlayerCombatData combat = catalog?.PlayerCombat;
            if (world == null || combat == null)
            {
                GD.PushError($"GameplayEnemySpawner: Design/WorldTuning.json or Design/PlayerCombat.json is missing; '{machine.Name}' keeps no shared behaviour.");
                return;
            }

            machine.Configure(
                world.enemyGravity,
                world.enemyDisengageDistance,
                world.enemyIdleToPatrolTime,
                world.enemyInvestigateDuration,
                world.enemyRecoveryDuration,
                world.enemyLedgeProbeForward,
                world.enemyLedgeProbeDepth,
                combat.perfectParryStunMultiplier);
        }

        /// <summary>
        /// Puts a finished actor into the world <b>at</b> its spawn point.
        ///
        /// THE TRAP, carried over from Unity: the position is written before the node enters the tree.
        /// A body added at the origin and moved afterwards is swept from the origin to its destination
        /// on the first physics step, and everything standing between the two takes the hit - which in
        /// this arena is the player, every time, because the player spawns near the origin. Unity's
        /// answer was <c>Instantiate(prefab, position, rotation)</c>; Godot's is this ordering.
        /// It is also why the whole hierarchy is built detached: a component's _Ready has to find its
        /// siblings, and the actor has to arrive in one piece, already in the right place.
        /// </summary>
        private static void PlaceInWorld(Node2D actor, Vector2 position)
        {
            actor.Position = position;
            GameplayBuildShim.SceneRoot?.AddChild(actor);
        }

        /// <summary>
        /// Resizes the capsule the actor scene already carries. The shape is marked
        /// <c>resource_local_to_scene</c> in <c>EnemyBase.tscn</c> and in every archetype that
        /// overrides it, so this writes one actor's body and not the whole arena's.
        /// The size still arrives from outside because a chapter boss takes its body from
        /// <c>RainbowChapterBossData.BodySize</c>, which no scene can know.
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
        /// Binds the designer-owned half of the body sprite. The texture and the feet-heavy pivot are
        /// authored per archetype scene; the colour comes from <c>Resources/Art/Readability.json</c>
        /// (or from the boss's own data), and the size and sorting order from
        /// <see cref="GameplayReadabilityDefaults"/>, so those three stay bound per spawn.
        /// </summary>
        private static void DressActorVisual(Node2D go, Color color, Vector2 size, int sortingOrder)
        {
            Sprite2D sr = go.GetNode<Sprite2D>("Visual");
            sr.SetSpriteSize(size);
            sr.Modulate = color;
            sr.ZIndex = sortingOrder;
        }

        private static void ApplyEnemyHealth(Node2D go, float maxHealth, Vector2 barSize, Vector2 barOffset, Color barColor, bool destroyOnDeath)
        {
            Health health = EnsureEnemyHealthRig(go, destroyOnDeath);

            // Difficulty and New Game+ are applied wherever the number is written, rather than in each
            // Create* method. Authored numbers stay authored: the multiplier is 1 on Normal with no lap
            // behind it. A chapter boss does not take its number from here - it writes its own from its
            // data, and this method scaling one too made the result depend on which of the two ran last.
            maxHealth *= DifficultySettings.EnemyHealthMultiplier;

            health.SetMaxHealth(maxHealth);
            health.SetHealth(maxHealth);

            // After the number, always: the bar reads the health it is handed, and nothing delivers the
            // event that would correct a bar built against an empty one.
            AddHealthBar(go, health, barSize, barOffset, barColor);
        }

        /// <summary>
        /// Everything an enemy needs to take a hit and show it - receiver, feedback, result bridge,
        /// death cleanup - without deciding what its health is.
        /// </summary>
        /// <remarks>
        /// Split out so the chapter boss can be given the rig while
        /// <c>RainbowChapterBossBehaviour</c> stays the only writer of its max health. Two writers
        /// with a call order between them is how Hard and every New Game+ lap reached the grunts and
        /// stopped at the boss: the spawner scaled the health and <c>SetBossData</c> put the authored
        /// number straight back a line later.
        /// </remarks>
        private static Health EnsureEnemyHealthRig(Node2D go, bool destroyOnDeath)
        {
            Health health = go.RequireComponent<Health>(Owner);

            EnsureDamageReceiver(go, health);

            CombatFeedback feedback = go.RequireComponent<CombatFeedback>(Owner);
            EnsureCombatResultBridge(go);

            var sr = go.GetComponent<Sprite2D>();
            if (sr != null)
                feedback.SetOriginalColor(sr.Modulate);

            if (destroyOnDeath)
                go.RequireComponent<EnemyDeathCleanup>(Owner);

            return health;
        }

        /// <summary>
        /// Gives the enemy its stagger resistance and seeds its wallet with the souls it is worth.
        /// The wallet doubles as the drop table: killing an actor moves its balance to the killer, so
        /// there is no second list of rewards that can drift out of step with this one.
        /// </summary>
        private static void ApplyEnemyPoiseAndReward(Node2D go, EnemyTuningData tuning)
        {
            if (tuning == null)
                return;

            Poise poise = go.RequireComponent<Poise>(Owner);
            poise.Configure(tuning.maxPoise, tuning.poiseHeavyMultiplier, tuning.poiseRegenDelay, tuning.poiseRegenRate);

            SoulsWallet wallet = go.RequireComponent<SoulsWallet>(Owner);
            wallet.SetSouls(tuning.soulReward);
        }

        private static void AddHealthBar(Node target, Health health, Vector2 size, Vector2 offset, Color color)
        {
            GameplayWorldHealthBar bar = target.RequireComponent<GameplayWorldHealthBar>(Owner);
            bar.Initialize(health, size, offset, color);
        }

        private static DamageReceiver EnsureDamageReceiver(Node target, Health health)
        {
            DamageReceiver receiver = target.RequireComponent<DamageReceiver>(Owner);
            receiver.Initialize(health);
            return receiver;
        }

        private static void EnsureCombatResultBridge(Node target)
        {
            target.RequireComponent<CombatResultBroadcaster>(Owner);
        }

        /// <summary>
        /// The danger disc under an attack, which every archetype scene already carries as an
        /// <c>AttackReadout.tscn</c> instance under the name given here. The texture, the disc's
        /// centring and the telegraph pulse child are authored; the local position, the size and the
        /// designer-owned colour are readability's and stay bound per spawn.
        /// </summary>
        private static void DressAttackReadout(Node2D parent, string name, Vector2 localPosition, Vector2 size, Color color, int sortingOrder)
        {
            var go = parent.GetNode<Sprite2D>(name);
            go.Position = localPosition;
            go.SetSpriteSize(size);
            go.Modulate = color;
            go.ZIndex = sortingOrder;
        }

        /// <summary>
        /// The word under an enemy's feet saying what it is - the <c>WorldLabel.tscn</c> instance
        /// <c>EnemyBase.tscn</c> carries as <c>RoleMarker</c>, the very scene the arena's signage
        /// instances too. The caption, the local position, the colour, the font size and the sorting
        /// order all differ per enemy, so all five are bound here.
        /// </summary>
        private static void DressRoleMarker(Node2D parent, string label, Vector2 localPosition, Color color, GameplayReadabilityDefaults readability)
        {
            var go = parent.GetNode<Node2D>("RoleMarker");
            go.Position = localPosition;

            Label text = go.GetNode<Label>("Text");
            text.Text = label;
            text.ZIndex = readability.RoleMarkerSortingOrder;
            text.AddThemeFontSizeOverride("font_size", readability.RoleMarkerFontSizePx);
            text.AddThemeColorOverride("font_color", color);
        }
    }
}
