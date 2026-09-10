using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;
using MyGame.Testing;
using UnityTestAgent.Agent;
using UnityTestAgent.Core;
using UnityTestAgent.Observation;
using UnityTestAgent.Reporting;
using UnityTestAgent.Replay;
using UnityTestAgent.Scenario;

namespace MyGame.Tests
{
    /// <summary>
    /// The scripted agents driving the real game.
    /// </summary>
    /// <remarks>
    /// **Reflection is gone.** Every lookup here was <c>RequiredType("MyGame.Enemy.MeleeGrunt")</c> in
    /// Unity, with a comment saying it existed so splitting the game into per-module assembly
    /// definitions could not silently break the fixture. Godot compiles the whole project into one
    /// assembly and has no assembly definitions to split, so the reason is gone and the types are named
    /// directly - which is also how a rename now fails at compile time instead of at 2am in a
    /// <c>RequiredType</c> assert.
    ///
    /// **UNITS.** <see cref="MyGameStateProbe"/> reports pixels, +Y down. Every authored figure from the
    /// Unity fixture is therefore converted here: 4 m along becomes <c>World.U(4f)</c>, and half a metre
    /// *up* becomes <c>-World.U(0.5f)</c>.
    /// </remarks>
    public sealed class UnityTestAgentPlayModeSmokeTests
    {
        private const string GameplayScenePath = "res://Scenes/GameplayScene.tscn";

        // 900 was sized against the 36-unit arena, where the first grunt stood 8 units from the player.
        // Chapter one became a 304-unit level on 2026-08-17 and that grunt is now 14 units away. The 2026-08-17
        // stall at x 9.12 was not the budget - see SkipCutscenesSoTheAgentKeepsTheControls for what it was -
        // but the extra headroom is kept: the walk itself is now 2.3s of an 18s budget, and a red here should
        // mean the agent lost a fight rather than that it ran out of clock on the way to one.
        private const int MaxGameplayDefeatTicks = 2400;
        private const int MaxRouteTicks = 480;
        private const int MaxBossSurvivalTicks = 240;

        private bool _startSkipAll;
        private readonly List<Node> _spawned = new();

        /// <summary>
        /// Cutscenes off for the whole fixture. Without it the entry shot holds the controls for the
        /// first second and a half of every scene test, and a route test that only needs nine units can
        /// pass on that free window rather than on its input path.
        ///
        /// The Unity version of this comment went further, because there the driver bypassed input
        /// entirely and <c>CutsceneDirector.Restore</c> switching <c>PlayerInputReceiver</c> back on
        /// silently overwrote the agent's move input from a dead keyboard. That half is dead here: the
        /// agent presses the same InputMap actions a person would, so a cutscene that takes the controls
        /// away takes them away from the agent too, honestly and visibly.
        /// </summary>
        /// <remarks>
        /// Skipping is not the same as waiting the shot out, which the 2026-08-08 decision ruled against:
        /// <c>Play</c> returns before <c>Lock</c> when <c>SkipAll</c> is set, so no shot ever starts, no
        /// timing shifts around one, and the boss intro's hold releases the same frame it does today.
        /// </remarks>
        [SetUp]
        public void SkipCutscenesSoTheAgentKeepsTheControls()
        {
            _startSkipAll = CutsceneDirector.SkipAll;
            CutsceneDirector.SkipAll = true;
        }

        [TearDown]
        public void RestoreCutscenes()
        {
            CutsceneDirector.SkipAll = _startSkipAll;

            foreach (Node node in _spawned)
            {
                if (GodotObject.IsInstanceValid(node))
                    node.QueueFree();
            }

            _spawned.Clear();
        }

