using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Gameplay;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// QA regressions for the chapter-two hazard strip. The fixtures author their own numbers as JSON so
    /// retuning a shipped boss never turns them red - except
    /// <see cref="ChapterTwoHopNumbers_CannotStackAHopAtItsOwnApex"/>, which reads the shipped file on
    /// purpose, because the break it guards is a designer edit rather than a code edit.
    /// </summary>
    /// <remarks>
    /// PORT NOTES: the fixture JSON stays in Unity metres and goes through the same
    /// <c>OnValidate</c> + <c>ScaleToPixels</c> pass <see cref="RainbowChapterBossData.Load"/> runs, so
    /// every distance in it reaches the boss in pixels. Positions written as world units are converted
    /// with <see cref="World.V"/> (which flips Y) and bare distances with <see cref="World.U"/>.
    /// </remarks>
    public sealed class GameplayHazardStripRegressionTests
    {
        /// <summary>
        /// What <see cref="EnemyStateMachine"/> applies to an airborne enemy every physics step, because
        /// <c>project.godot</c> sets <c>default_gravity = 0</c>. It stands in for Unity's
        /// <c>Physics2D.gravity.y</c> at <c>gravityScale = 1</c>, and it is in pixels/s^2 - the same
        /// units <c>ApproachHopImpulse</c> is in after the load, so their ratio is still seconds.
        /// </summary>
        private static readonly float EnemyGravity = World.U(9.81f);

        private RainbowChapterBossBehaviour _boss;
        private PlayerMotor2D _player;
        private BossHazardStrip _hazard;

        private static Node FixtureRoot => GameplayBuildShim.SceneRoot;

        /// <summary>
        /// A strip is a root object that outlives whoever dropped it, and nothing in the game or in the
        /// other suites destroys one early, so a leftover burn can cross a test boundary. Cleared both
        /// ends: before, so an inherited strip is not read as this test's, and after, so this test does
        /// not hand one to the next.
        /// </summary>
        [SetUp]
        public void SetUp() => DestroyEveryStrip();

        [TearDown]
        public void TearDown()
        {
            DestroyEveryStrip();

            if (GodotObject.IsInstanceValid(_boss))
                _boss.QueueFree();
            if (GodotObject.IsInstanceValid(_player))
                _player.QueueFree();
            if (GodotObject.IsInstanceValid(_hazard))
                _hazard.QueueFree();

            _boss = null;
            _player = null;
            _hazard = null;
        }

        private static void DestroyEveryStrip()
        {
            foreach (BossHazardStrip strip in SceneQuery.FindAll<BossHazardStrip>())
            {
                if (GodotObject.IsInstanceValid(strip))
                    strip.QueueFree();
            }
        }

        /// <summary>
        /// Burning ground is not a swing, and answering it with a parry must not pay what parrying a
        /// swing pays. <see cref="CombatResolver"/> asks the guard before it looks at what is hitting, so
        /// a tick landing inside the window is published as a successful parry: it negates the tick (the
        /// accepted trade-off) and it also raises <see cref="PlayerController2D.OnParrySuccess"/>, which
        /// <see cref="CombatResultBroadcaster"/> turns into resonance and parry hit-stop.
        ///
        /// The shipped numbers make that a farm rather than an edge case: the Ember Pilgrim's strip ticks
        /// every 0.5s and the player's parry cooldown is 0.5s, so a player synced to the tick can parry
        /// every one of them - a full resonance bar for standing in fire and taking nothing for it.
        /// </summary>
        [Test]
        public async Task HazardTick_InsideAParryWindow_DoesNotCreditAParry()
        {
            PlayerController2D player = BuildPlayerAt(Vector2.Zero);

            // PORT CHANGE: the components are siblings under the body now, so Health is reached from the
            // controller's parent rather than off the same GameObject.
            var health = player.GetComponentInParent<Health>();
            float before = health.CurrentHealth;

            var parries = 0;
            player.OnParrySuccess += () => parries++;

            // Damage and the two times are unscaled; the radius inside BuildStripAt is the one distance.
            BossHazardStrip strip = BuildStripAt(Vector2.Zero, damage: 4f, tickInterval: 0.05f, duration: 1f);
            Assert.NotNull(strip);

            var sawParryWindow = false;

            float deadline = GameClock.Time + 1f;
            while (GameClock.Time < deadline)
            {
                // Re-requested every frame; the 0.5s parry cooldown is what actually paces it, so the
                // window opens and closes several times inside the strip's life.
                player.RequestParry();

                await TestContext.Runner.NextFrame();

                sawParryWindow |= player.IsInParryWindow();
            }

            Assert.IsTrue(sawParryWindow, "The fixture never opened a parry window, so it proves nothing about one.");
            Assert.Less(health.CurrentHealth, before, "The strip never resolved a tick, so nothing was offered to the guard.");

            Assert.AreEqual(0, parries,
                "A hazard tick resolved inside a parry window is published as a successful parry, which pays " +
                "resonance and parry hit-stop for standing in fire. Ground damage has to bypass the guard's " +
                "parry branch, or carry a flag CombatResolver can tell apart from a swing.");
        }

        /// <summary>
        /// The approach hop reads "grounded" off the vertical velocity
        /// (<c>RainbowChapterBossBehaviour.TickApproachHop</c>), and vertical velocity passes through zero
        /// at the top of every hop. The only thing stopping a second impulse there is the hop interval
        /// still being on cooldown, which holds solely because the authored interval is longer than the
        /// time to apex. Nothing in code or <c>OnValidate</c> enforces that relation, so lowering
        /// <c>approachHopInterval</c> - a pure tuning edit with no code change - gives a boss that
        /// re-hops at every apex and climbs out of an arena that clamps X and not Y.
        /// </summary>
        [Test]
        public void ChapterTwoHopNumbers_CannotStackAHopAtItsOwnApex()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            GameplaySceneDefaults scene = GameplaySceneDefaults.Create();
            catalog.SceneLayoutFor("Chapter02_Orange").ApplyTo(scene);

            RainbowChapterBossData data = catalog.ChapterBoss(scene.BossDataFile);
            Assert.NotNull(data, "The chapter-two boss file has to load, or this guards nothing.");

            if (!data.HopsWhileApproaching)
                return;

            // PORT CHANGE: Unity read Physics2D.gravity.y, because CreateEnemyRoot left gravityScale at
            // 1. Godot's project gravity is zero and EnemyStateMachine applies World.U(9.81f) itself, so
            // that constant is what the apex is measured against. Impulse and gravity are both in pixels,
            // so the quotient is the same number of seconds Unity computed in metres.
            float timeToApex = data.ApproachHopImpulse / EnemyGravity;

            Assert.Greater(data.ApproachHopInterval, timeToApex,
                $"approachHopInterval ({data.ApproachHopInterval}) is not longer than the time to the apex of " +
                $"one hop ({timeToApex}), so the boss can take a second hop while it is still in the air. " +
                "X is clamped to the arena; Y is not.");
        }

        /// <summary>
        /// The strip is the reward for a commitment the boss finished. Death is the one interruption that
        /// does not run through <c>Stun()</c>, so it needs its own proof: killing the boss mid-swing must
        /// leave no ground denial behind, or the last exchange of the fight hands a corpse a hazard the
        /// player still has to walk out of while the victory beat is already playing.
        /// </summary>
        [Test]
        public async Task KillingTheBossMidSwing_LeavesNoStrip()
        {
            RainbowChapterBossBehaviour boss = BuildHazardBoss(out RainbowChapterBossData data);

            Assert.IsTrue(boss.RequestAttack(data.Attacks[0]), "The swing has to be in the air before death can interrupt it.");
            Assert.IsTrue(boss.IsAttackRunning);

            // Damage is unscaled by the port; Vector2.right is Vector2.Right.
            boss.GetComponent<Health>().ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(boss.IsDefeated, "A lethal hit should kill the boss.");
            Assert.IsFalse(boss.IsAttackRunning, "Death drops the swing rather than letting it finish.");

            // Longer than the whole authored swing, so a strip dropped late would still have appeared.
            await TestContext.Runner.Seconds(1.5f);

            // Assert.IsNull rather than == null on purpose: the harness's IsNull also fails a freed Godot
            // object, so it catches a strip that was created and has since burnt out too.
            Assert.IsNull(boss.LastHazard, "A swing the boss died in the middle of never earned its strip.");
        }

        /// <summary>
        /// Ground denial has to be on the ground. Nothing gates an attack on being grounded and
        /// <c>StopMovement</c> only zeroes X, so a swing begun at the top of an approach hop finishes
        /// airborne - and a strip dropped at the body centre then hangs above the floor, where a player who
        /// can only walk never has to leave it. That failure is silent: the fire appears, it just cannot be
        /// stood in, so the mechanic reads as tuned-to-nothing rather than as broken.
        /// </summary>
        /// <remarks>
        /// PORT NOTE: Unity's kinematic body simply stayed where it was put for the whole swing. The
        /// ported boss is a <see cref="CharacterBody2D"/> that falls at <c>World.U(9.81f)</c> with no
        /// floor under it, so the lift has to outlast the swing rather than be permanent: 3 metres is
        /// 300 px, and 0.5s of telegraph plus active window costs about 123 px of it. The boss is
        /// therefore still well clear of the floor at the moment the strip drops, which is the condition
        /// this test needs.
        /// </remarks>
        [Test]
        public async Task HazardDroppedMidAir_LandsOnTheFloorTheBossSpawnedOn()
        {
            RainbowChapterBossBehaviour boss = BuildHazardBoss(out RainbowChapterBossData data);

            // The boss spawns standing on the floor, which is the height the strip has to end up at.
            float floorY = boss.GlobalPosition.Y;

            // Where an approach hop leaves the body. Y FLIP: Unity's Vector3.up * 3 is 300 px of
            // *subtracted* Y here, because up is -Y in Godot.
            boss.GlobalPosition -= new Vector2(0f, World.U(3f));

            Assert.IsTrue(boss.RequestAttack(data.Attacks[0]), "The swing has to start before it can drop anything.");

            await TestContext.Runner.WaitUntil(() => !boss.IsAttackRunning, 3f);

            Assert.IsFalse(boss.IsAttackRunning, "The swing should finish on its own.");
            Assert.NotNull(boss.LastHazard,
                "A finished swing on a hazard-leaving row has to leave a strip, or this test guards nothing.");

            // Tolerance converts with the axis: Unity's 0.01 world units is 1 px.
            Assert.AreEqual(floorY, boss.LastHazard.GlobalPosition.Y, World.U(0.01f),
                "The strip was dropped at the boss's body centre, which was three units up mid-hop. Ground " +
                "denial the player can walk under is not ground denial.");
        }

        // ------------------------------------------------------------------------------------------

        private RainbowChapterBossBehaviour BuildHazardBoss(out RainbowChapterBossData data)
        {
            // Authored in Unity metres, then put through the two passes RainbowChapterBossData.Load runs
            // after reading a design file. Without ScaleToPixels the 1.5 range would be 1.5 pixels.
            data = JsonData.FromJson<RainbowChapterBossData>(
                "{\"maxHealth\":100,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                "\"phaseTwoHealthThreshold\":0.5,\"phaseTwoSpeedMultiplier\":1,\"phaseTwoCooldownMultiplier\":1," +
                // Stun, poise, body and pulse were class defaults until K3; a boss with a zero body or a
                // zero stun is not the one these tests were written against, so the fixture says them.
                "\"maxPoise\":90,\"poiseHeavyMultiplier\":2,\"poiseRegenDelay\":3,\"poiseRegenRate\":30,\"stunDuration\":1,\"soulReward\":300," +
                "\"bodySize\":{\"x\":1.6,\"y\":2.3},\"telegraphPulseSpeed\":8,\"telegraphPulseAmplitude\":0.2," +
                "\"bossName\":\"HazardDeathFixture\",\"chapterName\":\"Test Chapter\"," +
                "\"attacks\":[{\"attackId\":\"test_lunge\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":1,\"knockback\":0," +
                "\"telegraphTime\":0.3,\"activeTime\":0.2,\"recoveryTime\":0.2,\"range\":1.5,\"forwardOffset\":0.5," +
                "\"phaseTwoWeight\":1,\"leavesHazard\":true,\"hazardPhaseTwoOnly\":false," +
                "\"hazardDamage\":1,\"hazardRadius\":1,\"hazardTickInterval\":0.1,\"hazardDuration\":1," +
                "\"hazardForwardOffset\":0.5}]}");

            Assert.NotNull(data, "The fixture JSON has to parse.");
            data.OnValidate();
            data.ScaleToPixels();

            Assert.AreEqual(1, data.Attacks.Length, "The fixture boss should carry exactly one authored attack.");
            Assert.IsTrue(data.Attacks[0].ShouldLeaveHazard(false), "The fixture attack has to be one that leaves a strip.");

            _boss = new RainbowChapterBossBehaviour
            {
                Name = "HazardDeathBossFixture",

                // Unity (20, 0, 0) metres -> Godot (2000, 0) px. Far from anything else a test built.
                Position = World.V(new Vector2(20f, 0f)),
            };

            // Required since K5b: EnemyStateMachine has no initialisers for the eight shared
            // numbers and refuses to run unconfigured. Before the tree, which is where the spawner
            // does it.
            EnemyFixture.ConfigureFromDesign(_boss);

            // Unity added a Kinematic Rigidbody2D; the ported boss is the body itself.
            _boss.AddComponent<Health>();

            // Assembled detached and entered in one piece: the behaviour's _Ready expects Health to
            // already be a sibling.
            FixtureRoot.AddChild(_boss);

            // Required since K5 - see GameplayBossAttackGrammarTests for why chapter one's file.
            _boss.SetEncounterData(BossEncounterData.Load("Design/WrathEncounter"));
            _boss.SetBossData(data);
            return _boss;
        }

        private BossHazardStrip BuildStripAt(Vector2 position, float damage, float tickInterval, float duration)
        {
            // Instanced from the shipped scene rather than built here, so the fixture is the strip the
            // boss actually drops - sprite included. Unity's GameObject + AddComponent<BossHazardStrip>
            // is one node here: the strip is the scene's root.
            _hazard = GD.Load<PackedScene>(BossHazardStrip.ScenePath).Instantiate<BossHazardStrip>();
            _hazard.Name = "HazardStripFixture";
            _hazard.Position = position;
            FixtureRoot.AddChild(_hazard);

            // Configure takes a radius already in pixels, so the authored 1.5 metres converts here.
            _hazard.Configure(null, damage, World.U(1.5f), tickInterval, duration);
            return _hazard;
        }

        /// <summary>
        /// The same shape <see cref="GameplayChapterBossTests"/> builds: a real
        /// <see cref="PlayerController2D"/>, because it is the <see cref="IDamageGuard"/> the resolver
        /// asks and a stub would not be the thing under test. No damage hitbox is wired - this player
        /// never attacks - so the SetDamageHitbox trap does not apply here.
        /// </summary>
        private PlayerController2D BuildPlayerAt(Vector2 position)
        {
            _player = new PlayerMotor2D
            {
                Name = "HazardTestPlayer",
                Position = position,
                CollisionLayer = World.Layer.Player,
                CollisionMask = 0,
            };

            // Unity: CircleCollider2D radius 0.4 -> 40 px.
            _player.AddChild(new CollisionShape2D
            {
                Name = "Collider",
                Shape = new CircleShape2D { Radius = World.U(0.4f) },
            });

            Health health = _player.AddComponent<Health>();
            DamageReceiver receiver = _player.AddComponent<DamageReceiver>();
            PlayerController2D controller = _player.AddComponent<PlayerController2D>();

            // Built here rather than left to PlayerController2D._Ready, which adds one deferred and
            // hands it no tuning - and required since K7b, where an unconfigured component reports
            // itself and stops. Both are the shape Scenes/Actors/Player.tscn already ships.
            PlayerFixture.Configure(_player.AddComponent<PlayerActionController>());
            PlayerFixture.Configure(_player);

            FixtureRoot.AddChild(_player);

            // After entering the tree: Health._Ready resets current to max. Health is unscaled.
            health.SetMaxHealth(500f);
            health.SetHealth(500f);
            receiver.Initialize(health);

            return controller;
        }
    }
}
