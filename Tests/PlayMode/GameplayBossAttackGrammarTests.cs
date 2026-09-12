using System;
using System.Threading.Tasks;
using Godot;
using MyGame.Combat;
using MyGame.Core;
using MyGame.Enemy;
using MyGame.Player;

namespace MyGame.Tests
{
    /// <summary>
    /// The vocabulary chapters three to eight are authored out of: a feint, a chain, and a pull. All
    /// three are things a boss row can now say, and all three are silent when they break - a feint that
    /// never snaps is an attack that missed, a chain that never fires is an attack that ended, and a pull
    /// that goes the wrong way is a shove.
    ///
    /// Fixtures author their own JSON so retuning a shipped boss never turns these red.
    ///
    /// UNITS - the fixture JSON below is the <b>authored Unity number</b>, metres and seconds, exactly as
    /// a design file would be, and <see cref="BuildBossData"/> puts it through the same
    /// <c>OnValidate</c> + <c>ScaleToPixels</c> pair <see cref="RainbowChapterBossData.Load"/> runs. So
    /// by the time the boss reads them, range 1.5 is 150 px, forwardOffset 0.5 is 50 px, knockback 6 is
    /// 600 px/s and attackRange 1.5 is 150 px, while every damage, weight and time crosses unchanged.
    /// Loading the fixture with a bare <c>JsonData.FromJson</c> would leave the boss swinging at a
    /// metre and a half of pixels, which is a hitbox the size of a fingernail.
    ///
    /// PORT: Unity made both fixtures kinematic <c>Rigidbody2D</c>s so nothing fell. Both actors are
    /// <c>CharacterBody2D</c>s here and each supplies its own gravity (project.godot sets the engine's
    /// to zero at 9.81 m/s^2 for an enemy and 20 for the player), and there is still no floor - so
    /// "kinematic" is the per-frame pin, which now holds the boss as well as the player. Only the feint
    /// needs it: it is the one window long enough (telegraph plus pause) for two bodies falling at
    /// different rates to drift out of each other's reach before the attack snaps.
    /// </summary>
    public sealed class GameplayBossAttackGrammarTests
    {
        private RainbowChapterBossBehaviour _boss;
        private PlayerMotor2D _player;

        [TearDown]
        public void TearDown()
        {
            foreach (BossHazardStrip strip in SceneQuery.FindAll<BossHazardStrip>())
            {
                if (GodotObject.IsInstanceValid(strip))
                    strip.Free();
            }

            // Free rather than QueueFree: Unity's Object.Destroy landed at the end of the frame and the
            // next test never saw the corpse. A queued free here would still be in the tree when the next
            // fixture's boss looks for a player, so the teardown is made immediate instead.
            if (GodotObject.IsInstanceValid(_boss))
                _boss.Free();
            if (GodotObject.IsInstanceValid(_player))
                _player.Free();

            _boss = null;
            _player = null;
        }

        /// <summary>
        /// The frame every fixture waits before it builds anything. The first test of a run starts
        /// inside the runner's own <c>_Ready</c>, and a node cannot be added to a parent that is still
        /// propagating ready - Godot refuses the add outright and the fixture would be left detached.
        /// One frame puts the test back on ordinary ground.
        /// </summary>
        private static Task OneFrame() => TestContext.Runner.NextFrame();

        /// <summary>
        /// The feint has to be a real pause with no hit in it, and it has to end in the attack landing.
        /// A feint that can damage during the pause is just a longer active window, and one that never
        /// reaches its active window is a boss that stopped attacking.
        /// </summary>
        [Test]
        public async Task Feint_PausesWithoutHitting_ThenSnapsActive()
        {
            await OneFrame();

            RainbowChapterBossBehaviour boss = BuildBoss(FeintJson(), out RainbowChapterBossData data);

            // Half a unit to the boss's right - 50 px, and +x is +x in both engines.
            Vector2 stand = boss.GlobalPosition + Vector2.Right * World.U(0.5f);
            Health playerHealth = BuildPlayerAt(stand);
            float before = playerHealth.CurrentHealth;

            Assert.IsTrue(boss.RequestAttack(data.Attacks[0]));

            // Pinned every frame: a feint is long enough - telegraph plus pause - that a player left to
            // its own gravity drifts before it snaps. Standing still through the pause is the behaviour
            // under test.
            await WaitUntilPinned(stand, () => boss.IsFeinting || boss.IsAttackActive, 2f,
                "The telegraph should have ended into the feint pause.");

            Assert.IsTrue(boss.IsFeinting, "A row with a feint pause must not go straight from telegraph to active.");
            Assert.AreEqual(before, playerHealth.CurrentHealth, 0.01f, "Nothing lands during the pause.");

            await WaitUntilPinned(stand, () => boss.IsAttackActive || !boss.IsAttackRunning, 2f,
                "The feint has to snap into a real active window.");

            Assert.Less(playerHealth.CurrentHealth, before,
                "A feint that never connects is not a feint, it is an attack that missed.");
        }

