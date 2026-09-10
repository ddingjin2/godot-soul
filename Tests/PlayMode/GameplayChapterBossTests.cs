using System;
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
    /// The claim the chapter boss layer has to earn: a boss authored as data fights. Nothing here writes
    /// a pattern in C# - the attacks come off a <see cref="RainbowChapterBossData"/> asset, and the
    /// behaviour reads them out.
    ///
    /// Most of it is built by hand rather than loaded from a scene, so retuning a shipped boss never
    /// turns these red. <see cref="ChapterTwoScene_SpawnsTheAuthoredBoss"/> is the exception and the
    /// end of the argument: it loads the arena the game ships and checks who is standing in it.
    /// </summary>
    /// <remarks>
    /// PORT NOTES for the whole fixture:
    /// <list type="bullet">
    /// <item><description>The fixture JSON below is left exactly as it was authored - Unity metres - and
    /// then put through <c>OnValidate()</c> + <c>ScaleToPixels()</c>, which is what
    /// <see cref="RainbowChapterBossData.Load"/> does after reading a design file. A raw
    /// <c>JsonData.FromJson</c> alone would leave the boss measured in metres, so its 1.5 m attack range
    /// would be 1.5 <i>pixels</i> and nothing would ever connect.</description></item>
    /// <item><description>Every offset the Unity fixture wrote as a bare number of world units is
    /// wrapped in <see cref="World.U"/> here, and every vertical one flips sign.</description></item>
    /// <item><description>Unity's fixture bodies were <c>Rigidbody2D</c> set to Kinematic. The ported
    /// enemies are <see cref="CharacterBody2D"/> and <see cref="EnemyStateMachine"/> applies its own
    /// gravity (<c>World.U(9.81f)</c>), so a fixture boss with no floor under it falls. That is left
    /// alone: the player falls too (it did in Unity as well - see the comment in
    /// <see cref="FinishedHazardAttack_LeavesAStripThatKeepsBurning"/>), the attack range is 150 px
    /// against a first hit test on the frame the active window opens, and the one test that cares about
    /// height - the strip drop - is the one that measures it deliberately.</description></item>
    /// </list>
    /// </remarks>
    public sealed class GameplayChapterBossTests
    {
        private const string ChapterTwoSceneName = "Chapter02_Orange";
        private const float EntryCutsceneWait = 6f;

        private RainbowChapterBossBehaviour _boss;
        private PlayerMotor2D _player;

        /// <summary>Where a hand-built fixture actor is parented - Unity's implicit "active scene root".</summary>
        private static Node FixtureRoot => GameplayBuildShim.SceneRoot;

        /// <summary>
        /// A strip is a root object that outlives whoever dropped it, and
        /// <see cref="PhaseTwoOnlyHazard_WaitsForPhaseTwo"/> ends with one still burning. Swept both ends,
        /// the way <see cref="GameplayHazardStripRegressionTests"/> does, so a burn cannot cross a test
        /// boundary in either direction.
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

            _boss = null;
            _player = null;
        }

        /// <summary>
        /// Unity swept <c>FindObjectsByType&lt;BossHazardStrip&gt;</c>; <see cref="SceneQuery.FindAll{T}"/>
        /// is the ported whole-tree walk that stands in for it.
        /// </summary>
        private static void DestroyEveryStrip()
        {
            foreach (BossHazardStrip strip in SceneQuery.FindAll<BossHazardStrip>())
            {
                if (GodotObject.IsInstanceValid(strip))
                    strip.QueueFree();
            }
        }

        [Test]
        public async Task AuthoredAttack_ReadsAsTelegraphThenActive_AndDamagesThePlayer()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData data);

            // Unity: boss.transform.position + Vector3.right * 1f. One metre right is 100 px right; X
            // does not flip.
            Health playerHealth = BuildPlayerAt(boss.GlobalPosition + new Vector2(World.U(1f), 0f));

            float before = playerHealth.CurrentHealth;

            Assert.IsTrue(boss.RequestAttack(data.Attacks[0]), "A live boss with a profile in hand should start the attack.");
            Assert.IsTrue(boss.IsAttackRunning, "The attack is running from the frame it is requested.");
            Assert.IsFalse(boss.IsAttackActive, "It has to be read first - an attack that can hit on frame one is not a telegraph.");

            // Damage is unscaled by the port, so 0.01 is still the tolerance it was in Unity.
            Assert.AreEqual(before, playerHealth.CurrentHealth, 0.01f, "Nothing lands during the telegraph.");

            await WaitUntil(() => boss.IsAttackActive || !boss.IsAttackRunning, 2f,
                "The telegraph should have opened into an active window by now.");

            Assert.Less(playerHealth.CurrentHealth, before,
                "An authored attack that never damages a player standing inside its range is a boss made of data and nothing else.");

            await WaitUntil(() => !boss.IsAttackRunning, 2f, "The active window should close on its own.");
            Assert.IsFalse(boss.IsAttackRunning, "Recovery starts when the attack ends, not when the next one is requested.");
        }

        [Test]
        public async Task AttackOutOfRange_MissesWithoutEndingTheSwing()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData data);

            // Well past the profile's 1.5 range, so the swing runs its full length and connects with air.
            // Unity's 9 world units is 900 px.
            Health playerHealth = BuildPlayerAt(boss.GlobalPosition + new Vector2(World.U(9f), 0f));
            float before = playerHealth.CurrentHealth;

            boss.RequestAttack(data.Attacks[0]);

            await WaitUntil(() => !boss.IsAttackRunning, 3f, "The swing should finish whether or not it hit.");

            Assert.AreEqual(before, playerHealth.CurrentHealth, 0.01f,
                "Range is authored per attack; an attack that hits regardless of it makes the number a decoration.");
        }

        [Test]
        public async Task PhaseTwo_StartsAtTheAuthoredThresholdAndOnlyOnce()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData _);
            var health = boss.GetComponent<Health>();

            var phaseTwoCount = 0;

            // UnityEvent.AddListener is a plain C# event subscription here.
            boss.OnPhaseTwoStarted += () => phaseTwoCount++;

            // Health fractions are unscaled - only distances and speeds are.
            health.SetHealth(health.MaxHealth * 0.75f);
            await TestContext.Runner.NextFrame();
            Assert.IsFalse(boss.IsPhaseTwo, "Above the threshold the boss is still in phase one.");

            health.SetHealth(health.MaxHealth * 0.4f);
            await TestContext.Runner.NextFrame();
            Assert.IsTrue(boss.IsPhaseTwo, "Crossing the authored threshold has to start phase two.");
            Assert.AreEqual(1, phaseTwoCount, "Phase two is a transition, not a state that keeps announcing itself.");

            health.SetHealth(health.MaxHealth * 0.2f);
            await TestContext.Runner.NextFrame();
            Assert.AreEqual(1, phaseTwoCount, "Taking more damage inside phase two must not raise it again.");
        }

        /// <summary>
        /// Breaking the boss's poise has to interrupt the swing, not merely queue a stun behind it.
        /// Driven through <see cref="EnemyStateMachine.OnPoiseBroken"/> - the same entry point
        /// <see cref="CombatResolver"/> uses - because that is deterministic, where racing a real parry
        /// window against a telegraph would be a coin flip on a slow batchmode frame.
        /// </summary>
        [Test]
        public async Task BreakingPoiseMidTelegraph_DropsTheSwing()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData data);
            Health playerHealth = BuildPlayerAt(boss.GlobalPosition + new Vector2(World.U(1f), 0f));
            float before = playerHealth.CurrentHealth;

            boss.RequestAttack(data.Attacks[0]);
            Assert.IsTrue(boss.IsAttackRunning, "The swing has to be in the air before it can be interrupted.");

            boss.OnPoiseBroken();

            Assert.IsTrue(boss.IsStunned, "A broken poise gauge opens the same window a perfect parry does.");
            Assert.IsFalse(boss.IsAttackRunning, "An interrupted swing is dropped, not paused.");

            await WaitForFrames(3);

            Assert.AreEqual(before, playerHealth.CurrentHealth, 0.01f,
                "A swing interrupted during its telegraph must never reach its active window.");
        }

        /// <summary>
        /// The other half of the encounter seam. Killing a chapter boss has to reach the victory
        /// controller, and it reaches it through <see cref="IBossEncounter.Defeated"/> - which is raised
        /// after the death has been read, not on the killing blow, so the panel does not cover the kill
        /// it is celebrating.
        /// </summary>
        [Test]
        public async Task Defeat_AnnouncesItselfThroughTheEncounterSeam_AfterTheDeathIsRead()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData _);
            IBossEncounter seam = boss;

            var defeated = 0;

            // Defeated is a C# event here, not a UnityEvent object: subscribe with +=.
            seam.Defeated += () => defeated++;

            // Damage is unscaled by the port. Vector2.right is Vector2.Right.
            boss.GetComponent<Health>().ApplyDamage(9999f, Vector2.Right);
            await TestContext.Runner.NextFrame();

            Assert.IsTrue(boss.IsDefeated, "A lethal hit should kill the boss.");
            Assert.AreEqual(0, defeated, "The announcement waits out the presentation delay; firing on the blow buries the kill under the panel.");

            await WaitUntil(() => defeated > 0, 4f,
                "Defeat has to reach the seam, or beating a chapter boss wins nothing at all.");

            Assert.AreEqual(1, defeated, "Victory happens once.");
        }

        /// <summary>
        /// The end of the road this layer was built for: the spawner produces a fightable chapter boss
        /// from authored data alone. Everything the arena's boss needs to be a real combat participant
        /// rather than scenery with a health bar - poise to break, souls to pay out, a damage receiver -
        /// has to arrive with it, and none of it is in the behaviour.
        /// </summary>
        [Test]
        public void Spawner_BuildsAFightableBoss_FromAuthoredDataAlone()
        {
            RainbowChapterBossData data = BuildBossData();

            // PORT CHANGE: Unity's first argument was LayerMask.NameToLayer("Enemy"). The ported
            // CreateEnemyRoot sets World.Layer.Enemy itself, so the parameter is gone.
            // The position converts: Unity (5, 1) metres, +Y up -> Godot (500, -100) px, +Y down.
            _boss = GameplayEnemySpawner.CreateChapterBoss(
                World.V(new Vector2(5f, 1f)),
                GameplayReadabilityDefaults.Create(),
                data,
                null);

            Assert.IsInstanceOf<IBossEncounter>(_boss, "The spawned boss has to be bindable as an encounter.");

            // PORT CHANGE: asserted through IBossEncounter.BossName, not the node name. Unity let two
            // GameObjects share a name; Godot makes a sibling name unique on the spot, so a second boss
            // in the tree - a leftover from an earlier test, or a real second spawn - silently becomes
            // "TestChapterBoss2". BossName reads the authored data and is what the HUD and the probe
            // actually report, which is what this test was always about.
            Assert.AreEqual(data.BossName, ((IBossEncounter)_boss).BossName,
                "The authored name should reach the object; it is what the HUD and the probe report.");

            var health = _boss.GetComponent<Health>();
            Assert.NotNull(health, "A boss with no health cannot be beaten.");
            Assert.AreEqual(data.MaxHealth, health.MaxHealth, 0.01f, "Health comes from the authored data, not a default.");

            var poise = _boss.GetComponent<Poise>();
            Assert.NotNull(poise, "Without poise the boss can never be staggered, so heavy attacks stop meaning anything against it.");

            var wallet = _boss.GetComponent<SoulsWallet>();
            Assert.NotNull(wallet, "The wallet is the drop table; without one the kill pays nothing.");
            Assert.AreEqual(data.SoulReward, wallet.Souls, "The kill should be worth what the data says.");

            Assert.NotNull(_boss.GetComponent<DamageReceiver>(), "CombatResolver routes damage through the receiver.");
            Assert.NotNull(_boss.GetComponent<GameplayWorldHealthBar>(), "A boss with no bar gives the player no read on the fight.");
        }

        /// <summary>
        /// Chapter two's mechanic, and the half of it a suite can see. The lunge is answerable; the strip
        /// it leaves is what makes the ground cost something. A strip that appears but never resolves
        /// damage would look right in the editor and change nothing about the fight.
        /// </summary>
        [Test]
        public async Task FinishedHazardAttack_LeavesAStripThatKeepsBurning()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData data, HazardBossJson(phaseTwoOnly: false));

            // Unity: + Vector3.right * 0.5f -> half a metre right is 50 px right.
            Health playerHealth = BuildPlayerAt(boss.GlobalPosition + new Vector2(World.U(0.5f), 0f));

            boss.RequestAttack(data.Attacks[0]);
            await WaitUntil(() => !boss.IsAttackRunning, 3f, "The swing should finish on its own.");

            BossHazardStrip strip = boss.LastHazard;
            Assert.NotNull(strip, "An attack authored to leave a strip has to leave one when it runs to its end.");

            // After the swing, so the strip's damage is measured on its own rather than on top of the
            // hit that dropped it.
            float afterSwing = playerHealth.CurrentHealth;

            Vector2 stripPos = strip.GlobalPosition;

            // Already in pixels - BossAttackProfile.ScaleToPixels converted the authored 1.5 m.
            float radius = strip.Radius;

            // Read inside the loop: the count dies with the strip, so sampling it after the object has
            // been destroyed would report zero for a strip that burned correctly and then expired.
            //
            // The player is pinned to the strip for the same reason it is placed by hand in the first
            // place: this fixture has no floor, so a player left alone falls out of the arena in about a
            // second. "Standing in it" is the condition under test, and holding the position is how a
            // ground-less fixture states it.
            //
            // PORT NOTE: the velocity is zeroed with the position. Unity's rigidbody was re-driven from
            // the motor every step too, but a CharacterBody2D integrates whatever Velocity it is left
            // holding, so pinning the position alone would let a second of accumulated fall speed carry
            // the body back out of the strip between the pin and the strip's own tick.
            var ticks = 0;
            float deadline = GameClock.Time + 2f;
            while (GameClock.Time < deadline)
            {
                if (!GodotObject.IsInstanceValid(strip))
                    break;

                _player.GlobalPosition = stripPos;
                _player.Velocity = Vector2.Zero;

                ticks = strip.TickCount;
                if (ticks > 0)
                    break;

                await TestContext.Runner.NextFrame();
            }

            bool expired = !GodotObject.IsInstanceValid(strip);

            Assert.Greater(ticks, 0,
                "A strip the player is standing in has to burn them. " +
                $"expired={expired} ticks={ticks} radius={radius} stripPos={stripPos} " +
                $"playerPos={_player.GlobalPosition} bossPos={boss.GlobalPosition}");

            Assert.Less(playerHealth.CurrentHealth, afterSwing,
                "A strip that never resolves damage is a decal, not a hazard.");

            await WaitUntil(() => !GodotObject.IsInstanceValid(strip), 4f,
                "A strip that never expires denies the ground for the rest of the fight.");
        }

        /// <summary>
        /// The strip is the payoff for a commitment the boss finished. Dropping one from a swing the
        /// player interrupted would hand the boss its ground denial for free, which is the opposite of
        /// what a punish window is.
        /// </summary>
        [Test]
        public async Task InterruptedHazardAttack_LeavesNothingBehind()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData data, HazardBossJson(phaseTwoOnly: false));

            boss.RequestAttack(data.Attacks[0]);
            Assert.IsTrue(boss.IsAttackRunning, "The swing has to be in the air before it can be interrupted.");

            boss.OnPoiseBroken();
            await WaitForFrames(3);

            Assert.IsNull(boss.LastHazard, "A swing that was broken never earned its strip.");
        }

        /// <summary>
        /// The phase gate, which is the whole shape of the fight: phase one teaches the attack, phase two
        /// adds what standing near it costs. A strip that appears in phase one makes the two halves the
        /// same fight.
        /// </summary>
        [Test]
        public async Task PhaseTwoOnlyHazard_WaitsForPhaseTwo()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData data, HazardBossJson(phaseTwoOnly: true));

            boss.RequestAttack(data.Attacks[0]);
            await WaitUntil(() => !boss.IsAttackRunning, 3f, "The swing should finish on its own.");
            Assert.IsNull(boss.LastHazard, "A phase-two strip must not appear while the boss is still in phase one.");

            var health = boss.GetComponent<Health>();
            health.SetHealth(health.MaxHealth * 0.4f);
            await TestContext.Runner.NextFrame();
            Assert.IsTrue(boss.IsPhaseTwo, "The fixture threshold is 0.5, so 40% has to be phase two.");

            boss.RequestAttack(data.Attacks[0]);
            await WaitUntil(() => !boss.IsAttackRunning, 3f, "The second swing should finish too.");

            Assert.NotNull(boss.LastHazard, "Once phase two starts, the same attack has to start leaving strips.");
        }

        /// <summary>
        /// Every authored chapter, checked the only way a chain of string references can be checked. A
        /// chapter is a scene plus three JSON files that name each other by name, and every link fails
        /// the same silent way: the spawner logs an error and serves chapter one, which is a fight that
        /// works and is the wrong one.
        ///
        /// Driven off the scene folder rather than a list here, so a chapter added without a test is
        /// still covered and a chapter deleted does not leave a test naming a ghost.
        /// </summary>
        /// <remarks>
        /// PORT CHANGE: Unity enumerated Build Settings. Godot has no build-settings scene list, and
        /// <see cref="ChapterRoute.Scenes"/> is the ported enumeration of <c>res://Scenes/*.tscn</c> that
        /// the shipped code itself routes off. It already drops <c>TitleScene</c>; the
        /// <c>StartsWith("Chapter")</c> filter is kept exactly as Unity wrote it, which is also what
        /// skips chapter one's differently-named <c>GameplayScene</c>.
        /// </remarks>
        [Test]
        public void EveryChapterScene_NamesABossAndAnEncounterThatLoad()
        {
            GameplayTuningCatalog catalog = GameplayTuningCatalog.Load();
            var checkedChapters = 0;

            foreach (string sceneName in ChapterRoute.Scenes())
            {
                if (!sceneName.StartsWith("Chapter"))
                    continue;

                checkedChapters++;

                GameplaySceneDefaults scene = GameplaySceneDefaults.Create();
                catalog.SceneLayoutFor(sceneName).ApplyTo(scene);

                Assert.IsNotEmpty(scene.BossDataFile,
                    $"{sceneName} names no boss, so the arena would serve chapter one's WrathMiniBoss.");

                RainbowChapterBossData boss = catalog.ChapterBoss(scene.BossDataFile);
                Assert.NotNull(boss, $"{sceneName} names boss data '{scene.BossDataFile}', which is not in Resources/Design.");
                Assert.IsNotEmpty(boss.BossName, $"{sceneName}'s boss has no name; the HUD and the probe both report it.");
                Assert.Greater(boss.Attacks.Length, 0, $"{boss.BossName} has no attacks, so the fight is a health bar that walks.");

                Assert.IsNotEmpty(scene.BossEncounterFile,
                    $"{sceneName} names no encounter, so it borrows chapter one's arena reach and punish window.");
                Assert.NotNull(catalog.ChapterEncounter(scene.BossEncounterFile),
                    $"{sceneName} names encounter '{scene.BossEncounterFile}', which is not in Resources/Design.");

                foreach (BossAttackProfile attack in boss.Attacks)
                {
                    Assert.IsNotEmpty(attack.AttackId, $"{boss.BossName} has an attack with no id; a chain cannot name it.");

                    if (attack.HasChain)
                    {
                        Assert.NotNull(boss.FindAttack(attack.ChainNextAttackId),
                            $"{boss.BossName}'s '{attack.AttackId}' chains to '{attack.ChainNextAttackId}', which is not one " +
                            "of its attack ids. The chain would end there with only a console warning to say so.");
                    }
                }
            }

            Assert.Greater(checkedChapters, 0,
                "No chapter scenes are on the scene route, so this checked nothing at all.");
        }

        /// <summary>
        /// The claim itself: chapter two is a scene the game can load, and the thing standing in its
        /// arena is the authored Ember Pilgrim.
        ///
        /// Every other test here builds its boss by hand, so all of them would stay green with the four
        /// chapter-two files deleted. The spawner's fallback is silent by design - a boss file that does
        /// not load logs an error and serves chapter one, which is a fight that works and is the wrong
        /// one. Asserting no <see cref="WrathMiniBoss"/> is what catches that.
        /// </summary>
        [Test]
        public async Task ChapterTwoScene_SpawnsTheAuthoredBoss()
        {
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterTwoSceneName));

            // The entry cutscene runs unscaled, so this is polled on real time the way the other
            // scene-loading suites do it. Unity walked the tree for a CutsceneDirector; the ported
            // director publishes itself as a singleton.
            CutsceneDirector director = CutsceneDirector.Instance;
            if (director != null)
            {
                await TestContext.Runner.WaitUntil(() => !director.IsPlaying, EntryCutsceneWait);
            }

            var boss = SceneQuery.FindFirst<RainbowChapterBossBehaviour>();
            Assert.NotNull(boss, "Chapter two's arena has to build the authored chapter boss, not nothing.");

            Assert.IsNull(SceneQuery.FindFirst<WrathMiniBoss>(),
                "A WrathMiniBoss in this arena means the boss file failed to load and the spawner fell back to chapter one.");

            Assert.AreEqual("Ember Pilgrim", boss.BossName, "The arena should be standing up the boss its layout names.");
            Assert.AreEqual(RainbowChapterColor.Orange, boss.ChapterColor, "Chapter two is the orange chapter.");
            Assert.AreEqual(3, boss.BossData.Attacks.Length, "The Ember Pilgrim is authored with three attacks.");

            var health = boss.GetComponent<Health>();
            Assert.NotNull(health, "A boss with no health cannot be beaten.");
            Assert.AreEqual(boss.BossData.MaxHealth, health.MaxHealth, 0.01f,
                "Health has to come off the authored file, not a component default.");

            Assert.NotNull(boss.GetComponent<Poise>(), "Without poise the boss can never be staggered.");
            Assert.AreEqual(boss.BossData.SoulReward, boss.GetComponent<SoulsWallet>().Souls,
                "The kill should be worth what the file says.");

            // Load something else back: leaving the chapter-two arena open would hand the next test in
            // the run a scene it did not ask for.
            await TestContext.Runner.LoadScene(ChapterRoute.ScenePath(ChapterRoute.DefaultFirstChapterScene));
        }

        /// <summary>
        /// The seam that lets a chapter boss open the way chapter one does. A listener holds the intro
        /// from inside the event, the boss waits, and the fight starts when the hold is released - the
        /// same contract <see cref="WrathMiniBoss"/> offers, now behind <see cref="IBossEncounter"/> so the
        /// cutscene trigger can bind either.
        /// </summary>
        [Test]
        public async Task Intro_HoldsForItsListener_AndStartsTheFightWhenReleased()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData _);
            Assert.IsTrue(boss.IsInIntro, "A boss the player has not reached yet has not arrived.");

            IBossEncounter seam = boss;
            var introCount = 0;
            seam.IntroStarted += () =>
            {
                introCount++;

                // Called synchronously from inside the event, the way GameplayCutsceneTriggers does it:
                // the intro sequence reads the flag further down its own body.
                seam.HoldIntro();
            };

            // The arrival waits on a player being seen, not on _Ready. Unity's 3 units right is 300 px.
            BuildPlayerAt(boss.GlobalPosition + new Vector2(World.U(3f), 0f));

            await WaitUntil(() => introCount > 0, 2f, "Seeing the player should begin the arrival.");
            Assert.IsTrue(boss.IsInIntro, "A held intro stays open until its listener lets go.");

            await WaitForFrames(5);
            Assert.IsTrue(boss.IsInIntro, "Frames passing must not release a hold nobody released.");

            seam.ReleaseIntroHold();

            await WaitUntil(() => !boss.IsInIntro, 2f, "Releasing the hold should start the fight.");
            Assert.AreEqual(1, introCount, "The arrival happens once, not on every frame the player is visible.");
        }

        [Test]
        public async Task Intro_ReleasesItselfWhenNobodyIsListening()
        {
            RainbowChapterBossBehaviour boss = BuildBoss(out RainbowChapterBossData _);
            BuildPlayerAt(boss.GlobalPosition + new Vector2(World.U(3f), 0f));

            // No listener, so no hold: the boss must not stand still waiting for a cutscene that does
            // not exist. This is the state the P0 runner and any scene without a director are in.
            await WaitUntil(() => !boss.IsInIntro, 2f,
                "With nobody holding it, the arrival has to end on its own.");
        }

        // ------------------------------------------------------------------------------------------

        private static async Task WaitForFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
                await TestContext.Runner.NextFrame();
        }

        private static async Task WaitUntil(Func<bool> done, float seconds, string message)
        {
            Assert.IsTrue(await TestContext.Runner.WaitUntil(done, seconds), message);
        }

        /// <summary>
        /// One attack, deliberately fast, so the tests above are not waiting on feel numbers. Everything
        /// that matters to them - range, damage, the length of each window - is authored here rather than
        /// read out of a shipped file, so retuning a real boss never turns these red.
        ///
        /// Authored the way a designer would author it - as JSON, in Unity metres - and then put through
        /// the same <c>OnValidate</c> + <c>ScaleToPixels</c> pass <see cref="RainbowChapterBossData.Load"/>
        /// runs, which is what turns "range 1.5" into 150 px.
        /// </summary>
        private static RainbowChapterBossData BuildBossData(string json = null)
        {
            json ??= "{\"maxHealth\":200,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                     "\"phaseTwoHealthThreshold\":0.5,\"phaseTwoSpeedMultiplier\":1.2,\"phaseTwoCooldownMultiplier\":0.75," +
                     "\"maxPoise\":90,\"poiseHeavyMultiplier\":2,\"poiseRegenDelay\":3,\"poiseRegenRate\":30,\"soulReward\":275," +
                     "\"bossName\":\"TestChapterBoss\",\"chapterName\":\"Test Chapter\"," +
                     "\"attacks\":[{\"attackId\":\"test_swing\",\"damage\":12,\"knockback\":0," +
                     "\"telegraphTime\":0.25,\"activeTime\":0.25,\"recoveryTime\":0.25,\"range\":1.5,\"forwardOffset\":0.5," +
                     "\"phaseTwoWeight\":1}]}";

            // PORT CHANGE: ScriptableObject.CreateInstance + JsonUtility.FromJsonOverwrite becomes one
            // JsonData.FromJson, then the two passes Load() would have run. Skipping them would leave
            // every distance in this fixture measured in metres.
            RainbowChapterBossData data = JsonData.FromJson<RainbowChapterBossData>(json);
            Assert.NotNull(data, "The fixture JSON has to parse.");
            data.OnValidate();
            data.ScaleToPixels();

            Assert.AreEqual(1, data.Attacks.Length, "The fixture boss should carry exactly one authored attack.");
            return data;
        }

        /// <summary>
        /// The same fixture boss, with its one attack authored to leave a strip. Short and cheap on
        /// purpose - a burn that outlived the test would keep ticking into the next one.
        /// </summary>
        private static string HazardBossJson(bool phaseTwoOnly)
        {
            return "{\"maxHealth\":200,\"moveSpeed\":2,\"detectionRange\":8,\"attackRange\":1.5," +
                   "\"phaseTwoHealthThreshold\":0.5,\"phaseTwoSpeedMultiplier\":1.2,\"phaseTwoCooldownMultiplier\":0.75," +
                   "\"maxPoise\":90,\"poiseHeavyMultiplier\":2,\"poiseRegenDelay\":3,\"poiseRegenRate\":30,\"soulReward\":275," +
                   "\"bossName\":\"TestHazardBoss\",\"chapterName\":\"Test Chapter\"," +
                   "\"attacks\":[{\"attackId\":\"test_lunge\",\"damage\":12,\"knockback\":0," +
                   "\"telegraphTime\":0.25,\"activeTime\":0.25,\"recoveryTime\":0.25,\"range\":1.5,\"forwardOffset\":0.5," +
                   "\"phaseTwoWeight\":1,\"leavesHazard\":true,\"hazardPhaseTwoOnly\":" + (phaseTwoOnly ? "true" : "false") +
                   ",\"hazardDamage\":5,\"hazardRadius\":1.5,\"hazardTickInterval\":0.2,\"hazardDuration\":1.2," +
                   "\"hazardForwardOffset\":0.5}]}";
        }

        /// <summary>
        /// Unity's <c>new GameObject</c> + three <c>AddComponent</c> calls, as a node graph. The whole
        /// hierarchy is assembled detached and entered in one piece, because
        /// <c>RainbowChapterBossBehaviour._Ready</c> subscribes to a <see cref="Health"/> it expects to
        /// already be a sibling.
        /// </summary>
        private RainbowChapterBossBehaviour BuildBoss(out RainbowChapterBossData data, string json = null)
        {
            data = BuildBossData(json);

            _boss = new RainbowChapterBossBehaviour
            {
                Name = "ChapterBossFixture",
                Position = Vector2.Zero,
            };

            // Unity added a Kinematic Rigidbody2D here. The ported boss IS the body, and its "kinematic"
            // is EnemyStateMachine's own gravity + MoveAndSlide - see the class remarks.
            _boss.AddComponent<Health>();

            FixtureRoot.AddChild(_boss);

            _boss.SetBossData(data);
            return _boss;
        }

        /// <summary>
        /// Unity built the player as a GameObject carrying a collider, a kinematic body, Health, a
        /// <see cref="PlayerController2D"/> and a <see cref="DamageReceiver"/>. Here the body is the
        /// <see cref="PlayerMotor2D"/> and everything else is a child of it - which is also the shape
        /// <c>CombatResolver</c> reads back when it looks for the receiver and the damage guard.
        /// </summary>
        private Health BuildPlayerAt(Vector2 position)
        {
            _player = new PlayerMotor2D
            {
                Name = "ChapterBossTestPlayer",
                Position = position,
                CollisionLayer = World.Layer.Player,

                // Nothing to stand on in this fixture, exactly as in Unity: an empty mask keeps the body
                // from catching on whichever scene happens to be open behind the test.
                CollisionMask = 0,
            };

            // Unity: CircleCollider2D with radius 0.4 -> 40 px.
            _player.AddChild(new CollisionShape2D
            {
                Name = "Collider",
                Shape = new CircleShape2D { Radius = World.U(0.4f) },
            });

            Health health = _player.AddComponent<Health>();
            DamageReceiver receiver = _player.AddComponent<DamageReceiver>();

            // PlayerController2D wires its motor off the body in _Ready; without one the fixture works
            // but for a different reason than the shipped player does.
            _player.AddComponent<PlayerController2D>();

            FixtureRoot.AddChild(_player);

            // After the node is in the tree: Health._Ready resets current to max, so configuring before
            // it entered would be overwritten. Health values are unscaled.
            health.SetMaxHealth(100f);
            health.SetHealth(100f);
            receiver.Initialize(health);

            return health;
        }
    }
}
