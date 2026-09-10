using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;
using MyGame.Testing;
using MyGame.UI;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;
using UnityTestAgent.Replay;
using UnityTestAgent.Reporting;
using UnityTestAgent.Scenario;

namespace MyGame.Tests
{
    /// <summary>
    /// P0 combat stability, part three: the gameplay wiring - spawners, environment, HUD, input map and
    /// the authored tuning behind them. Same fixture as
    /// <see cref="P0CombatStabilityTests"/>, split only for length.
    /// </summary>
    public partial class P0CombatStabilityTests
    {
        [Test]
        public void GameplayActorsAttachDamageReceivers()
        {
            var player = new Node2D { Name = "PlayerRoot" };
            var playerHealth = new Health { Name = nameof(Health) };
            player.AddChild(playerHealth);
            Spawn(player);
            playerHealth.SetHealth(100f);

            // Still reflection, still by name: these are private statics and a rename has to break the
            // test rather than the build. The Unity signature took a GameObject; it takes the actor Node
            // here, because a Unity component is a child node in this port.
            Type playerSpawner = ResolveType("MyGame.Gameplay.GameplayPlayerSpawner");
            Assert.NotNull(playerSpawner, "GameplayPlayerSpawner should exist.");
            MethodInfo ensurePlayerReceiver = playerSpawner.GetMethod("EnsureDamageReceiver", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(ensurePlayerReceiver, "GameplayPlayerSpawner should expose private EnsureDamageReceiver helper.");
            ensurePlayerReceiver.Invoke(null, new object[] { player, playerHealth });
            Assert.NotNull(player.GetComponent<DamageReceiver>(), "Gameplay player root should receive a DamageReceiver.");

            MethodInfo ensurePlayerBridge = playerSpawner.GetMethod("EnsureCombatResultBridge", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(ensurePlayerBridge, "GameplayPlayerSpawner should expose private EnsureCombatResultBridge helper.");
            ensurePlayerBridge.Invoke(null, new object[] { player });
            Assert.NotNull(player.GetComponent<CombatResultBroadcaster>(), "Gameplay player bridge should attach the combat result broadcaster.");

            var enemy = new Node2D { Name = "EnemyRoot" };
            var enemyHealth = new Health { Name = nameof(Health) };
            enemy.AddChild(enemyHealth);
            Spawn(enemy);
            enemyHealth.SetHealth(100f);

            Type enemySpawner = ResolveType("MyGame.Gameplay.GameplayEnemySpawner");
            Assert.NotNull(enemySpawner, "GameplayEnemySpawner should exist.");
            MethodInfo ensureEnemyReceiver = enemySpawner.GetMethod("EnsureDamageReceiver", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(ensureEnemyReceiver, "GameplayEnemySpawner should expose private EnsureDamageReceiver helper.");
            ensureEnemyReceiver.Invoke(null, new object[] { enemy, enemyHealth });
            Assert.NotNull(enemy.GetComponent<DamageReceiver>(), "Gameplay enemy root should receive a DamageReceiver.");

            MethodInfo ensureEnemyBridge = enemySpawner.GetMethod("EnsureCombatResultBridge", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(ensureEnemyBridge, "GameplayEnemySpawner should expose private EnsureCombatResultBridge helper.");
            ensureEnemyBridge.Invoke(null, new object[] { enemy });
            Assert.NotNull(enemy.GetComponent<CombatResultBroadcaster>(), "Gameplay enemy bridge should attach the combat result broadcaster.");
        }

        [Test]
        public void GameplayEnvironmentCreatesCheckpointComponent()
        {
            Type checkpointType = ResolveType("MyGame.Gameplay.Checkpoint");
            Assert.NotNull(checkpointType, "Checkpoint component should exist.");

            MethodInfo createCheckpoint = typeof(GameplayEnvironmentBuilder)
                .GetMethod("CreateCheckpoint", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(createCheckpoint, "GameplayEnvironmentBuilder should keep a CreateCheckpoint helper.");

            // Unity's `new GameObject` dropped straight into the active scene; the shim's Root override is
            // how a headless test builds into a node it owns and can free again.
            Node previousRoot = GameplayBuildShim.Root;
            GameplayBuildShim.Root = Spawn(new Node2D { Name = "CheckpointBuildRoot" });
            try
            {
                // Unity returned a Transform; there is no such node here, so the builder returns the Node2D.
                var checkpoint = (Node2D)createCheckpoint.Invoke(null, new object[] { GameplaySceneDefaults.Create() });
                Assert.NotNull(checkpoint, "CreateCheckpoint should build a checkpoint object.");

                Checkpoint component = checkpoint.GetComponent<Checkpoint>();
                Assert.NotNull(component, "Gameplay checkpoint object should include Checkpoint component.");

                PropertyInfo respawnPosition = checkpointType.GetProperty("RespawnPosition");
                Assert.NotNull(respawnPosition, "Checkpoint should expose RespawnPosition.");
                // Vector3 -> Vector2: 2D positions have no z here.
                Assert.AreEqual(checkpoint.GlobalPosition, (Vector2)respawnPosition.GetValue(component),
                    "Checkpoint respawn position should match its transform position.");
            }
            finally
            {
                GameplayBuildShim.Root = previousRoot;
            }
        }

        [Test]
        public void GameplayHudExposesManualRefresh()
        {
            Assert.NotNull(typeof(GameplayHud).GetMethod("ForceRefresh"), "GameplayHud should expose ForceRefresh.");
        }

        /// <summary>
        /// Unity also asserted that HUD setup created an <c>EventSystem</c> so the victory buttons were
        /// clickable. That assertion is gone with the type: Godot's viewport owns focus and picking, and
        /// <see cref="GameplayHudSpawner"/> creates no EventSystem equivalent (see PORT_STATUS, UI).
        /// Everything else - the two scene targets, the controller the HUD carries, and the boss's own
        /// victory event driving all three effects - is unchanged.
        /// </summary>
        [Test]
        public void GameplayVictoryFlowOffersRestartAndTitleChoices()
        {
            Assert.IsTrue(SceneExists(GameplayVictoryController.GameplaySceneName), "Victory restart should target a scene that exists.");
            Assert.IsTrue(SceneExists(GameplayVictoryController.TitleSceneName), "Victory return-to-title should target a scene that exists.");

            float startTimeScale = GameClock.TimeScale;
            Node previousRoot = GameplayBuildShim.Root;
            GameplayBuildShim.Root = Spawn(new Node2D { Name = "VictoryBuildRoot" });

            try
            {
                PlayerMotor2D playerBody = CreatePlayerTarget();
                var humanity = new HumanityController { Name = nameof(HumanityController) };
                playerBody.AddChild(humanity);
                var sin = new SinResonanceController { Name = nameof(SinResonanceController) };
                playerBody.AddChild(sin);
                var input = new PlayerInputReceiver { Name = nameof(PlayerInputReceiver) };
                playerBody.AddChild(input);
                sin.Initialize(humanity);

                var playerContext = new GameplayPlayerContext(
                    playerBody,
                    playerBody.GetComponent<PlayerController2D>(),
                    playerBody.GetComponent<Health>(),
                    playerBody.GetComponent<StaminaSystem>(),
                    humanity,
                    sin,
                    null,
                    null);

                var boss = new WrathMiniBoss { Name = "VictoryBoss" };
                AddBoxShape(boss);
                var bossHealth = new Health { Name = nameof(Health) };
                boss.AddChild(bossHealth);
                Spawn(boss);
                bossHealth.SetHealth(100f);

                GameplayHud hud = GameplayHudSpawner.Spawn(
                    playerContext,
                    new GameplayEnemyContext(Array.Empty<Node2D>(), boss));
                _spawned.Add(hud);

                GameplayVictoryController victory = hud.GetComponent<GameplayVictoryController>();
                Assert.NotNull(victory, "Gameplay HUD setup should attach the victory controller.");

                // UnityEvent.Invoke() has no equivalent on a C# event, so the boss's own OnVictory is
                // raised through its compiler-generated backing field - the same by-name reflection this
                // file uses everywhere else. IBossEncounter.Defeated is an alias of it.
                RaiseEvent(boss, "OnVictory");

                Assert.IsTrue(victory.HasWon, "Victory controller should react to the boss victory event.");
                Assert.IsFalse(input.Enabled, "Victory controller should disable manual player input.");
                Assert.IsTrue(hud.IsVictoryVisible, "Victory should show the HUD victory panel with its restart and title choices.");
            }
            finally
            {
                GameplayBuildShim.Root = previousRoot;
                GameClock.TimeScale = startTimeScale;
            }
        }

        /// <summary>
        /// Unity looked the scene up in the AssetDatabase. There is no such database here, and
        /// <see cref="ChapterRoute.ScenePath"/> is the one place that turns a chapter name into a path -
        /// so the check is that the file it names is really there.
        /// </summary>
        private static bool SceneExists(string sceneName)
        {
            return ResourceLoader.Exists(ChapterRoute.ScenePath(sceneName));
        }

        [Test]
        public void GameplayInputExposesDashAndHeavyAttackBindings()
        {
            Assert.NotNull(typeof(GameplayInput).GetProperty("DashPressed"), "GameplayInput should expose DashPressed.");
            Assert.NotNull(typeof(GameplayInput).GetProperty("HeavyAttackPressed"), "GameplayInput should expose HeavyAttackPressed.");
            Assert.NotNull(typeof(GameplayInput).GetProperty("ParryPressed"), "GameplayInput should expose ParryPressed.");
            Assert.NotNull(typeof(PlayerController2D).GetMethod("RequestHeavyAttack"), "Gameplay input flow should be able to request heavy attacks.");
        }

        /// <summary>
        /// The Unity Input Actions asset is gone: its Player map and the legacy-keyboard fallback are one
        /// Godot InputMap in <c>project.godot</c>, and <see cref="GameplayInput"/> reads it by action
        /// name. Read through the <see cref="InputMap"/> API rather than the project file's text, for the
        /// same reason the Unity test read the asset through the Input System API - re-serialising the
        /// bindings must not break the test while a lost binding still does.
        /// </summary>
        [Test]
        public void GameplayInputMapOwnsCombatBindings()
        {
            foreach (string action in new[]
                     {
                         "move_left", "move_right", "move_up", "move_down",
                         "jump", "dodge", "attack", "parry", "heavy_attack",
                         "sin_wrath", "sin_sloth", "sin_pride", "interact",
                     })
            {
                Assert.IsTrue(InputMap.HasAction(action), "InputMap should include " + action + ".");
            }

            // The checkpoint zone has no other way in: GameplayInput.InteractPressed reads this action.
            Assert.IsTrue(HasKey("interact", Key.E), "InputMap should map E to interact.");

            Assert.IsTrue(HasMouseButton("attack", MouseButton.Left, false), "InputMap should map left mouse button to attack.");
            Assert.IsTrue(HasMouseButton("parry", MouseButton.Right, false), "InputMap should map right mouse button to parry.");

            // Unity spelled this as a ButtonWithOneModifier composite over either Alt key plus left mouse.
            // Godot has no composite: a modifier is a flag on the event itself, and InputEventMouseButton
            // .AltPressed is true for either Alt key - which is the same binding, expressed once.
            Assert.IsTrue(HasMouseButton("heavy_attack", MouseButton.Left, true),
                "Heavy attack should fire on Alt plus left mouse, as a modified mouse event.");
        }

        private static bool HasKey(string action, Key key)
        {
            return InputMap.ActionGetEvents(action)
                .OfType<InputEventKey>()
                .Any(e => e.PhysicalKeycode == key || e.Keycode == key);
        }

        private static bool HasMouseButton(string action, MouseButton button, bool altPressed)
        {
            return InputMap.ActionGetEvents(action)
                .OfType<InputEventMouseButton>()
                .Any(e => e.ButtonIndex == button && e.AltPressed == altPressed);
        }

        [Test]
        public void GameplayEnemyTelegraphsAreReadableForParry()
        {
            MeleeGruntData melee = GameplayTuningDefaults.CreateMeleeGrunt(Colors.Red);
            LeapingAttackerData leaper = GameplayTuningDefaults.CreateLeapingAttacker();
            WrathMiniBossData boss = GameplayTuningDefaults.CreateWrathMiniBoss(Colors.Red);

            // Seconds, not distances - no PPU scaling on any of these four.
            Assert.GreaterOrEqual(melee.telegraphTime, 0.8f, "Melee grunt telegraph should be slow enough to parry.");
            Assert.GreaterOrEqual(leaper.leapTelegraphTime, 1f, "Leaping attacker telegraph should be slow enough to parry.");
            Assert.GreaterOrEqual(boss.slashTelegraphTime, 0.8f, "Boss slash telegraph should be slow enough to parry.");
            Assert.GreaterOrEqual(boss.slamTelegraphTime, 1f, "Boss slam telegraph should be slow enough to parry.");
        }

        /// <summary>
        /// <c>GameplayTuningCatalog</c> is a plain C# class here rather than a ScriptableObject - Unity
        /// made it one only so the inspector could hold it - so the Unity <c>as ScriptableObject</c> cast
        /// is a plain null check. Everything it has to reference is unchanged.
        /// </summary>
        [Test]
        public void GameplayTuningCatalogLoadsAuthoredAssets()
        {
            Type catalogType = ResolveType("MyGame.Gameplay.GameplayTuningCatalog");
            Assert.NotNull(catalogType, "GameplayTuningCatalog should exist.");

            MethodInfo load = catalogType.GetMethod("Load");
            Assert.NotNull(load, "GameplayTuningCatalog should expose Load.");

            object catalog = load.Invoke(null, null);
            Assert.NotNull(catalog, "GameplayTuningCatalog should load from the design JSON in Resources/Design.");

            Assert.NotNull(catalogType.GetProperty("PlayerMovement")?.GetValue(catalog), "GameplayTuningCatalog should reference PlayerMovementData.");
            Assert.NotNull(catalogType.GetProperty("PlayerCombat")?.GetValue(catalog), "GameplayTuningCatalog should reference PlayerCombatData.");
            Assert.NotNull(catalogType.GetProperty("PlayerResources")?.GetValue(catalog), "GameplayTuningCatalog should reference PlayerResourceData.");
            Assert.NotNull(catalogType.GetProperty("MeleeGrunt")?.GetValue(catalog), "GameplayTuningCatalog should reference MeleeGruntData.");
            Assert.NotNull(catalogType.GetProperty("LeapingAttacker")?.GetValue(catalog), "GameplayTuningCatalog should reference LeapingAttackerData.");
            Assert.NotNull(catalogType.GetProperty("RangedCaster")?.GetValue(catalog), "GameplayTuningCatalog should reference RangedCasterData.");
            Assert.NotNull(catalogType.GetProperty("WrathMiniBoss")?.GetValue(catalog), "GameplayTuningCatalog should reference WrathMiniBossData.");
        }

        /// <summary>
        /// Unity had two live sources for an actor's body tint - the extracted prefab and
        /// <c>GameplayReadabilityDefaults</c> - and this test existed because recolouring one without the
        /// other was invisible in play. <c>Resources/Prefabs</c> is not ported (PORTING_GUIDE: the
        /// spawners rebuild every actor in code), so the second source is gone and there is nothing left
        /// to disagree. Kept as a skip rather than deleted so the reason is on the record.
        /// </summary>
        [Test]
        public void ActorPrefabBodyColorsMatchReadabilityDefaults()
        {
            Assert.Ignore("Resources/Prefabs is not ported - GameplayReadabilityDefaults is the only body-colour source left, so the two sources this test compared no longer exist.");
        }

        /// <summary>
        /// The Unity test compared the frame <c>GameplayWorldHealthBar.Build</c> paints against the one
        /// every actor prefab already carried, because Build reuses an existing <c>HealthBar/Frame</c>
        /// instead of recolouring it. With prefabs gone, Build is always the one that creates the frame -
        /// so what survives is that it does create it, and paints it with this file's own frame colour.
        /// </summary>
        [Test]
        public void WorldHealthBarBuildsAndPaintsItsFrame()
        {
            var bar = new GameplayWorldHealthBar { Name = "HealthBarFrameProbe" };
            Spawn(bar);
            InvokeNonPublic(bar, "Build");

            Node2D barRoot = bar.GetNodeOrNull<Node2D>("HealthBar");
            Assert.NotNull(barRoot, "GameplayWorldHealthBar.Build should create a HealthBar child.");

            Sprite2D builtFrame = barRoot.GetNodeOrNull<Sprite2D>("Frame");
            Assert.NotNull(builtFrame, "GameplayWorldHealthBar.Build should create a HealthBar/Frame child.");

            FieldInfo frameColor = typeof(GameplayWorldHealthBar)
                .GetField("FrameColor", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(frameColor, "GameplayWorldHealthBar should keep its FrameColor constant.");
            Assert.IsTrue(
                builtFrame.Modulate.IsEqualApprox((Color)frameColor.GetValue(null)),
                $"The frame Build creates ({builtFrame.Modulate}) should be painted with GameplayWorldHealthBar.FrameColor; Build reuses an existing HealthBar/Frame instead of recolouring it.");
        }

        /// <summary>
        /// Unity had enemy body tint coming from two places - the <c>*Data.asset</c> the prefab
        /// serialized, which <c>Awake</c> read before the spawner handed over the JSON instance, and
        /// <c>Resources/Design/*.json</c>. The <c>.asset</c> files are not ported (PORTING_GUIDE: the JSON
        /// supersedes them), so the surviving check is that the JSON really is the one source: what the
        /// catalog hands the spawner has to be what the archetype's own <c>Load()</c> reads.
        /// </summary>
        [Test]
        public void EnemyTuningColorsComeFromDesignJson()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            Assert.NotNull(catalog, "GameplayTuningCatalog should load the design JSON.");

            RequireTuningColor("MeleeGrunt", catalog.MeleeGrunt?.enemyColor, MeleeGruntData.Load()?.enemyColor);
            RequireTuningColor("LeapingAttacker", catalog.LeapingAttacker?.enemyColor, LeapingAttackerData.Load()?.enemyColor);
            RequireTuningColor("RangedCaster", catalog.RangedCaster?.enemyColor, RangedCasterData.Load()?.enemyColor);
            RequireTuningColor("WrathMiniBoss", catalog.WrathMiniBoss?.bossColor, WrathMiniBossData.Load()?.bossColor);
        }

        private static void RequireTuningColor(string name, Color? fromCatalog, Color? fromLoad)
        {
            Assert.NotNull(fromCatalog, $"GameplayTuningCatalog should load {name} from Resources/Design.");
            Assert.NotNull(fromLoad, $"{name}Data.Load() should read Resources/Design/{name}.json.");
            Assert.IsTrue(
                fromCatalog.Value.IsEqualApprox(fromLoad.Value),
                $"{name} colour {fromCatalog.Value} from the catalog does not match {fromLoad.Value} from {name}.json. The JSON is the single source in this port - a second one would tint the spawned actor differently from the tuned one.");
        }

        [Test]
        public void RainbowBossExposesAttackRequestApi()
        {
            MethodInfo requestAttack = typeof(RainbowChapterBossBehaviour).GetMethod("RequestAttack");
            Assert.NotNull(requestAttack, "RainbowChapterBossBehaviour should expose RequestAttack.");
            Assert.NotNull(typeof(RainbowChapterBossBehaviour).GetProperty("CurrentAttackProfile"), "Rainbow boss should expose CurrentAttackProfile.");
            Assert.NotNull(typeof(RainbowChapterBossBehaviour).GetProperty("IsAttackRunning"), "Rainbow boss should expose IsAttackRunning.");
        }

        [Test]
        public void GameplaySceneIncludesCheckpointRunSection()
        {
            GameplaySceneDefaults scene = GameplaySceneDefaults.Create();
            bool found = false;

            foreach (GameplaySceneDefaults.PlatformDefinition platform in scene.Platforms)
            {
                if (platform.Name == "CheckpointRunPlatform")
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "Gameplay test room should include a checkpoint run platform.");
        }

        [Test]
        public void GameplaySceneDefaultsCanLoadFromScriptableObject()
        {
            Type assetType = ResolveType("MyGame.Gameplay.GameplaySceneDefaultsAsset");
            Assert.NotNull(assetType, "GameplaySceneDefaultsAsset should exist.");
            MethodInfo createFromAsset = typeof(GameplaySceneDefaults).GetMethod("CreateFromAsset");
            Assert.NotNull(createFromAsset, "GameplaySceneDefaults should expose CreateFromAsset.");
        }

        [Test]
        public void GameplaySceneDefaultsProvideExpandedMapAndCameraFollow()
        {
            GameplaySceneDefaults scene = GameplaySceneDefaults.Create();

            // PPU: GameplaySceneDefaults hands out Godot pixels, so every Unity metre in these three
            // thresholds is multiplied. X only - no vertical expectation here, so nothing flips sign.
            Assert.GreaterOrEqual(scene.GroundSize.X, World.U(40f), "Gameplay map should be widened for scrolling combat.");
            Assert.GreaterOrEqual(scene.BackdropSize.X, scene.GroundSize.X + World.U(4f), "Backdrop should cover the widened map.");
            Assert.GreaterOrEqual(scene.WrathMiniBossSpawnPosition.X, World.U(25f), "Boss should spawn deeper into the expanded map.");

            Type followType = ResolveType("MyGame.Gameplay.GameplayCameraFollow2D");
            Assert.NotNull(followType, "GameplayCameraFollow2D should exist.");
            Assert.NotNull(followType.GetMethod("Initialize"), "GameplayCameraFollow2D should expose Initialize.");
        }

        // ---------------------------------------------------------------------------------------
        // UnityTestAgent
        //
        // Ported: `Scripts/Testing/` carries the eleven MyGame* adapters and `Scripts/Testing/Package/`
        // the UnityTestAgent library they plug into. Two things changed shape and show up in every test
        // below.
        //
        // 1. SYNTHETIC INPUT IS REAL INPUT. Unity's driver called PlayerController2D.RequestAttack()
        //    behind the keyboard's back, so every Unity fixture switched PlayerInputReceiver *off*
        //    first. MyGameAgentInputDriver presses the project.godot InputMap actions instead, which
        //    GameplayInput reads back - so the receiver has to be present and enabled, and the tests
        //    that drive it need frames to pass where the Unity ones asserted on the next line.
        // 2. UNITS. MyGameStateProbe reports Godot pixels, +Y down. Every authored metre in these
        //    fixtures goes through World.U, and the agent ranges they are compared against are already
        //    in pixels.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The driver holds InputMap actions down for as long as the agent asks; nothing releases them
        /// when a test ends, so a key left pressed here would leak into the next fixture. Released from
        /// <see cref="TearDown"/>.
        /// </summary>
        private MyGameAgentInputDriver _agentInput;

        [Test]
        public async Task UnityTestAgentAdaptersDrivePlayerAndCaptureState()
        {
            PlayerMotor2D playerBody = CreateDrivenPlayer(out PlayerController2D controller);
            _agentInput = new MyGameAgentInputDriver(controller);

            // Unity asserted on the line after Apply, because its driver wrote the controller directly.
            // Here the press has to travel - PlayerInputReceiver._Process reads move_right into
            // SetMoveInput, and the motor only turns inside FixedTick - and both of the tree's frame
            // signals fire *before* the nodes process, so counting frames by hand is off by one in a way
            // that is easy to get accidentally green. WaitUntil is the harness's answer and cannot.
            _agentInput.Apply(AgentAction.Move(1f));
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => controller.FacingDir > 0f),
                "UnityTestAgent input driver should forward movement to PlayerController2D.");

            // The Unity assertion had a hole this port inherits: FacingDir starts at +1, so the check
            // above also passes on a completely dead input path. Turning the other way is the half that
            // can only go green if move_left really reached the motor.
            _agentInput.Apply(AgentAction.Move(-1f));
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => controller.FacingDir < 0f),
                "UnityTestAgent input driver should forward a reversal, not just the facing the player started with.");

            // Unity called PlayerActionController.Tick(0.01f, FacingDir) by hand here, because an
            // edit-mode test has no frames. PlayerController2D._Process does that tick, one frame after
            // the receiver turns the attack key into RequestAttack.
            _agentInput.Apply(AgentAction.Attack());
            Assert.IsTrue(
                await TestContext.Runner.WaitUntil(() => controller.IsAttacking),
                "UnityTestAgent input driver should forward light attack requests.");

            // Unity's bare GameObject carrying a Health component; a component is a child node here.
            // PPU: two authored metres along.
            var enemy = new Node2D { Name = "AgentEnemy", Position = new Vector2(World.U(2f), 0f) };
            var enemyHealth = new Health { Name = nameof(Health) };
            enemy.AddChild(enemyHealth);
            Spawn(enemy);
            enemyHealth.SetMaxHealth(50f);
            enemyHealth.SetHealth(50f);

            var probe = new MyGameStateProbe(controller, new Node[] { enemy });
            AgentObservation observation = probe.Capture();

            Assert.IsTrue(observation.Player.IsAlive, "UnityTestAgent state probe should report player life state.");
            Assert.AreEqual(100, observation.Player.HitPoints, "UnityTestAgent state probe should report player hit points.");
            Assert.AreEqual(1, observation.Enemies.Count, "UnityTestAgent state probe should report tracked enemies.");
            Assert.AreEqual("AgentEnemy", observation.Enemies[0].Id, "UnityTestAgent state probe should use enemy object names as ids.");
            Assert.AreEqual(50, observation.Enemies[0].HitPoints, "UnityTestAgent state probe should report enemy hit points.");

            // Silences the compiler about the body, which the fixture only needs for teardown.
            Assert.NotNull(playerBody);
        }

        [Test]
        public void UnityTestAgentScenarioRunnerCanUseMyGameAdapters()
        {
            PlayerMotor2D playerBody = CreateDrivenPlayer(out PlayerController2D controller);

            // PPU: one authored metre along.
            var enemy = new Node2D { Name = "ScenarioEnemy", Position = new Vector2(World.U(1f), 0f) };
            var enemyHealth = new Health { Name = nameof(Health) };
            enemy.AddChild(enemyHealth);
            Spawn(enemy);
            enemyHealth.SetMaxHealth(10f);
            enemyHealth.SetHealth(10f);

            var scenario = new UnityTestScenario("mygame-agent-smoke", 3)
                .WithSuccessCondition(new PlayerAliveCondition())
                .WithFailureCondition(new PlayerDeadCondition());

            _agentInput = new MyGameAgentInputDriver(controller);

            // ScenarioRunner.Run is a synchronous loop over its own tick counter - it never waits for a
            // frame - so this stays a void test and no frame passes inside it. The three ticks exercise
            // the adapter seam (probe -> agent -> driver), not the game's reaction to it.
            var setup = new MyGameScenarioSetup(
                () =>
                {
                    enemyHealth.SetHealth(10f);
                    // Unity's `player.transform.position = Vector3.zero`. The body is the motor here.
                    playerBody.GlobalPosition = Vector2.Zero;
                });

            ScenarioResult result = new ScenarioRunner().Run(
                scenario,
                new ScriptedAgent(new[] { AgentAction.Attack() }),
                new MyGameStateProbe(controller, new Node[] { enemy }),
                _agentInput,
                new ScenarioRunOptions { Setup = setup });

            Assert.AreEqual(ScenarioStatus.Passed, result.Status, "UnityTestAgent scenario runner should work with MyGame adapters.");
        }

        [Test]
        public void UnityTestAgentStateProbeCapturesScenarioContext()
        {
            PlayerMotor2D playerBody = CreateDrivenPlayer(out PlayerController2D controller);

            // Unity added Rigidbody2D + Health + MeleeGrunt to a bare GameObject. The archetype *is* the
            // body here (EnemyStateMachine is a CharacterBody2D), so the actor is a MeleeGrunt with the
            // health component as a child node. PPU on every position below; none of them is vertical.
            var enemy = new MeleeGrunt { Name = "ContextEnemy", Position = new Vector2(World.U(2f), 0f) };
            var enemyHealth = new Health { Name = nameof(Health) };
            enemy.AddChild(enemyHealth);
            Spawn(enemy);
            enemyHealth.SetMaxHealth(20f);
            enemyHealth.SetHealth(20f);

            var boss = new WrathMiniBoss { Name = "ContextBoss", Position = new Vector2(World.U(4f), 0f) };
            var bossHealth = new Health { Name = nameof(Health) };
            boss.AddChild(bossHealth);
            Spawn(boss);
            bossHealth.SetHealth(100f);

            // Y FLIP: Unity's (3, 1) is a metre *up*, which is -Y here. This port's EnemyProjectile moves
            // its own transform instead of riding a rigidbody, so there is no linearVelocity to author -
            // the probe reports zero velocity for a non-body and nothing here reads it.
            var projectile = new EnemyProjectile
            {
                Name = "ContextProjectile",
                Position = new Vector2(World.U(3f), -World.U(1f)),
            };
            Spawn(projectile);

            var checkpoint = new Checkpoint { Name = "ContextCheckpoint", Position = new Vector2(World.U(0.25f), 0f) };
            Spawn(checkpoint);
            checkpoint.Configure("test-checkpoint", Vector2.Zero);

            // Unity's `tag = "Untagged"`: tags became groups, and joining none is the same statement.
            var interactable = new Node2D { Name = "ContextInteractable", Position = new Vector2(World.U(5f), 0f) };
            Spawn(interactable);

            var probe = new MyGameStateProbe(
                controller,
                new Node[] { enemy },
                new Node[] { boss },
                new Node[] { projectile },
                new Node[] { checkpoint },
                new Node[] { interactable });

            AgentObservation observation = probe.Capture();

            Assert.AreEqual("test-checkpoint", observation.Player.CheckpointId, "UnityTestAgent state probe should report nearby checkpoint id.");
            Assert.AreEqual(1, observation.Bosses.Count, "UnityTestAgent state probe should report tracked bosses.");
            Assert.AreEqual("ContextBoss", observation.Bosses[0].Id, "UnityTestAgent state probe should use boss object names as ids.");
            Assert.AreEqual(1, observation.Projectiles.Count, "UnityTestAgent state probe should report tracked projectiles.");
            Assert.IsTrue(observation.Projectiles[0].IsHostile, "UnityTestAgent state probe should mark enemy projectiles hostile.");
            Assert.AreEqual(1, observation.Interactables.Count, "UnityTestAgent state probe should report tracked interactables.");

            Assert.NotNull(playerBody);
        }

        [Test]
        public void UnityTestAgentScenarioFactoryCreatesUsefulFirstScenarios()
        {
            UnityTestScenario smoke = MyGameUnityTestScenarioFactory.CreatePlayerAliveSmoke();
            Assert.AreEqual("mygame-player-alive-smoke", smoke.Id, "UnityTestAgent scenario factory should create player alive smoke scenario.");
            Assert.AreEqual(1, smoke.SuccessConditions.Count, "Player alive smoke scenario should have one success condition.");
            Assert.AreEqual(1, smoke.FailureConditions.Count, "Player alive smoke scenario should fail on death.");

            UnityTestScenario checkpoint = MyGameUnityTestScenarioFactory.CreateReachCheckpoint("Gameplay-start", 40);
            Assert.AreEqual("mygame-reach-checkpoint-Gameplay-start", checkpoint.Id, "UnityTestAgent scenario factory should create checkpoint scenario ids.");

            UnityTestScenario enemy = MyGameUnityTestScenarioFactory.CreateDefeatEnemy("enemy-1", 120);
            Assert.AreEqual("mygame-defeat-enemy-enemy-1", enemy.Id, "UnityTestAgent scenario factory should create enemy defeat scenario ids.");
            Assert.IsInstanceOf<MyGameTrackedEnemyDefeatedCondition>(enemy.SuccessConditions[0], "Enemy defeat scenarios should use MyGame's tracked-enemy condition.");

            var enemyCondition = new MyGameTrackedEnemyDefeatedCondition("enemy-1");
            AgentObservation enemyObservation = AgentObservation.Empty(1)
                .WithEnemy(new EnemyObservation { Id = "enemy-1", IsAlive = true });
            Assert.IsFalse(enemyCondition.IsMet(enemyObservation), "Tracked enemy condition should not pass while the target is alive.");
            enemyObservation.Enemies.Clear();
            Assert.IsTrue(enemyCondition.IsMet(enemyObservation), "Tracked enemy condition should pass when a previously observed target disappears.");

            UnityTestScenario boss = MyGameUnityTestScenarioFactory.CreateBossPatternSurvival("boss-1", 90);
            Assert.AreEqual("mygame-boss-pattern-survival-boss-1", boss.Id, "UnityTestAgent scenario factory should create boss survival scenario ids.");
        }

        [Test]
        public void UnityTestAgentReportArtifactsAreWritten()
        {
            // Unity wrote under Application.dataPath/../Logs; this lands under user://Logs/UnityTestAgent,
            // globalised on the way out because FileSystemReporter is System.IO and has never heard of a
            // user:// scheme.
            string directory = MyGameUnityTestAgentPaths.CreateReportDirectory("p0-report-smoke");
            try
            {
                var reporter = new FileSystemReporter(directory);
                ScenarioResult result = ScenarioResult.Pass("p0-report-smoke", 1, "report smoke");
                var replay = new ReplayRecording("p0-report-smoke", 123);
                replay.Frames.Add(new ReplayFrame
                {
                    Tick = 1,
                    Observation = AgentObservation.Empty(1),
                    Action = AgentAction.Idle(),
                });

                reporter.Report(result, replay);

                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(directory, "summary.json")), "UnityTestAgent reporter should write summary.json.");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(directory, "report.md")), "UnityTestAgent reporter should write report.md.");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(directory, "replay.json")), "UnityTestAgent reporter should write replay.json.");
            }
            finally
            {
                if (System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void UnityTestAgentCustomAgentsMakeGameSpecificDecisions()
        {
            // Pure decision logic over a hand-built observation - no nodes, no frames. The only thing the
            // port changes is the space the positions are in: the agents' ranges are pixels (170 for the
            // parry agent's 1.7 m, 130 for the dodge agent's 1.3 m), so the authored metres here are too.
            AgentObservation observation = AgentObservation.Empty(1);
            observation.Player.IsAlive = true;
            observation.Player.Stamina = 100f;
            observation.Enemies.Add(new EnemyObservation
            {
                Id = "threat",
                IsAlive = true,
                IsAttacking = true,
                Position = new Vector2Observation(World.U(1f), 0f), // PPU: one metre away, inside both ranges.
            });

            AgentDecision parry = new MyGameParryTimingAgent().Decide(observation);
            Assert.AreEqual(AgentActionType.Parry, parry.Action.Type, "MyGame parry timing agent should parry nearby telegraphed attacks.");

            AgentDecision dodgeHeavy = new MyGameDodgeHeavyAttackAgent().Decide(observation);
            Assert.AreEqual(AgentActionType.Dodge, dodgeHeavy.Action.Type, "MyGame dodge-heavy agent should dodge active threats first.");

            observation.Enemies[0].IsAttacking = false;
            observation.Enemies[0].Position = new Vector2Observation(World.U(0.8f), 0f); // PPU: 0.8 m, inside the 130 px heavy range.
            dodgeHeavy = new MyGameDodgeHeavyAttackAgent().Decide(observation);
            Assert.AreEqual(AgentActionType.HeavyAttack, dodgeHeavy.Action.Type, "MyGame dodge-heavy agent should heavy attack nearby safe targets.");

            var route = new MyGameCheckpointRouteAgent(new[] { new Vector2Observation(World.U(2f), 0f) });
            AgentDecision routeDecision = route.Decide(observation);
            Assert.AreEqual(AgentActionType.Move, routeDecision.Action.Type, "MyGame checkpoint route agent should move toward route points.");
        }

        /// <summary>
        /// <see cref="CreatePlayerTarget"/> plus the one component the agent layer needs and the rest of
        /// this fixture does not.
        /// </summary>
        /// <remarks>
        /// The Unity fixtures disabled <see cref="PlayerInputReceiver"/> - their driver bypassed input,
        /// and a receiver reading a dead keyboard overwrote the agent's move input once a frame. The
        /// sign is inverted here: <see cref="MyGameAgentInputDriver"/> presses the same InputMap actions
        /// a person does, so the receiver is the thing it is talking to. Disabling it would silence the
        /// agent completely.
        /// </remarks>
        private PlayerMotor2D CreateDrivenPlayer(out PlayerController2D controller)
        {
            PlayerMotor2D body = CreatePlayerTarget();
            controller = body.GetComponent<PlayerController2D>();
            Assert.NotNull(controller, "CreatePlayerTarget should build a PlayerController2D for the agent to drive.");

            body.AddChild(new PlayerInputReceiver { Name = nameof(PlayerInputReceiver) });
            return body;
        }
    }
}