        /// <summary>
        /// A chain runs its next link on its own beat rather than waiting out the recovery, and it stops
        /// at the end of the authored chain rather than looping.
        /// </summary>
        [Test]
        public async Task Chain_RunsTheNextLink_AndStops()
        {
            await OneFrame();

            RainbowChapterBossBehaviour boss = BuildBoss(ChainJson(), out RainbowChapterBossData data);

            var started = 0;

            // Unity's UnityEvent.AddListener; the ported events are plain C# events.
            boss.OnAttackRequested += () => started++;

            Assert.IsTrue(boss.RequestAttack(data.Attacks[0]));

            await WaitUntil(() => boss.HasQueuedChain, 2f, "Finishing the first link should queue the second.");
            await WaitUntil(() => started >= 2, 2f, "The queued link has to actually run.");

            Assert.AreEqual("wave_b", boss.CurrentAttackProfile?.AttackId ?? "none",
                "The chain should have run the row it named, not the next one in the array.");

            await WaitUntil(() => !boss.IsAttackRunning && !boss.HasQueuedChain, 3f,
                "The second link ends the chain, so nothing should be queued after it.");

            // Long enough for another link to have fired if the chain had looped back on itself. No
            // player is built for this fixture, so the boss picks nothing on its own and the only
            // requests are the chain's.
            await TestContext.Runner.Seconds(0.6f);
            Assert.AreEqual(2, started, "A two-link chain runs twice. More than that is a loop with no punish window.");
        }

        /// <summary>
        /// Interrupting one link has to end the whole chain. Breaking a swing and then eating the rest of
        /// the combo is not a punish window.
        /// </summary>
        [Test]
        public async Task Chain_DiesWithTheSwingThatCarriedIt()
        {
            await OneFrame();

            RainbowChapterBossBehaviour boss = BuildBoss(ChainJson(), out RainbowChapterBossData data);

            boss.RequestAttack(data.Attacks[0]);
            await WaitUntil(() => boss.HasQueuedChain, 2f, "The first link should queue the second.");

            boss.OnPoiseBroken();

            Assert.IsFalse(boss.HasQueuedChain, "A broken swing takes the rest of its chain with it.");
        }

        /// <summary>
        /// A pull drags the target toward the boss. Authoring it as a negative knockback would do nothing
        /// at all - <see cref="PlayerController2D"/> ignores any force that is not above zero - so the
        /// direction is what carries it, and the direction is what this checks.
        /// </summary>
        [Test]
        public async Task Pull_SendsTheTargetTowardTheBoss()
        {
            await OneFrame();

            RainbowChapterBossBehaviour boss = BuildBoss(PullJson(), out RainbowChapterBossData data);

            // Player to the right of the boss, so a shove reads +x and a pull reads -x. The x axis does
            // not flip between the engines, so the whole claim survives the port unchanged.
            Health playerHealth = BuildPlayerAt(boss.GlobalPosition + Vector2.Right * World.U(0.5f));

            boss.RequestAttack(data.Attacks[0]);
            await WaitUntil(() => playerHealth.CurrentHealth < playerHealth.MaxHealth, 2f,
                "The attack has to land before its knockback direction means anything.");

            // 600 px/s by the time it reaches Health - the authored 6 m/s through ScaleToPixels. Only its
            // sign is asserted, which is the claim: a pull keeps a positive force.
            Assert.Greater(playerHealth.LastKnockback, 0f,
                "A pull keeps a positive force; only its direction differs. Zero or less is dropped outright.");

            Assert.Less(playerHealth.LastHitDirection.X, 0f,
                "The player stands to the boss's right, so a pull has to push them in -x - back toward the boss.");
        }

