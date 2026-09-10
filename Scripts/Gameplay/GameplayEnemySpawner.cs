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
    /// Every enemy in the arena, built in code.
    ///
    /// UNITS: sizes, offsets and positions arrive already in Godot pixels with +Y down, from
    /// <see cref="GameplayReadabilityDefaults"/> and <see cref="GameplaySceneDefaults"/>; enemy tuning
    /// arrives in pixels too, because <c>EnemyTuningData.ScaleToPixels</c> runs at load. Nothing here
    /// converts anything.
    ///
    /// SHAPE: the Unity actor was a Rigidbody2D GameObject carrying a behaviour component. The port
    /// made <c>EnemyStateMachine</c> the <see cref="CharacterBody2D"/>, so the behaviour <i>is</i> the
    /// actor root and everything else - health, poise, wallet, readouts - is a child of it.
    /// </summary>
    public static class GameplayEnemySpawner
    {
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
            MeleeGrunt go = CreateEnemyRoot<MeleeGrunt>(GameplayPrefabNames.MeleeGrunt);
            AddCapsuleCollider(go, readability.MeleeColliderSize);
            AddActorVisual(go, GameplayVisualFactory.ActorSpriteKind.Grunt, readability.EnemyColor, readability.MeleeVisualSize, readability.ActorSortingOrder);

            var attackPoint = new Node2D { Name = "AttackPoint", Position = new Vector2(World.U(0.6f), 0f) };
            go.AddChild(attackPoint);

            CreateAttackReadout(go, "SlashDanger", readability.MeleeDangerLocalPosition, readability.MeleeDangerSize, readability.MeleeDangerColor, readability.EnemyReadoutSortingOrder);
            CreateRoleMarker(go, "Melee", readability.MeleeRoleLocalPosition, readability.MeleeRoleColor, readability);

            MeleeGruntData tuning = SpawnTuning<MeleeGruntData>(spawn) ?? (catalog?.MeleeGrunt
                ?? GameplayTuningDefaults.CreateMeleeGrunt(readability.EnemyColor));

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
            LeapingAttacker go = CreateEnemyRoot<LeapingAttacker>(GameplayPrefabNames.LeapingAttacker);
            AddCapsuleCollider(go, readability.LeaperColliderSize);
            AddActorVisual(go, GameplayVisualFactory.ActorSpriteKind.Leaper, readability.LeaperColor, readability.LeaperVisualSize, readability.ActorSortingOrder);

            CreateAttackReadout(go, "LeapLandingDanger", readability.LeapDangerLocalPosition, readability.LeapDangerSize, readability.LeapDangerColor, readability.EnemyReadoutSortingOrder);
            CreateRoleMarker(go, "Leap", readability.LeapRoleLocalPosition, readability.LeapRoleColor, readability);

            LeapingAttackerData tuning = SpawnTuning<LeapingAttackerData>(spawn) ?? (catalog?.LeapingAttacker
                ?? GameplayTuningDefaults.CreateLeapingAttacker());

            go.SetTuningData(tuning);

            ApplyEnemyHealth(go, tuning.maxHealth, readability.EnemyHealthBarSize, readability.LeaperHealthBarOffset, readability.EnemyHealthBarColor, true);
            ApplyEnemyPoiseAndReward(go, tuning);

            // Parented last: every component has to exist before _Ready runs on the actor.
            PlaceInWorld(go, spawn.Position);

            return go;
        }

        private static RangedCaster CreateRangedCasterAt(GameplaySceneDefaults.EnemySpawn spawn, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            RangedCaster go = CreateEnemyRoot<RangedCaster>(GameplayPrefabNames.RangedCaster);
            AddCapsuleCollider(go, readability.CasterColliderSize);
            AddActorVisual(go, GameplayVisualFactory.ActorSpriteKind.Caster, readability.CasterColor, readability.CasterVisualSize, readability.ActorSortingOrder);

            CreateAttackReadout(go, "CastRange", readability.CastDangerLocalPosition, readability.CastDangerSize, readability.CastDangerColor, readability.EnemyReadoutSortingOrder);
            CreateRoleMarker(go, "Cast", readability.CastRoleLocalPosition, readability.CastRoleColor, readability);

            go.SetProjectilePrefab(CreateProjectilePrefab(readability));

            RangedCasterData tuning = SpawnTuning<RangedCasterData>(spawn) ?? (catalog?.RangedCaster
                ?? GameplayTuningDefaults.CreateRangedCaster());

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
            var data = Res.LoadJson<T>(DesignResourceFolder + dataFile);
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
            RainbowChapterBossBehaviour go = CreateEnemyRoot<RainbowChapterBossBehaviour>(data.BossName);
            AddCapsuleCollider(go, data.BodySize);
            AddActorVisual(go, GameplayVisualFactory.ActorSpriteKind.Boss, data.PrimaryColor, readability.BossVisualSize, readability.ActorSortingOrder);
            CreateRoleMarker(go, data.ChapterName, readability.BossRoleLocalPosition, readability.BossRoleColor, readability);

            // Added before the boss enters the tree: its _Ready reads the health it is about to resize,
            // and the sprite it paints, and a child is always ready before its parent. The rig only - no
            // number. The behaviour writes the boss's max health from its authored data, difficulty
            // included, and it is the only thing that does.
            Health health = EnsureEnemyHealthRig(go, false);

            // Without these a chapter boss cannot be poise-broken and pays nothing for dying - it looks
            // like a boss and behaves like scenery with health.
            Poise poise = go.AddComponent<Poise>();
            poise.Configure(data.MaxPoise, data.PoiseHeavyMultiplier, data.PoiseRegenDelay, data.PoiseRegenRate);
            go.AddComponent<SoulsWallet>().SetSouls(data.SoulReward);

            PlaceInWorld(go, position);

            go.SetEncounterData(encounter);
            go.SetHazardPrefab(CreateHazardPrefab(readability, go));
            go.SetAfterimagePrefab(CreateAfterimagePrefab(go));
            go.SetBossData(data);

            // After SetBossData, which is where this boss's max health is decided.
            AddHealthBar(go, health, readability.BossHealthBarSize, readability.BossHealthBarOffset, readability.BossHealthBarColor);

            return go;
        }

        private static WrathMiniBoss CreateWrathMiniBoss(Vector2 position, GameplayReadabilityDefaults readability, GameplayTuningCatalog catalog)
        {
            WrathMiniBoss go = CreateEnemyRoot<WrathMiniBoss>(GameplayPrefabNames.WrathMiniBoss);
            AddCapsuleCollider(go, readability.BossColliderSize);
            AddActorVisual(go, GameplayVisualFactory.ActorSpriteKind.Boss, readability.BossColor, readability.BossVisualSize, readability.ActorSortingOrder);

            CreateAttackReadout(go, "BossSlashDanger", readability.BossSlashDangerLocalPosition, readability.BossSlashDangerSize, readability.BossSlashDangerColor, readability.BossSlashReadoutSortingOrder);
            CreateAttackReadout(go, "BossSlamDanger", readability.BossSlamDangerLocalPosition, readability.BossSlamDangerSize, readability.BossSlamDangerColor, readability.BossSlamReadoutSortingOrder);
            CreateRoleMarker(go, "Mini Boss", readability.BossRoleLocalPosition, readability.BossRoleColor, readability);

            WrathMiniBossData tuning = catalog?.WrathMiniBoss
                ?? GameplayTuningDefaults.CreateWrathMiniBoss(readability.BossColor);

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
        /// The actor body, detached. The Unity version was a Rigidbody2D with freezeRotation,
        /// interpolation and Continuous collision; a CharacterBody2D never rotates, is interpolated by
        /// the engine and sweeps itself in MoveAndSlide, so none of the three has a line here.
        /// The Enemy group is joined by <c>EnemyStateMachine._Ready</c>, which is Unity's "Enemy" tag.
        /// </summary>
        private static T CreateEnemyRoot<T>(string name) where T : CharacterBody2D, new()
        {
            return new T
            {
                Name = name,
                CollisionLayer = World.Layer.Enemy,
                CollisionMask = World.Layer.GroundProbe | World.Layer.Player,
            };
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

        private static void AddCapsuleCollider(Node2D go, Vector2 size)
        {
            CollisionShape2D col = go.AddComponent<CollisionShape2D>("Collider");
            col.Shape = new CapsuleShape2D
            {
                // Unity's CapsuleCollider2D.size is the full width and height; Godot wants a radius.
                Radius = size.X * 0.5f,
                Height = size.Y,
            };
        }

        private static void AddActorVisual(Node2D go, GameplayVisualFactory.ActorSpriteKind spriteKind, Color color, Vector2 size, int sortingOrder)
        {
            Sprite2D sr = go.AddComponent<Sprite2D>("Visual");
            GameplayVisualFactory.Dress(
                sr, GameplayVisualFactory.CreateActorSprite(spriteKind), size, GameplayVisualFactory.Pivot(spriteKind));
            sr.Modulate = color;
            sr.ZIndex = sortingOrder;
        }

        /// <summary>
        /// The template a caster duplicates per shot. Deliberately <b>not</b> added to the tree: Unity's
        /// <c>SetActive(false)</c> bought the same thing, and a live template in the scene is a trigger
        /// that damages the player on contact and then destroys itself, taking every future shot with it.
        /// </summary>
        private static Node2D CreateProjectilePrefab(GameplayReadabilityDefaults readability)
        {
            var go = new EnemyProjectile { Name = GameplayPrefabNames.EnemyProjectile };

            var sr = new Sprite2D { Name = "Sprite" };
            go.AddChild(sr);
            GameplayVisualFactory.Dress(
                sr,
                GameplayVisualFactory.CreateDiscSprite(),
                new Vector2(World.U(0.42f), World.U(0.42f)),
                GameplayVisualFactory.Pivot(GameplayVisualFactory.SpriteKind.Disc));
            sr.Modulate = readability.ProjectileColor;
            sr.ZIndex = readability.ProjectileSortingOrder;

            return go;
        }

        /// <summary>
        /// The body a hazard strip wears. Colour and size come off the attack that drops it, so this is
        /// only the shape; the readout sorting order puts it above the floor and under the actors
        /// standing on it, which is where a danger marker on the ground belongs.
        /// </summary>
        /// <remarks>
        /// Parented to the boss so it dies with it. As a root object it was one leaked object per spawn,
        /// and <see cref="GameplayEnemyRespawner"/> respawns on every death and every rest - the leak was
        /// unbounded across a session. Duplicating from it still produces a detached node, because a
        /// duplicate has no parent until someone gives it one.
        /// </remarks>
        private static Node2D CreateHazardPrefab(GameplayReadabilityDefaults readability, Node2D owner)
        {
            var go = new BossHazardStrip { Name = "BossHazardStripTemplate" };
            owner.AddChild(go);

            Sprite2D sr = go.AddComponent<Sprite2D>("Sprite");
            GameplayVisualFactory.Dress(sr, GameplayVisualFactory.CreateDiscSprite(), Vector2.Zero, GameplayVisualFactory.Pivot(GameplayVisualFactory.SpriteKind.Disc));
            sr.ZIndex = readability.EnemyReadoutSortingOrder;

            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// The body an afterimage wears. Sprite, colour, size and sorting order all come off the boss at
        /// the moment it casts one, so this is only the shell - and it is parented to the boss for the
        /// same reason the hazard template is. <c>BossAfterimage</c> is itself a Sprite2D here, so it is
        /// one node rather than Unity's component plus renderer.
        /// </summary>
        private static Node2D CreateAfterimagePrefab(Node2D owner)
        {
            var go = new BossAfterimage { Name = "BossAfterimageTemplate" };
            owner.AddChild(go);

            go.SetActive(false);
            return go;
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
            Health health = go.EnsureComponent<Health>();

            EnsureDamageReceiver(go, health);

            CombatFeedback feedback = go.EnsureComponent<CombatFeedback>();
            EnsureCombatResultBridge(go);

            var sr = go.GetComponent<Sprite2D>();
            if (sr != null)
                feedback.SetOriginalColor(sr.Modulate);

            if (destroyOnDeath)
                go.EnsureComponent<EnemyDeathCleanup>();

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

            Poise poise = go.EnsureComponent<Poise>();
            poise.Configure(tuning.maxPoise, tuning.poiseHeavyMultiplier, tuning.poiseRegenDelay, tuning.poiseRegenRate);

            SoulsWallet wallet = go.EnsureComponent<SoulsWallet>();
            wallet.SetSouls(tuning.soulReward);
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

        private static void CreateAttackReadout(Node2D parent, string name, Vector2 localPosition, Vector2 size, Color color, int sortingOrder)
        {
            var go = new Sprite2D { Name = name, Position = localPosition };
            parent.AddChild(go);

            GameplayVisualFactory.Dress(go, GameplayVisualFactory.CreateDiscSprite(), size, GameplayVisualFactory.Pivot(GameplayVisualFactory.SpriteKind.Disc));
            go.Modulate = color;
            go.ZIndex = sortingOrder;

            go.AddComponent<GameplayTelegraphPulse>();
        }

        /// <summary>
        /// The word under an enemy's feet saying what it is. Unity's TextMesh has no Godot twin, so this
        /// is a Control <see cref="Label"/> under a Node2D - the same arrangement the arena's world
        /// labels use, and it scales with the camera the way a TextMesh did.
        /// </summary>
        private static void CreateRoleMarker(Node2D parent, string label, Vector2 localPosition, Color color, GameplayReadabilityDefaults readability)
        {
            var go = new Node2D { Name = "RoleMarker", Position = localPosition };
            parent.AddChild(go);

            var text = new Label
            {
                Name = "Text",
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ZIndex = readability.RoleMarkerSortingOrder,

                // Unity's TextAnchor.MiddleCenter: a Control is placed by its top-left, so growing both
                // ways from a zero-sized rect centres the text on the node's origin with no measuring.
                GrowHorizontal = Control.GrowDirection.Both,
                GrowVertical = Control.GrowDirection.Both,
                Size = Vector2.Zero,
            };

            text.AddThemeFontSizeOverride("font_size", readability.RoleMarkerFontSizePx);
            text.AddThemeColorOverride("font_color", color);
            go.AddChild(text);
        }
    }
}