        [Test]
        public async Task ScenarioRunner_UsesMyGameAdapters_AndWritesArtifacts()
        {
            string reportDirectory = null;
            try
            {
                PlayerController2D player = CreatePlayer();
                Node enemy = CreateEnemy("playmode-enemy", new Vector2(World.U(1f), 0f));

                var scenario = new UnityTestScenario("mygame-playmode-smoke", 5)
                    .WithSuccessCondition(new PlayerAliveCondition())
                    .WithFailureCondition(new PlayerDeadCondition());

                var recorder = new ReplayRecorder(scenario.Id, 7);
                reportDirectory = MyGameUnityTestAgentPaths.CreateReportDirectory(scenario.Id);
                ScenarioResult result = new ScenarioRunner().Run(
                    scenario,
                    new ScriptedAgent(new[] { AgentAction.Idle() }),
                    new MyGameStateProbe(player, new Node[] { enemy }),
                    new MyGameAgentInputDriver(player),
                    new ScenarioRunOptions
                    {
                        ReplayRecorder = recorder,
                        Reporter = new FileSystemReporter(reportDirectory)
                    });

                Assert.AreEqual(ScenarioStatus.Passed, result.Status);
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(reportDirectory, "summary.json")));
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(reportDirectory, "report.md")));
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(reportDirectory, "replay.json")));
            }
            finally
            {
                if (!string.IsNullOrEmpty(reportDirectory) && System.IO.Directory.Exists(reportDirectory))
                    System.IO.Directory.Delete(reportDirectory, true);
            }
        }

        /// <summary>
        /// The one piece of genuinely new logic in this layer: the driver now holds InputMap actions
        /// down across ticks instead of calling <c>PlayerController2D.RequestAttack()</c> behind the
        /// keyboard's back. A one-shot has to be one clean edge (or the player attacks forever), and a
        /// walk has to survive the tick it was started on (or the player never moves).
        /// </summary>
        [Test]
        public async Task InputDriver_TapsOneShots_AndHoldsMovement()
        {
            PlayerController2D player = CreatePlayer();
            var driver = new MyGameAgentInputDriver(player);
            try
            {
                driver.Apply(AgentAction.Attack());
                Assert.IsTrue(Input.IsActionPressed("attack"), "A one-shot should be held for the tick it was issued on.");

                driver.Apply(AgentAction.Move(1f));
                Assert.IsFalse(Input.IsActionPressed("attack"), "A one-shot should be released on the next tick.");
                Assert.IsTrue(Input.IsActionPressed("move_right"), "A move should press the matching direction.");

                driver.Apply(AgentAction.Move(1f));
                Assert.IsTrue(Input.IsActionPressed("move_right"), "A held direction should survive later ticks.");

                driver.Apply(AgentAction.Move(-1f));
                Assert.IsFalse(Input.IsActionPressed("move_right"), "Turning around should release the other direction.");
                Assert.IsTrue(Input.IsActionPressed("move_left"));

                driver.Apply(AgentAction.Idle());
                Assert.IsFalse(Input.IsActionPressed("move_left"), "Idle should let go of the keys.");
            }
            finally
            {
                driver.ReleaseAll();
            }
        }

        [Test]
        public async Task Agent_DefeatsFirstMeleeGrunt_InRealGameplayScene()
        {
            await LoadGameplayScene();

            PlayerController2D player = await WaitForPlayer();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            MeleeGrunt enemy = NearestOfType<MeleeGrunt>(PlayerPosition(player).X);
            Assert.NotNull(enemy, "Gameplay scene should spawn a melee grunt.");

            var inputDriver = new MyGameAgentInputDriver(player);
            var probe = new MyGameStateProbe(player, new Node[] { enemy });
            var agent = new SurvivingCombatAgent();
            var success = new MyGameTrackedEnemyDefeatedCondition(enemy.Name, enemy);
            AgentDecision lastDecision = new AgentDecision(AgentAction.Idle(), "not started");
            AgentObservation finalObservation = null;

            for (var tick = 0; tick < MaxGameplayDefeatTicks; tick++)
            {
                AgentObservation observation = probe.Capture();
                if (success.IsMet(observation))
                    return;

                lastDecision = agent.Decide(observation);
                inputDriver.Apply(lastDecision.Action);
                finalObservation = observation;

                await TestContext.Runner.NextPhysicsFrame();
            }

            finalObservation = probe.Capture();
            Assert.IsTrue(
                success.IsMet(finalObservation),
                "The scripted agent should defeat the first melee grunt in GameplayScene within " + MaxGameplayDefeatTicks + " frames. " +
                FormatFailureContext(player, enemy, finalObservation, lastDecision));
        }

        [Test]
        public async Task Agent_ReachesDuelRoutePoint_InRealGameplayScene()
        {
            await LoadGameplayScene();

            PlayerController2D player = await WaitForPlayer();
            Assert.NotNull(player, "Gameplay scene should spawn the player.");

            var inputDriver = new MyGameAgentInputDriver(player);
            var probe = new MyGameStateProbe(player, null);

            // Unity's (4, 0.5): four metres along and half a metre up. Up is -Y here.
            var route = new[] { new Vector2Observation(World.U(4f), -World.U(0.5f)) };
            var agent = new MyGameCheckpointRouteAgent(route, World.U(0.35f));
            var success = new ReachedPositionCondition(route[0], World.U(0.75f));

            for (var tick = 0; tick < MaxRouteTicks; tick++)
            {
                AgentObservation observation = probe.Capture();
                if (success.IsMet(observation))
                    return;

                inputDriver.Apply(agent.Decide(observation).Action);
                await TestContext.Runner.NextPhysicsFrame();
            }

            Assert.IsTrue(success.IsMet(probe.Capture()), "The route agent should walk the player to the duel floor approach point.");
        }

        [Test]
        public async Task Agent_SurvivesTheChapterOneBossOpening_InRealGameplayScene()
        {
            await LoadGameplayScene();

            PlayerController2D player = await WaitForPlayer();

            // Chapter one moved onto the shared chapter-boss loop on 2026-08-18. The type this reaches
            // for by name moved with it - the fight, the arena and the opening are the same.
            // Unity's search anchor was (29.5, 1); the x is all that is used.
            RainbowChapterBossBehaviour boss = NearestOfType<RainbowChapterBossBehaviour>(World.U(29.5f));
            Assert.NotNull(player, "Gameplay scene should spawn the player.");
            Assert.NotNull(boss, "Gameplay scene should spawn the chapter one boss.");
            DisableNonBossEnemies(boss);
            MovePlayerNearBoss(player, boss);

            var inputDriver = new MyGameAgentInputDriver(player);
            var probe = new MyGameStateProbe(player, null, new Node[] { boss }, null, null, null);
            var agent = new MyGameParryTimingAgent();

            for (var tick = 0; tick < MaxBossSurvivalTicks; tick++)
            {
                AgentObservation observation = probe.Capture();
                Assert.IsTrue(observation.Player.IsAlive, "The parry-timing agent should keep the player alive through the boss opening.");

                inputDriver.Apply(agent.Decide(observation).Action);
                await TestContext.Runner.NextPhysicsFrame();
            }
        }

        /// <summary>Unity's <c>LoadGameplayScene</c>. <c>TestBoot</c> parks the runner outside the
        /// current scene, so the runner survives the change and this is the whole of it.</summary>
        private static Task<Node> LoadGameplayScene() => TestContext.Runner.LoadScene(GameplayScenePath);

        /// <summary>
        /// A player actor with the four components the probe and the driver read, built the way
        /// <c>GameplayPlayerSpawner</c> builds one: the body <em>is</em> the motor, and everything else
        /// is a child node of it.
        /// </summary>
        private PlayerController2D CreatePlayer()
        {
            var body = new PlayerMotor2D
            {
                Name = "PlayModeAgentPlayer",
                CollisionLayer = World.Layer.Player,
                CollisionMask = World.Layer.GroundProbe | World.Layer.Enemy,
            };

            CollisionShape2D collider = body.AddComponent<CollisionShape2D>("Collider");
            collider.Shape = new CapsuleShape2D { Radius = World.U(0.25f), Height = World.U(1f) };

            AddHealth(body, 100f);
            body.AddComponent<StaminaSystem>().SetStamina(100f);

            // One node, not two: DamageHitbox2D is an Area2D and is its own anchor here.
            var hitbox = new DamageHitbox2D
            {
                Name = "HitboxAnchor",
                Position = new Vector2(World.U(0.4f), 0f),
                CollisionLayer = World.Layer.PlayerHitbox,
                CollisionMask = World.Layer.Enemy,
            };
            body.AddChild(hitbox);

            body.AddComponent<PlayerActionController>();
            // PlayerStateMachine is plain logic in this port, not a node - PlayerController2D owns one
            // directly, so there is nothing to add for it here.
            PlayerController2D player = body.AddComponent<PlayerController2D>();

            Spawn(body);

            // After the actor is in the tree, because PlayerController2D._Ready re-runs
            // actions.Initialize with its own null hitbox - the trap the port kept deliberately.
            player.SetHitboxAnchor(hitbox);
            player.SetDamageHitbox(hitbox);
            hitbox.Initialize(hitbox);
            return player;
        }

        private Node CreateEnemy(string name, Vector2 position)
        {
            var enemy = new CharacterBody2D
            {
                Name = name,
                Position = position,
                CollisionLayer = World.Layer.Enemy,
            };
            enemy.AddComponent<CollisionShape2D>("Collider").Shape =
                new CapsuleShape2D { Radius = World.U(0.25f), Height = World.U(1f) };
            AddHealth(enemy, 30f);
            Spawn(enemy);
            return enemy;
        }

        private static void AddHealth(Node actor, float value)
        {
            Health health = actor.AddComponent<Health>();
            health.SetMaxHealth(value);
            health.SetHealth(value);
        }

        /// <summary>
        /// Parents a fixture actor onto the runner rather than the open scene, so a later
        /// <c>LoadScene</c> cannot take it out from under the test, and records it for teardown.
        /// </summary>
        private void Spawn(Node node)
        {
            TestContext.Runner.AddChild(node);
            _spawned.Add(node);
        }

        /// <summary>
        /// Unity spun on <c>GameObject.Find("Player")</c> for sixty iterations of the same frame, which
        /// could only ever find something already there. This actually waits.
        /// </summary>
        private static async Task<PlayerController2D> WaitForPlayer()
        {
            PlayerController2D player = null;
            await TestContext.Runner.WaitUntil(() => (player = FindPlayer()) != null);
            return player;
        }

        private static PlayerController2D FindPlayer()
        {
            foreach (Node node in TestContext.Tree.GetNodesInGroup(World.Group.Player))
            {
                PlayerController2D found = node.GetComponentInChildren<PlayerController2D>();
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>The live <typeparamref name="T"/> whose x is closest to <paramref name="x"/>.</summary>
        private static T NearestOfType<T>(float x) where T : Node2D
        {
            T nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (T candidate in TestContext.CurrentScene.FindComponentsInChildren<T>())
            {
                if (!GodotObject.IsInstanceValid(candidate) || !candidate.IsVisibleInTree())
                    continue;

                float distance = Mathf.Abs(candidate.GlobalPosition.X - x);
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static void DisableNonBossEnemies(Node boss)
        {
            foreach (EnemyStateMachine enemy in TestContext.CurrentScene.FindComponentsInChildren<EnemyStateMachine>())
            {
                if (enemy == boss)
                    continue;

                // Hidden is not enough - SetActive also stops the tick and the collision shapes, which
                // is what "SetActive(false)" meant in Unity and what an enemy has to stop doing here.
                enemy.SetActive(false);
            }
        }

        /// <summary>
        /// Puts the player next to the boss. Unity had to turn the rigidbody's simulation off across
        /// the move, because a Continuous-collision body teleported 290 units swept the length of the
        /// chapter on the way. A <see cref="CharacterBody2D"/> only sweeps inside
        /// <c>MoveAndSlide</c>, so writing <c>GlobalPosition</c> between physics steps moves it
        /// outright - the velocity is still zeroed so the first step after does not carry the walk it
        /// was doing into the arena.
        /// </summary>
        private static void MovePlayerNearBoss(PlayerController2D player, Node2D boss)
        {
            PlayerMotor2D motor = player.GetComponentInParent<PlayerMotor2D>();
            if (motor == null)
                return;

            motor.ResetMotion();
            motor.GlobalPosition = boss.GlobalPosition + new Vector2(-World.U(1.5f), 0f);
        }

        private static Vector2 PlayerPosition(PlayerController2D player)
        {
            Node2D body = player.GetComponentInParent<PlayerMotor2D>();
            return body != null ? body.GlobalPosition : player.GlobalPosition;
        }

        private static string FormatFailureContext(
            PlayerController2D player, Node enemy, AgentObservation observation, AgentDecision decision)
        {
            string playerText = observation?.Player != null
                ? "player=(" + observation.Player.Position.X + "," + observation.Player.Position.Y + ") hp=" + observation.Player.HitPoints + " stamina=" + observation.Player.Stamina + " moveState=" + observation.Player.MovementState + " combatState=" + observation.Player.CombatState
                : "playerObservation=<null>";

            string observedEnemyText = observation != null && observation.Enemies.Count > 0
                ? "observedEnemy=(" + observation.Enemies[0].Position.X + "," + observation.Enemies[0].Position.Y + ") hp=" + observation.Enemies[0].HitPoints + " alive=" + observation.Enemies[0].IsAlive + " attacking=" + observation.Enemies[0].IsAttacking
                : "observedEnemy=<none>";

            // The state that cost a whole run to work out on 2026-08-17 - except the sign has flipped
            // with the input path. The agent now *needs* the receiver enabled; a disabled one means
            // something took the controls away and every action this fixture presses is going nowhere.
            PlayerInputReceiver receiver = player?.GetComponentInParent<PlayerInputReceiver>();

            return playerText +
                   " " + observedEnemyText +
                   " inputReceiverEnabled=" + (receiver != null && receiver.Enabled) +
                   " enemyStillAlive=" + GodotObject.IsInstanceValid(enemy) +
                   " enemyPosition=" + (enemy is Node2D e && GodotObject.IsInstanceValid(e) ? e.GlobalPosition.ToString() : "<freed>") +
                   " playerPosition=" + (player != null ? PlayerPosition(player).ToString() : "<freed>") +
                   " lastAction=" + decision.Action.Type +
                   " lastReason=" + decision.Reason;
        }
    }
}