        private static string FeintJson()
        {
            return BossJson(
                "{\"attackId\":\"feint_swing\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":10,\"knockback\":0,\"telegraphTime\":0.2," +
                "\"feintPauseTime\":0.35,\"activeTime\":0.25,\"recoveryTime\":0.2,\"range\":1.5," +
                "\"forwardOffset\":0.5,\"phaseTwoWeight\":1}");
        }

        private static string ChainJson()
        {
            return BossJson(
                "{\"attackId\":\"wave_a\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":5,\"knockback\":0,\"telegraphTime\":0.15," +
                "\"activeTime\":0.15,\"recoveryTime\":0.15,\"range\":1.5,\"forwardOffset\":0.5," +
                "\"phaseTwoWeight\":1,\"chainNextAttackId\":\"wave_b\",\"chainDelay\":0.15," +
                "\"chainDelayPhaseTwoScale\":1}," +
                "{\"attackId\":\"wave_b\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":5,\"knockback\":0,\"telegraphTime\":0.15," +
                "\"activeTime\":0.15,\"recoveryTime\":0.15,\"range\":1.5,\"forwardOffset\":0.5," +
                "\"phaseTwoWeight\":1}");
        }

        private static string PullJson()
        {
            return BossJson(
                "{\"attackId\":\"tide_pull\",\"damageType\":1,\"afterimageCountOverride\":-1,\"damage\":8,\"knockback\":6,\"telegraphTime\":0.15," +
                "\"activeTime\":0.25,\"recoveryTime\":0.2,\"range\":1.5,\"forwardOffset\":0.5," +
                "\"phaseTwoWeight\":1,\"pullsTarget\":true}");
        }

        private static string BossJson(string attackRows)
        {
            return "{\"maxHealth\":200,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                   "\"phaseTwoHealthThreshold\":0.5,\"phaseTwoSpeedMultiplier\":1,\"phaseTwoCooldownMultiplier\":1," +
                   // Stun, poise, body and pulse were class defaults until K3; a boss with a zero body or a
                   // zero stun is not the one these tests were written against, so the fixture says them.
                   "\"maxPoise\":90,\"poiseHeavyMultiplier\":2,\"poiseRegenDelay\":3,\"poiseRegenRate\":30,\"stunDuration\":1,\"soulReward\":300," +
                   "\"bodySize\":{\"x\":1.6,\"y\":2.3},\"telegraphPulseSpeed\":8,\"telegraphPulseAmplitude\":0.2," +
                   "\"bossName\":\"GrammarFixture\",\"chapterName\":\"Test Chapter\"," +
                   "\"attacks\":[" + attackRows + "]}";
        }

        /// <summary>
        /// Unity's <c>JsonUtility.FromJsonOverwrite</c> onto a fresh ScriptableObject. The Godot half is
        /// <c>JsonData.FromJson</c> plus the two steps <see cref="RainbowChapterBossData.Load"/> would
        /// have run - the clamp and the metres-to-pixels conversion - because a fixture that skips them
        /// is not the data the boss reads at runtime.
        /// </summary>
        private static RainbowChapterBossData BuildBossData(string json)
        {
            RainbowChapterBossData data = JsonData.FromJson<RainbowChapterBossData>(json);
            Assert.NotNull(data, "The fixture JSON should bind onto RainbowChapterBossData.");

            data.OnValidate();
            data.ScaleToPixels();
            return data;
        }

        private RainbowChapterBossBehaviour BuildBoss(string json, out RainbowChapterBossData data)
        {
            data = BuildBossData(json);

            // A Unity component on the boss GameObject is a child node here, and both the state machine's
            // _Ready and the behaviour's read the Health before anything else - so the rig is built while
            // the boss is still detached, then the whole thing enters the tree at once.
            _boss = new RainbowChapterBossBehaviour
            {
                Name = "AttackGrammarFixture",
                Position = Vector2.Zero,
                CollisionLayer = World.Layer.Enemy,

                // Nothing to stand on and nothing to catch on: an empty mask keeps the fixture from
                // colliding with whatever scene is open behind the test.
                CollisionMask = 0,
            };

            // Required since K5b: EnemyStateMachine has no initialisers for the eight shared
            // numbers and refuses to run unconfigured. Before the tree, which is where the spawner
            // does it.
            EnemyFixture.ConfigureFromDesign(_boss);

            var collider = _boss.AddComponent<CollisionShape2D>("Collider");
            collider.Shape = new CapsuleShape2D { Radius = World.U(0.8f), Height = World.U(2.3f) };
            _boss.AddComponent<Health>();

            TestContext.Tree.Root.AddChild(_boss);

            // Required since K5: the behaviour's arena, intro cap, punish floor and chain cap all read
            // the encounter directly now. Chapter one's shipped file, the same one the spawner hands a
            // boss whose arena names no encounter of its own - so the fixture fights in a real room
            // rather than in constants the class used to carry.
            _boss.SetEncounterData(BossEncounterData.Load("Design/WrathEncounter"));
            _boss.SetBossData(data);
            _boss.SkipIntro();
            return _boss;
        }

        private Health BuildPlayerAt(Vector2 position)
        {
            // PlayerMotor2D *is* the player body in this port, so the fixture's actor root is the motor
            // rather than a bare GameObject with a rigidbody bolted on. Its _Ready joins the player group,
            // which is how the boss's overlap query finds it.
            _player = new PlayerMotor2D
            {
                Name = "AttackGrammarPlayer",
                Position = position,
                CollisionLayer = World.Layer.Player,

                // Empty, where a real player carries World/Ground/Enemy: the boss stands half a metre
                // away and would otherwise shove the fixture out of its own attack.
                CollisionMask = 0,
            };

            var collider = _player.AddComponent<CollisionShape2D>("Collider");
            collider.Shape = new CircleShape2D { Radius = World.U(0.4f) };

            Health health = _player.AddComponent<Health>();
            _player.AddComponent<DamageReceiver>();

            // Built here rather than left to PlayerController2D._Ready, which does
            // `motor.AddChild(actions)` while the motor is still propagating ready - Godot refuses that
            // add outright ("Parent node is busy setting up children"), so the controller would be left
            // holding an orphaned action controller and the parry guard would never see a parry.
            // Scripts/Player/PlayerController2D.cs:136.
            PlayerFixture.Configure(_player.AddComponent<PlayerActionController>());
            _player.AddComponent<PlayerController2D>();

            // Required since K7b, for the same reason as EnemyFixture above: the player components
            // carry no initialisers either, and the motor's own tuning is PlayerMovement.json's.
            PlayerFixture.Configure(_player);

            TestContext.Tree.Root.AddChild(_player);

            health.SetMaxHealth(200f);
            health.SetHealth(200f);
            _player.GetComponent<DamageReceiver>().Initialize(health);

            return health;
        }


        /// <summary>
        /// <see cref="WaitUntil"/>, holding both bodies where they were put - Unity's coroutine that
        /// wrote <c>transform.position</c> every frame, which is <c>GlobalPosition</c> here, extended to
        /// the boss because Unity's kinematic rigidbody held it and a CharacterBody2D does not.
        /// </summary>
        private async Task WaitUntilPinned(Vector2 stand, Func<bool> done, float seconds, string message)
        {
            Vector2 bossStand = _boss.GlobalPosition;

            double deadline = Time.GetTicksMsec() + seconds * 1000.0;
            while (!done() && Time.GetTicksMsec() < deadline)
            {
                Pin(bossStand, stand);
                await TestContext.Runner.NextFrame();
            }

            Pin(bossStand, stand);
            Assert.IsTrue(done(), message);
        }

        private void Pin(Vector2 bossStand, Vector2 playerStand)
        {
            _boss.GlobalPosition = bossStand;
            _boss.Velocity = Vector2.Zero;
            _player.GlobalPosition = playerStand;
            _player.Velocity = Vector2.Zero;
        }

        private static async Task WaitUntil(Func<bool> done, float seconds, string message)
        {
            bool reached = await TestContext.Runner.WaitUntil(done, seconds);
            Assert.IsTrue(reached, message);
        }
    }
}
